# Performance Guide

## Benchmark Results (vs Microsoft.Data.Sqlite)

| Operation | Sharc | SQLite | Speedup | Allocation |
|-----------|-------|--------|---------|------------|
| Point seek (rowid) | 0.038 us | 23.227 us | **609x** | 0 B vs 728 B |
| Table scan (5K rows) | 875.95 us | 5,630.27 us | **6.4x** | 1.41 MB vs 1.41 MB |
| Graph BFS 2-hop | 45.59 us | 205.67 us | **4.5x** | 800 B vs 2.95 KB |
| Graph node seek | 7.071 us | 70.553 us | **10.0x** | 888 B vs 648 B |
| Filtered scan (5K rows) | 261.73 us | 541.54 us | **2.1x** | 0 B vs 720 B |

## Zero-Allocation Patterns

### Use Span-Based Accessors

```csharp
// FAST: No string allocation
ReadOnlySpan<byte> utf8 = reader.GetUtf8Span(1);

// SLOWER: Allocates a string
string name = reader.GetString(1);
```

### Use Column Projection

```csharp
// FAST: Only decodes 2 columns, skips the rest at byte level
using var reader = db.CreateReader("users", "id", "email");

// SLOWER: Decodes all columns
using var reader = db.CreateReader("users");
```

### Use Edge Cursors (Not GetEdges)

```csharp
// FAST: Zero-allocation cursor
using var cursor = graph.GetEdgeCursor(new NodeKey(1));
while (cursor.MoveNext())
{
    long target = cursor.TargetKey;
}

// SLOW (deprecated): Allocates GraphEdge per row
foreach (var edge in graph.GetEdges(new NodeKey(1)))
{
    long target = edge.TargetKey;
}
```

### Reuse Cursor with Reset

```csharp
// FAST: One cursor, multiple seeks
using var cursor = graph.GetEdgeCursor(new NodeKey(1));
// ... iterate ...
cursor.Reset(nextKey);
// ... iterate again — no new allocation
```

### Use Seek for Point Lookups

```csharp
// FAST: O(log N) B-tree seek
if (reader.Seek(42))
    return reader.GetString(1);

// SLOWER: Full table scan to find one row
while (reader.Read())
    if (reader.GetInt64(0) == 42)
        return reader.GetString(1);
```

### Batch Inserts

```csharp
// FAST: Single transaction for all records
long[] ids = writer.InsertBatch("users", records);

// SLOWER: One transaction per record
foreach (var record in records)
    writer.Insert("users", record);
```

## Page Cache Tuning

```csharp
var options = new SharcOpenOptions
{
    PageCacheSize = 500,  // Default: 200 pages (~800 KB at 4096-byte pages)
};
```

- **Read-heavy workloads:** Increase cache size (500-2000)
- **Write-heavy workloads:** Keep cache small (16-50) — the write engine manages its own pages
- **Memory-constrained:** Set to 0 to disable caching (every read hits the source)
- **In-memory databases:** Cache is less impactful since pages are already in RAM

## Graph Traversal Performance

The two-phase BFS in `Traverse()` separates edge discovery from node lookup:

1. **Phase 1 (Edge-only):** BFS using edge cursors only. Keeps page cache on the relation B-tree
2. **Phase 2 (Batch lookup):** Sequential concept B-tree lookups with better cache locality

For edge-only operations (counting, connectivity), use `GetEdgeCursor()` directly — it skips node materialization entirely.

## Memory

Sharc uses `ArrayPool<byte>.Shared` for overflow page assembly and `stackalloc` for small temporary buffers. No persistent heap allocations on the hot read path.
