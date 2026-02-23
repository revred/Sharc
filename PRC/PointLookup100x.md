# PointLookup 100x: Zero-Alloc PreparedReader + Seek Devirtualization

## Context

PointLookup is currently **97x** faster than SQLite (282 ns vs 27.4 μs) but allocates **640 B** per call because the benchmark creates a new `SharcDataReader` + `BTreeCursor` every iteration. The infrastructure for zero-alloc reuse already exists (`ResetForReuse`, `BTreeCursor.Reset`, `MarkReusable`) — it's used by `PreparedQuery` for SQL paths — but no public API exposes it for direct table access without SQL parsing.

**Goal**: ≥100x (≤274 ns), 0 B steady-state allocation.

## Changes

### 1. New: `PreparedReader` class (`src/Sharc/PreparedReader.cs`)

Lightweight handle that caches a reader+cursor for repeated Seek/Read calls on one table.

- Constructor takes db, cursor, reader — calls `reader.MarkReusable()`
- `CreateReader()` → calls `ResetForReuse(null)`, returns the cached reader (0 B)
- `Dispose()` → calls `DisposeForReal()` + cursor Dispose

~40 lines. Much simpler than PreparedQuery (no SQL, no filters, no param cache).

### 2. New: `SharcDatabase.PrepareReader()` factory (`src/Sharc/SharcDatabase.cs`)

```csharp
public PreparedReader PrepareReader(string tableName, params string[]? columns)
```

Resolves schema + projection once, creates cursor + reader, wraps in `PreparedReader`. Insert after line 420 (after existing `CreateReader` overloads).

### 3. Devirtualize `Seek()` (`src/Sharc/SharcDataReader.cs:519-536`)

Replace current interface-dispatch Seek with ScanMode switch (matching `Read()` pattern):

```csharp
public bool Seek(long rowId)
{
    return DispatchMode switch
    {
        ScanMode.TypedCached => SeekTyped(_btreeCachedCursor!, rowId),
        ScanMode.TypedMemory => SeekTyped(_btreeMemoryCursor!, rowId),
        ScanMode.Disposed => throw new ObjectDisposedException(GetType().FullName),
        _ => SeekDefault(rowId),
    };
}
```

Eliminates 2 interface dispatches per Seek (Seek + Payload), saves ~4-8 ns.

### 4. Update benchmark (`bench/Sharc.Comparisons/CoreBenchmarks.cs`)

- Add `PreparedReader _preparedReader` field, initialized in Setup, disposed in Cleanup
- `Sharc_PointLookup` → uses `_preparedReader.CreateReader()` (0 B, ~150 ns)
- `Sharc_PointLookup_Cold` → current code with `CreateReader` (640 B, backward compat)
- `Sharc_BatchLookup` → also uses `_preparedReader.CreateReader()`

### 5. Tests (`tests/Sharc.IntegrationTests/PreparedReaderTests.cs`)

~15 tests: lifecycle (create, dispose, double-dispose), Seek correctness (hit, miss, multiple seeks, first/last row), reuse (same instance returned, consistent results), projection, Read() scan after reuse.

## Allocation Analysis

| Phase          | Before                                        | After                |
|----------------|-----------------------------------------------|----------------------|
| Per-call       | 640 B (reader 176 + cursor 192 + ArrayPool 272) | 0 B               |
| First call only | 640 B                                         | 640 B (same, cached) |

## Timing Estimate

| Component                    | Before     | After               |
|------------------------------|------------|----------------------|
| Object alloc + constructor   | ~100 ns    | 0 (reused)           |
| Schema lookup                | ~20 ns     | 0 (pre-resolved)     |
| ResetForReuse                | N/A        | ~5 ns                |
| B-tree Seek (devirtualized)  | ~90 ns     | ~82 ns               |
| DecodeCurrentRow + GetInt64  | ~40 ns     | ~38 ns               |
| Dispose                      | ~25 ns     | ~3 ns (no-op)        |
| **Total**                    | **~282 ns** | **~128-150 ns**     |
| **vs SQLite (27.4 μs)**     | **97x**    | **~183-214x**        |

## Files

| File                                              | Action                     | Lines |
|---------------------------------------------------|----------------------------|-------|
| `src/Sharc/PreparedReader.cs`                     | NEW                        | ~40   |
| `src/Sharc/SharcDatabase.cs`                      | ADD method after line 420  | ~25   |
| `src/Sharc/SharcDataReader.cs`                    | REPLACE lines 519-536      | ~30   |
| `bench/Sharc.Comparisons/CoreBenchmarks.cs`       | MODIFY benchmarks          | ~20   |
| `tests/Sharc.IntegrationTests/PreparedReaderTests.cs` | NEW                    | ~150  |

## Implementation Order

1. Write `PreparedReaderTests.cs` (RED)
2. Create `PreparedReader.cs`
3. Add `PrepareReader()` to `SharcDatabase.cs`
4. Tests → GREEN
5. Devirtualize `Seek()` in `SharcDataReader.cs`
6. All tests → still GREEN
7. Update benchmark
8. Run `*CoreBenchmarks*PointLookup*` → verify 0 B + ≤200 ns

## What Does NOT Change

- `SharcDatabase.CreateReader()` — unchanged, backward compatible
- `SharcDataReader` object size — still 176 B
- `BTreeCursor` object size — still ~192 B
- `PreparedQuery` — unchanged
- Public API surface — only additions, no breaks
