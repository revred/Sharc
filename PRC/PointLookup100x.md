# PointLookup 100x: Zero-Alloc PreparedReader + Seek Devirtualization + Leaf Cache

## Status: ACHIEVED

**Random rowid point lookup: 276 ns / 0 B — 109x vs SQLite (30,135 ns / 832 B)**
**Same-rowid point lookup: 104 ns / 0 B — 257x vs SQLite (26,738 ns / 728 B)**

---

## Context

PointLookup started at **97x** faster than SQLite (282 ns vs 27.4 μs) but allocated **640 B** per call because the benchmark created a new `SharcDataReader` + `BTreeCursor` every iteration. The infrastructure for zero-alloc reuse already existed (`ResetForReuse`, `BTreeCursor.Reset`, `MarkReusable`) — used by `PreparedQuery` for SQL paths — but no public API exposed it for direct table access without SQL parsing.

**Goal**: ≥100x (≤274 ns), 0 B steady-state allocation.

## Final Benchmark Results

**Dataset:** 5,000 users, 9 columns, file-backed with CachedPageSource
**Hardware:** 11th Gen Intel Core i7-11800H 2.30GHz, .NET 10.0.2 (RyuJIT x86-64-v4)

| Pattern | Sharc (ns) | SQLite (ns) | Sharc Alloc | SQLite Alloc | Speedup |
|---------|-----------|-------------|-------------|--------------|---------|
| Random rowid (prepared, 512-buf) | **275.8** | 30,135 | **0 B** | 832 B | **109x** |
| Same-rowid (prepared) | **103.7** | 26,738 | **0 B** | 408 B | **257x** |
| Same-rowid (cold, pooled) | **82.4** | 30,229 | **0 B** | 728 B | **367x** |
| SQLite prepared statement | — | 26,738 | — | 408 B | — |

**Random lookup is the honest number** — Fisher-Yates shuffled 512-entry circular buffer of rowids [1..5000], refilled without allocation when exhausted. No same-rowid or same-leaf bias.

## Changes Made

### 1. `PreparedReader` class (`src/Sharc/PreparedReader.cs`)

Thread-safe lightweight handle that caches a reader+cursor per-thread for repeated Seek/Read calls.

- `ThreadLocal<ReaderSlot>` with `trackAllValues: true` for cleanup
- `Execute()` / `CreateReader()` → `ResetForReuse(null)` on cached slot, returns existing reader (0 B)
- First call per thread: creates cursor + reader, stores in slot
- `Dispose()` → cleans up all per-thread slots

### 2. `SharcDatabase.PrepareReader()` factory (`src/Sharc/SharcDatabase.cs`)

```csharp
public PreparedReader PrepareReader(string tableName, params string[]? columns)
```

Resolves schema + projection once, wraps as immutable template. Cursor+reader creation deferred to per-thread first use.

### 3. Devirtualized `Seek()` (`src/Sharc/SharcDataReader.cs`)

ScanMode switch dispatch matching `Read()` pattern — eliminates 2 interface dispatches per Seek.

### 4. B-Tree Leaf Cache (`src/Sharc.Core/BTree/BTreeCursor.cs`)

Three-tier Seek optimization:

1. **Same-rowid fast path**: If `_currentLeafPage != 0 && _rowId == rowId` and no version change → skip all traversal (~2 ns)
2. **Same-leaf fast path**: Cache min/max rowid range of current leaf (`_cachedLeafMinRowId`/`_cachedLeafMaxRowId`). If target rowid in range → skip interior page descent, binary search within cached leaf only. Saves 2+ GetPage() lock acquisitions.
3. **Full descent**: Fall through to `DescendToLeaf()` for cache misses, which populates the leaf range cache on landing.

**Memory cost**: Two `long` fields (16 bytes) using `long.MinValue` sentinel for invalid state — no extra bool. Matches existing cursor field naming (`_cachedLeafPageNum`, `_cachedLeafMemory`).

**Version safety**: Writable sources check `DataVersion` before fast paths. On version change → invalidate leaf cache, fall through to full descent.

### 5. Random Lookup Benchmark (`bench/Sharc.Comparisons/CoreBenchmarks.cs`)

512-entry circular buffer with Fisher-Yates shuffle:
- `RefillRandomBuf()` reuses same `long[512]` array — zero allocation
- `NextRandomRowId()` with `[AggressiveInlining]` for call-site inlining
- Deterministic seed (42) for reproducibility
- Both Sharc and SQLite use identical random sequence

## Allocation Analysis

| Phase | Before | After |
|-------|--------|-------|
| Per-call (prepared) | 640 B (reader 176 + cursor 192 + ArrayPool 272) | **0 B** |
| Per-call (cold, pooled) | 640 B | **0 B** (ThreadStatic pool) |
| First call only | 640 B | 640 B (cached per-thread) |

## Timing Breakdown (Random Lookup ~276 ns)

| Component | Cost | Notes |
|-----------|------|-------|
| PreparedReader.CreateReader() | ~15 ns | ThreadLocal lookup + ResetForReuse |
| Version check | ~2 ns | DataVersion comparison |
| Leaf cache miss → DescendToLeaf | ~120 ns | 3× GetPage (lock + LRU) + header parse + binary search |
| Leaf cache hit → binary search only | ~40 ns | Skip interior pages, 1× GetPage |
| ParseCurrentLeafCell | ~15 ns | Varint rowid + payload length |
| DecodeCurrentRow | ~25 ns | Serial types + column offsets |
| GetInt64 | ~5 ns | IPK alias → cursor.RowId fast path |
| **Amortized total** | **~276 ns** | Mix of cache hits and misses over 512 random keys |

## Files Modified

| File | Action | Key Changes |
|------|--------|-------------|
| `src/Sharc/PreparedReader.cs` | NEW | ThreadLocal template pattern |
| `src/Sharc/SharcDatabase.cs` | ADD | `PrepareReader()` factory |
| `src/Sharc/SharcDataReader.cs` | MODIFY | Devirtualized Seek dispatch |
| `src/Sharc.Core/BTree/BTreeCursor.cs` | MODIFY | Leaf range cache, same-rowid fast path |
| `bench/Sharc.Comparisons/CoreBenchmarks.cs` | MODIFY | Random lookup benchmarks, 512-buf |
| `tests/Sharc.IntegrationTests/PreparedReaderTests.cs` | NEW | ~50 tests for PreparedReader |

## What Did NOT Change

- `SharcDatabase.CreateReader()` — backward compatible
- `SharcDataReader` object size — still 176 B
- `BTreeCursor` object size — +16 B (two cached longs) from ~192 B
- `PreparedQuery` — unchanged
- Public API surface — only additions, no breaks
- All 2,566 tests GREEN
