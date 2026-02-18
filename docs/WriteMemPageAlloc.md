# Dynamic Space-On-Demand: Write Memory Page Allocation

## Problem

`SharcWriter.Open(path)` eagerly allocates ~8 MB from `ArrayPool<byte>.Shared` via `CachedPageSource(capacity: 2000)`. The constructor rents 2000 × 4096-byte page buffers in a loop before a single page is read. A single-row DELETE benchmarks at **6,113 KB allocated** — nearly all from this upfront cache reservation.

The write path only touches 3–10 pages per operation. It already has two independent caches that make the 2000-page LRU largely redundant:

1. **BTreeMutator._pageCache** — per-transaction Dictionary<uint, byte[]>, rents on demand, covers all B-tree traversal pages (fresh BTreeMutator created per transaction; not pooled — see ADR-017)
2. **ShadowPageSource._dirtyPages** — COW dirty-page buffer, rents on demand per written page (pooled across auto-commit operations via `SharcWriter._cachedShadow`)

The 2000-page LRU was designed for read workloads (full table scans). Applying it to writes is architecturally mismatched.

## Root Cause Analysis

### Three-Layer Write Cache (redundant stacking)

```
Read order during a write operation:

  BTreeMutator._pageCache         ← Layer 2: fastest, in-process, per-transaction
    → ShadowPageSource._dirtyPages  ← Layer 3: COW write buffer
      → CachedPageSource (LRU)       ← Layer 1: 8 MB pre-allocated, 2000-slot max
        → FilePageSource              ← disk
```

Layer 1 (the 8 MB LRU) is exercised **exactly once per page per transaction** — then bypassed entirely because BTreeMutator._pageCache intercepts all subsequent reads. For a 100K-row table with a 3-level B-tree, a single DELETE reads 3–4 pages from Layer 1 on the first operation, then never touches it again within the transaction.

### Working Set Size

| Operation | Pages accessed | B-tree depth (100K rows) |
|:----------|:--------------:|:------------------------:|
| Single DELETE | 3–4 | root → interior → leaf |
| Single INSERT (no split) | 3–4 | root → interior → leaf |
| Single INSERT (with split) | 5–6 | + new leaf + parent update |
| Batch 100 INSERTs (1 txn) | ~10–15 | shared root/interior via BTreeMutator._pageCache |

The 2000-page cache serves a workload that touches 3–15 pages.

### Allocation Chain

```
SharcWriter.Open(path)
  → SharcDatabase.Open(path, new SharcOpenOptions { Writable = true })
    → SharcDatabaseFactory.CreateFromPageSource(rawSource, options)
      → new CachedPageSource(rawSource, options.PageCacheSize)  // PageCacheSize = 2000 (default)
        → constructor loop: for (0..2000) ArrayPool<byte>.Shared.Rent(4096)  // ~8 MB
```

`SharcOpenOptions.PageCacheSize` defaults to 2000 — calibrated for "Large DB, single table scan" per PerformanceStrategy.md §4.2. No differentiation exists for write workloads.

## Architecture: Modern Database Page Cache Models

Every major embedded database uses demand-driven (grow-on-access) buffer allocation. None pre-allocate at construction.

### SQLite pcache1

- `PCache1AllocPage()` allocates page frames **on first access**, not at cache creation
- `sqlite3_config(SQLITE_CONFIG_PCACHE_HDRSZ)` sets the maximum, not the initial allocation
- Pages are recycled on eviction — the buffer is reused, not freed and re-allocated

### RocksDB Block Cache

- Sharded LRU with a **memory budget** (bytes, not page count)
- Entries are allocated on `Insert()`, not on cache construction
- `NewLRUCache(capacity_bytes)` creates an empty cache that grows to the budget

### DuckDB Buffer Manager

- Memory budget-based allocation
- Buffer frames allocated on demand from a free list
- The pool starts empty and grows as queries execute

### LMDB

- Uses mmap — no explicit page cache at all
- The OS virtual memory manager handles page residency

### Common Pattern

**Capacity is a maximum, not a reservation.** The cache starts empty. Buffers are allocated on first use and recycled on eviction. The working set is determined by access patterns, not by configuration at open time.

## Design: Dynamic Space-On-Demand

### Change 1: CachedPageSource — Demand-Driven Buffer Allocation

**Constructor** — stop renting in the loop:

```csharp
// BEFORE (eager — rents 2000 buffers at construction):
for (int i = 0; i < capacity; i++)
{
    _slots[i].Data = ArrayPool<byte>.Shared.Rent(inner.PageSize);
    _freeSlots.Push(i);
}

// AFTER (demand-driven — metadata only at construction):
for (int i = 0; i < capacity; i++)
{
    // Data stays null — rented on first use in AllocateSlot().
    // Capacity is a maximum, not a reservation.
    _freeSlots.Push(i);
}
```

Slot metadata (`_slots[]`, `_prev[]`, `_next[]`, `_lookup`, `_freeSlots`) stays pre-allocated — it's cheap (ints + dictionary, ~24 KB for 2000 slots). Only the expensive `byte[]` page buffers become demand-driven.

**AllocateSlot()** — rent on first use:

```csharp
private int AllocateSlot()
{
    if (_count < _capacity)
    {
        int slot = _freeSlots.Pop();
        _count++;
        // Demand-driven: rent buffer on first use.
        // Capacity is a maximum, not a reservation.
        _slots[slot].Data ??= ArrayPool<byte>.Shared.Rent(_inner.PageSize);
        return slot;
    }
    else
    {
        // Eviction: reuse the existing buffer from the LRU tail (already rented).
        int slot = _tail;
        var victimPage = _slots[slot].PageNumber;
        _lookup.Remove(victimPage);
        RemoveNode(slot);
        return slot;
    }
}
```

**Dispose()** — no change needed. The existing null-guard already handles unrented slots:

```csharp
if (_slots[i].Data != null)
{
    ArrayPool<byte>.Shared.Return(_slots[i].Data);
    _slots[i].Data = null!;
}
```

### Change 2: SharcWriter — Write-Appropriate Cache Default

```csharp
public static SharcWriter Open(string path)
{
    var db = SharcDatabase.Open(path, new SharcOpenOptions
    {
        Writable = true,
        PageCacheSize = 16
    });
    return new SharcWriter(db, ownsDb: true);
}
```

**Why 16**: The LRU only serves cross-transaction reads for the write path — schema page (page 1) and root pages for frequently-written tables. BTreeMutator._pageCache handles all intra-transaction hot pages. 16 pages × 4096 = 64 KB maximum, demand-grown from 0.

### Why This Is Architecture, Not a Trick

A **lazy allocation patch** delays the same N rentals — it changes when bytes are rented but leaves the conceptual model intact (a cache is a static reservation of N slots).

A **demand-driven cache** changes the contract: capacity is the **ceiling**, the working set is determined by access patterns. A scan touching 500 of 6,700 pages allocates 500 buffers, not 2000. A write touching 3 pages allocates 3 buffers, not 16. Combined with right-sizing the write default, the architecture correctly models both workloads.

## Files Modified

| File | Change |
|:-----|:-------|
| `src/Sharc.Core/IO/CachedPageSource.cs` | Constructor: remove eager rent loop. AllocateSlot(): rent on first use. |
| `src/Sharc/SharcWriter.cs` | Open(): pass `PageCacheSize = 16` |
| `tests/Sharc.Tests/IO/CachedPageSourceTests.cs` | New tests for demand-driven allocation behavior |
| `PRC/PerformanceStrategy.md` | Add "Write" workload row to §4.2 sizing table |
| `PRC/DecisionLog.md` | Record ADR for demand-driven allocation + write default |

## Expected Impact

| Metric | Before | After |
|:-------|-------:|------:|
| SharcWriter.Open() allocation | ~8 MB | ~24 KB (metadata only) |
| Single-row DELETE allocation | 6,113 KB | < 200 KB |
| Write speed | 3.52 ms | unchanged (same execution path) |
| Read scan allocation | unchanged | unchanged (benchmarks use PageCacheSize=0) |
| Read file-backed (default 2000) | 8 MB upfront | pays only for pages actually accessed |

## Workload Sizing Table (updated)

| Workload | Recommended Cache | Rationale |
|:---------|:-----------------:|:----------|
| Small DB (<10 MiB) | 0 | Use MemoryPageSource directly |
| **Write (any DB size)** | **16** | **BTreeMutator handles intra-txn; LRU for schema + root pages only** |
| Medium DB scan | 200–500 | Sequential scan working set |
| Large DB, single table | 2000 (default) | Full scan with LRU eviction |
| Multiple concurrent readers | 5000+ | Shared hot pages across readers |
