# Multi-Agent Access: DataVersion & IsStale

## Overview

Sharc supports multiple agents (readers and writers) sharing a single in-memory database. The `DataVersion` and `IsStale` primitives provide **passive change detection** — a reader can detect when data has been modified since it opened, without polling, locking, or exceptions.

This is a prerequisite for AI multi-agent coordination (e.g., agent chess, collaborative editing) where agents need to know when their view of the data is outdated.

## The Two Primitives

### IPageSource.DataVersion

```csharp
public interface IPageSource : IDisposable
{
    long DataVersion => 0;  // default: unknown/untracked
}
```

A monotonically increasing counter that changes on every data mutation.

| Source Type | DataVersion Behavior |
|---|---|
| `MemoryPageSource` | Starts at 1, increments on every `WritePage()` via `Interlocked.Increment` |
| `CachedPageSource` | Delegates to `_inner.DataVersion` |
| `ProxyPageSource` | Delegates to `_target.DataVersion` |
| `ShadowPageSource` | Composite: `_baseSource.DataVersion + _shadowVersion` |
| `FilePageSource` | Returns 0 (read-only, no tracking) |
| `SafeMemMapdPageSource` | Returns 0 (read-only) |

**Semantics:**
- `DataVersion == 0` means "unknown/untracked" — the source cannot detect mutations (read-only file-backed sources)
- `DataVersion >= 1` means the source tracks mutations — writable sources always start at 1
- Thread-safe reads via `Interlocked.Read()` — safe for concurrent reader threads

### IBTreeCursor.IsStale

```csharp
public interface IBTreeCursor : IDisposable
{
    bool IsStale { get; }
}
```

A passive property that compares the cursor's snapshot version against the current `DataVersion`.

```csharp
public bool IsStale
{
    get
    {
        long current = _pageSource.DataVersion;
        if (_snapshotVersion == 0 || current == 0) return false;
        return current != _snapshotVersion;
    }
}
```

**Semantics:**
- `IsStale` is a **property check**, not a method call. No exceptions, no side effects.
- Returns `false` when either version is 0 (untracked source)
- Returns `true` when the page source has been mutated since the cursor was created/reset/seeked
- The caller decides what to do: reset, dispose, or continue reading stale data

**Version refresh points:**
- `BTreeCursor(...)` constructor — captures initial snapshot
- `Reset()` — refreshes snapshot, clears cached leaf page
- `Seek(rowId)` — refreshes snapshot, navigates to new leaf

### SharcDataReader.IsStale

The public API surface exposes `IsStale` via the reader:

```csharp
public bool IsStale => _cursor?.IsStale ?? false;
```

## Cached Leaf Page Optimization

### The Problem

During sequential scan, `BTreeCursor` reads the same leaf page for every cell on that page. A typical leaf page holds ~50 cells. Without caching, this means ~50 `GetPage()` calls per leaf — each returning the same data.

### The Solution: GetPageMemory + Leaf Cache

```csharp
// IPageSource default method
ReadOnlyMemory<byte> GetPageMemory(uint pageNumber) => GetPage(pageNumber).ToArray();

// MemoryPageSource zero-copy override
public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber)
{
    int offset = (int)(pageNumber - 1) * PageSize;
    return _data.AsMemory(offset, PageSize);
}
```

`BTreeCursor` caches the current leaf page:

```csharp
private ReadOnlyMemory<byte> _cachedLeafMemory;
private uint _cachedLeafPageNum;

private ReadOnlySpan<byte> GetCachedLeafPage()
{
    if (_currentLeafPage != _cachedLeafPageNum)
    {
        _cachedLeafMemory = _pageSource.GetPageMemory(_currentLeafPage);
        _cachedLeafPageNum = _currentLeafPage;
    }
    return _cachedLeafMemory.Span;
}
```

**Result:** ~49 of every 50 `GetPage()` calls per leaf page are eliminated. For `MemoryPageSource`, the cache holds a zero-copy `ReadOnlyMemory<byte>` view into the backing array.

### Cache + IsStale Interaction

The cached leaf page and staleness detection are **complementary, not conflicting**:

1. **Cache does NOT auto-invalidate** — If an external writer modifies a page, the cached memory still holds the old data. This is correct: `IsStale` is passive, and the cursor remains functional.

2. **Reset() clears the cache** — When the caller detects staleness and calls `Reset()`, both the version snapshot and the cached leaf memory are cleared. The next `MoveNext()` re-fetches from the page source.

3. **Seek() refreshes the cache** — `Seek()` navigates to a new leaf page, which naturally replaces the cached leaf memory with the target leaf.

4. **Independent per-cursor** — Each cursor maintains its own cache and version snapshot. Multiple readers on the same `MemoryPageSource` have independent staleness detection.

### Sequence Diagram: Multi-Agent Read-Write-Detect-Reset

```
Agent A (writer)          MemoryPageSource          Agent B (reader)
    │                     DataVersion=1                   │
    │                          │                          │
    │                          │          CreateCursor()──┤
    │                          │          snapshot=1      │
    │                          │                          │
    │                          │          MoveNext()──────┤
    │                          │          (caches leaf)   │
    │                          │          IsStale=false   │
    │                          │                          │
    ├──WritePage()────────────►│                          │
    │                     DataVersion=2                   │
    │                          │                          │
    │                          │          IsStale=true────┤
    │                          │          (cache holds    │
    │                          │           old data)      │
    │                          │                          │
    │                          │          Reset()─────────┤
    │                          │          snapshot=2      │
    │                          │          cache cleared   │
    │                          │                          │
    │                          │          MoveNext()──────┤
    │                          │          (fetches fresh  │
    │                          │           page data)     │
    │                          │          IsStale=false   │
```

## Multi-Agent Patterns

### Pattern 1: Turn-Based (Agent Chess)

```csharp
// Agent A makes a move
using (var writer = SharcWriter.From(db))
{
    writer.Insert("chess_moves", ...);
}

// Agent B detects the move
if (readerB.IsStale)
{
    // Agent A committed — read the new move
    using var fresh = db.CreateReader("chess_moves", "agent", "move");
    while (fresh.Read()) { /* process moves */ }
}
```

### Pattern 2: Multiple Readers + Single Writer

```csharp
// Multiple agent readers
var readers = new SharcDataReader[N];
for (int i = 0; i < N; i++)
    readers[i] = db.CreateReader("shared_state");

// Writer commits
using (var writer = SharcWriter.From(db))
    writer.Insert("shared_state", ...);

// All readers detect staleness
foreach (var r in readers)
    Assert.True(r.IsStale);  // All true — DataVersion is global
```

### Pattern 3: Forensic Timeline with Trust Ledger

The trust layer provides cryptographic evidence of who did what:

```csharp
// Agent A makes a move and records it
using (var writer = SharcWriter.From(db))
    writer.Insert("chess_moves", ...);
ledger.Append("AgentA: e2e4 (turn 1)", signerA);

// Agent B detects, responds, records
if (readerB.IsStale)
{
    using (var writer = SharcWriter.From(db))
        writer.Insert("chess_moves", ...);
    ledger.Append("AgentB: e7e5 (turn 2)", signerB);
}

// Verify the full chronology
Assert.True(ledger.VerifyIntegrity());  // Hash chain intact
```

## File-Backed vs Memory-Backed Sources

| Feature | MemoryPageSource | FilePageSource |
|---|---|---|
| `DataVersion` | Starts at 1, increments per write | Always 0 |
| `IsStale` | Accurate staleness detection | Always false |
| `GetPageMemory` | Zero-copy (AsMemory) | Copies via ToArray() |
| Multi-agent use | Full support | Not applicable |

File-backed sources return `DataVersion=0` by design — they represent read-only file handles. Multi-agent coordination requires `MemoryPageSource` (or writable sources that override `DataVersion`).

## Scanning Considerations

### Sequential Scan at Scale (100K+ rows)

With the cached leaf page optimization, sequential scans are efficient even at large scales:

- **~50 cells per leaf** → ~1 `GetPageMemory()` call per leaf (not per cell)
- **Zero-copy for MemoryPageSource** → no allocation per leaf transition
- **Interior pages** still use `GetPage()` (not cached, but rare: one per ~50 cells)

### Concurrent Scan + Write

When scanning while another agent writes:

1. **The scan reads a consistent snapshot** — the cursor holds a reference to the page memory that was valid when the leaf was first accessed
2. **IsStale becomes true** after the write commits — the scanner can check periodically
3. **No corruption risk** — `MemoryPageSource` writes are page-atomic, and the cached memory reference is to the same backing array (mutations are visible but structurally safe)
4. **Decision point** — the scanning agent can either:
   - Continue the current scan (accepts stale data for this pass)
   - Abort and re-scan with a fresh cursor (guarantees freshness)

### Index Cursors

`IndexBTreeCursor` follows the same pattern:

- `_snapshotVersion` field, same `IsStale` logic
- Version refreshed in `Reset()` and `SeekFirst()`
- `WithoutRowIdCursorAdapter` delegates `IsStale` to `_inner.IsStale`

## Test Coverage

### Unit Tests (CursorStalenessTests.cs — 12 tests)

| Test | Verifies |
|---|---|
| `IsStale_NewCursor_ReturnsFalse` | Fresh cursor → not stale |
| `IsStale_AfterExternalWrite_ReturnsTrue` | External write → stale |
| `IsStale_AfterReset_ReturnsFalse` | Reset refreshes snapshot |
| `IsStale_AfterSeek_ReturnsFalse` | Seek refreshes snapshot |
| `IsStale_ReadOnlySource_AlwaysFalse` | DataVersion=0 → never stale |
| `IsStale_MultipleWrites_ReturnsTrue` | Multiple writes → still stale |
| `IsStale_WriteAfterReset_ReturnsTrue` | Reset + write → stale again |
| `IsStale_IndexCursor_SameSemantics` | Index cursor works identically |
| `CachedLeafPage_ResetClearsCache_ReadsFreshData` | Reset invalidates cache, re-read gets new data |
| `CachedLeafPage_TwoCursors_IndependentStaleness` | Two cursors detect staleness independently |
| `CachedLeafPage_WriteDoesNotAutoInvalidateCache` | Cache is passive — no auto-invalidation |
| `CachedLeafPage_SeekRefreshesBothVersionAndCache` | Seek refreshes both version and cache |

### Unit Tests (DataVersionTests.cs — 11 tests)

Tests for `IPageSource.DataVersion` across `MemoryPageSource`, `CachedPageSource`, `ProxyPageSource`, and `ShadowPageSource`.

### Unit Tests (GetPageMemoryTests.cs — 4 tests)

Tests for `IPageSource.GetPageMemory` — zero-copy verification, default implementation, cached source delegation.

### Integration Tests (MultiAgentTests.cs — 9 tests)

| Test | Verifies |
|---|---|
| `TwoAgents_Write_CursorDetectsStale` | Writer commits → reader detects stale |
| `TwoAgents_ResetRefreshesData` | Fresh reader sees updated data |
| `DataVersion_SurvivesCommit` | Transaction commit → version increment |
| `ChessGame_TurnBasedWrites` | Full turn-based agent simulation |
| `DataVersion_MemoryPageSource_IncrementsOnWrite` | Direct page source version tracking |
| `ParallelReaders_Writer_StalenessDetected` | Multiple readers all detect staleness |
| `TwoAgents_LedgerChronology` | Trust ledger + alternating agents + integrity verification |
| `CachedLeafPage_AgentWriteInvalidatesCache_FreshReaderSeesUpdatedData` | Full flow: read → write → stale → fresh reader |
| `CachedLeafPage_MultipleReaders_WriterCommit_AllDetectStale` | Multiple cached readers all detect writer commit |

## Files

| File | Purpose |
|---|---|
| `src/Sharc.Core/IPageSource.cs` | `DataVersion` default method, `GetPageMemory` default method |
| `src/Sharc.Core/IO/MemoryPageSource.cs` | `_dataVersion` field + `GetPageMemory` zero-copy override |
| `src/Sharc.Core/IO/CachedPageSource.cs` | Delegates `DataVersion` to inner |
| `src/Sharc.Core/IO/ProxyPageSource.cs` | Delegates `DataVersion` to target |
| `src/Sharc.Core/IO/ShadowPageSource.cs` | Composite `DataVersion` (base + shadow) |
| `src/Sharc.Core/IBTreeReader.cs` | `IsStale` on `IBTreeCursor` and `IIndexBTreeCursor` |
| `src/Sharc.Core/BTree/BTreeCursor.cs` | `_snapshotVersion`, `IsStale`, `_cachedLeafMemory`, `GetCachedLeafPage()` |
| `src/Sharc.Core/BTree/IndexBTreeCursor.cs` | `_snapshotVersion`, `IsStale` |
| `src/Sharc.Core/BTree/WithoutRowIdCursorAdapter.cs` | `IsStale => _inner.IsStale` |
| `src/Sharc/SharcDataReader.cs` | `public bool IsStale` |
