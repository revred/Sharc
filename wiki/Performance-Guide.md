# Performance Guide

## Benchmark Results (vs Microsoft.Data.Sqlite)

| Operation | Sharc | SQLite | Speedup | Allocation |
|-----------|-------|--------|---------|------------|
| Point seek (rowid) | 0.27 us | 25.9 us | **95x** | 664 B vs 728 B |
| Table scan (1K rows) | 48 us | 187 us | **3.9x** | 0 B vs 37 KB |
| Graph BFS 2-hop | 2.60 us | 81.55 us | **31x** | 928 B vs 2.8 KB |
| Graph node seek | 3.4 us | 24.3 us | **7.1x** | 8.3 KB vs 728 B |
| Filtered scan (5K rows) | 206 us | 891 us | **4.3x** | near-zero |

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
