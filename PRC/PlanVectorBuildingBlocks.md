# PlanVectorBuildingBlocks — Vector Database Primitives for Sharc

**Status**: Proposed
**Priority**: High — AI workload differentiation
**Target**: v1.4.0
**Depends on**: JitQuery (existing), FilterStar/BakedDelegate (existing), CachedPageSource (existing), SharcWriter (existing), GetBlobSpan (existing), System.Numerics.Tensors (NuGet, Sharc.Vector only)

---

## Executive Summary

Sharc can support vector database building blocks with **one lightweight BCL dependency** (`System.Numerics.Tensors`) and **minimal new code** by applying the same patterns that made stored procedures and graph traversal successful: C# closures as the compilation unit, the existing CACHE layer for hot-path acceleration, and JIT handles for pre-resolved reuse.

The core insight: **a vector similarity search is just a filtered scan where the predicate is a distance function operating on BLOB bytes.** Sharc already has every building block — BLOB storage, zero-copy `GetBlobSpan()`, closure-composed `BakedDelegate` filters, LRU-cached pages, and `JitQuery` handles. The vector layer is the **missing 20%** that assembles these into a coherent API.

Distance computation delegates to `TensorPrimitives` (`System.Numerics.Tensors`), a Microsoft 1st-party BCL package — MIT licensed, zero transitive dependencies on .NET 8+, SIMD-accelerated with `Vector128`/`256`/`512` hardware intrinsics including AVX-512 when available. This dependency lives **only in `Sharc.Vector`** — the core library remains zero-dependency.

---

## Why This Works on Sharc (Architectural Fit)

### Pattern Mapping: Existing → Vector

| Existing Pattern | How It Applies to Vectors |
| :--- | :--- |
| `FilterStar.Column("age").Gte(25L)` → `BakedDelegate` | `VectorOps.Cosine(queryVec)` → distance closure on BLOB bytes |
| `JitQuery` pre-resolves table + columns + filters | `VectorQuery` pre-resolves table + vector column + metric + index |
| `CachedPageSource` LRU for hot pages | `VectorCache` LRU for hot vector data (decoded floats) |
| `ViewBuilder.Where(row => ...)` C# closure as filter | `VectorBuilder.Where(vec => distance < threshold)` closure as filter |
| `PreparedQuery.Execute()` skip parse, reuse handle | `PreparedSimilarity.Execute(queryVec)` skip setup, reuse handle |
| `ILayer` / `IViewCursor` abstraction for scan sources | `IVectorIndex` abstraction for ANN/flat scan sources |
| `SharcExtensionRegistry` plugin mechanism | `VectorExtension` registers itself on database open |

### What Already Exists (Zero Changes Needed)

| Component | Location | Role in Vector Layer |
| :--- | :--- | :--- |
| `GetBlobSpan(int)` | `SharcDataReader.cs` | Zero-copy access to vector bytes — no decode overhead |
| `RecordDecoder` BLOB handling | `RecordDecoder.cs` | Serial type ≥12 (even) = BLOB, size computed from type |
| `SharcWriter.Insert/Update` | `SharcWriter.cs` | Store vectors as BLOB columns in regular tables |
| `FilterTreeCompiler.CompileBaked` | `FilterStarCompiler.cs` | Compile distance predicate into `BakedDelegate` |
| `JitQuery.Where()` + `Query()` | `JitQuery.cs` | Accumulate distance filter, execute with pre-resolved state |
| `CachedPageSource` LRU | `CachedPageSource.cs` | Page-level caching for vector table scans |
| `BTreeCursor.Seek/MoveNext` | `BTreeCursor.cs` | Row-level iteration over vector table |
| `ISharcExtension` | `ISharcExtension.cs` | Register vector capabilities as an extension |
| `ArrayPool<byte>.Shared` | Throughout | Zero-alloc temporary buffers for vector decode |

---

## Architecture: Three Layers

```
┌─────────────────────────────────────────────────────────────────────┐
│  Public API (Sharc.Vector/)                                         │
│  db.Vector("embeddings")           → VectorQuery handle             │
│  VectorOps.Cosine / Euclidean / Dot                                 │
│  vq.NearestTo(queryVec, k: 10)    → SharcDataReader                │
│  vq.WithinDistance(queryVec, 0.3)  → SharcDataReader                │
├─────────────────────────────────────────────────────────────────────┤
│  Index Layer (Sharc.Vector/Index/)                                   │
│  FlatVectorIndex        — brute-force scan (baseline, always works) │
│  CachedVectorIndex      — flat + decoded float[] LRU cache          │
│  IVectorIndex           — abstraction (future: HNSW, IVF, PQ)       │
├─────────────────────────────────────────────────────────────────────┤
│  Primitives (Sharc.Vector/Distance/)                                 │
│  DistanceMetric         — enum: Cosine, Euclidean, DotProduct        │
│  VectorDistanceFunctions — delegates to TensorPrimitives (SIMD)      │
│  BlobVectorCodec        — float[] ↔ BLOB byte[] (IEEE 754 LE)       │
└─────────────────────────────────────────────────────────────────────┘

  ↕ All layers operate on existing Sharc infrastructure ↕

┌─────────────────────────────────────────────────────────────────────┐
│  Sharc Core (unchanged)                                              │
│  BTreeCursor → CachedPageSource → RecordDecoder → GetBlobSpan       │
│  JitQuery → FilterStar → BakedDelegate → SharcDataReader             │
│  SharcWriter → BTreeMutator → PageManager                            │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Phase 0: Vector Primitives (Pure Math, No Storage Changes)

**Effort**: 1 day | **Impact**: Foundation for everything else
**Note**: `VectorDistanceFunctions` is now 6 lines of `TensorPrimitives` delegation — the bulk of Phase 0 is `BlobVectorCodec` and tests.

### BlobVectorCodec

Converts between `float[]` / `ReadOnlySpan<float>` and the BLOB byte layout stored in SQLite.

```csharp
namespace Sharc.Vector;

/// <summary>
/// Encodes and decodes float vectors to/from SQLite BLOB format.
/// Layout: IEEE 754 little-endian, 4 bytes per dimension.
/// A 384-dim vector = 1,536 byte BLOB.
/// </summary>
public static class BlobVectorCodec
{
    /// <summary>Decodes a BLOB span into a float span (zero-copy reinterpret).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<float> Decode(ReadOnlySpan<byte> blob)
        => MemoryMarshal.Cast<byte, float>(blob);

    /// <summary>Encodes a float array into a BLOB byte array for storage.</summary>
    public static byte[] Encode(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.Cast<float, byte>(vector).CopyTo(bytes);
        return bytes;
    }

    /// <summary>Encodes into a caller-provided buffer (zero-alloc).</summary>
    public static int Encode(ReadOnlySpan<float> vector, Span<byte> destination)
    {
        var source = MemoryMarshal.Cast<float, byte>(vector);
        source.CopyTo(destination);
        return source.Length;
    }

    /// <summary>Returns the dimensionality of a vector stored in the given BLOB.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDimensions(int blobByteLength) => blobByteLength / sizeof(float);
}
```

**Key design choice**: `MemoryMarshal.Cast<byte, float>` is a zero-copy reinterpret on little-endian platforms (all modern x86/ARM). This means `GetBlobSpan()` → `BlobVectorCodec.Decode()` has **zero allocation and zero copy** — the float span points directly into the cached page buffer. The returned `ReadOnlySpan<float>` feeds directly into `TensorPrimitives.CosineSimilarity()` / `Distance()` / `Dot()` — the entire hot path from page cache to distance score is zero-alloc.

### VectorDistanceFunctions

```csharp
using System.Numerics.Tensors;

namespace Sharc.Vector;

/// <summary>
/// Distance metrics for vector similarity. Delegates to <see cref="TensorPrimitives"/>
/// for SIMD-accelerated computation (Vector128/256/512, including AVX-512 when available).
/// All functions operate on ReadOnlySpan&lt;float&gt; for zero-allocation invocation from the
/// filter hot path.
/// </summary>
/// <remarks>
/// <para>Uses <c>System.Numerics.Tensors.TensorPrimitives</c> — a Microsoft 1st-party BCL
/// package (MIT, zero transitive deps on .NET 8+). This provides battle-tested SIMD
/// implementations with explicit Vector128/256/512 intrinsics, FMA, alignment tricks,
/// and remainder masking — significantly better than hand-rolled <c>Vector&lt;float&gt;</c> loops.</para>
/// <para>The entire distance layer is 6 lines of delegation. Microsoft maintains the SIMD.</para>
/// </remarks>
public static class VectorDistanceFunctions
{
    /// <summary>
    /// Cosine distance = 1.0 - cosine_similarity.
    /// Range: [0, 2]. 0 = identical direction. 1 = orthogonal. 2 = opposite.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => 1f - TensorPrimitives.CosineSimilarity(a, b);

    /// <summary>
    /// Euclidean (L2) distance. Range: [0, +∞).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => TensorPrimitives.Distance(a, b);

    /// <summary>
    /// Dot product (inner product). Higher = more similar.
    /// For normalized vectors, equivalent to cosine similarity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => TensorPrimitives.Dot(a, b);

    /// <summary>Resolves a metric enum to the corresponding function delegate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Func<ReadOnlySpan<float>, ReadOnlySpan<float>, float> Resolve(
        DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => CosineDistance,
        DistanceMetric.Euclidean => EuclideanDistance,
        DistanceMetric.DotProduct => DotProduct,
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
}

/// <summary>Distance metric for vector similarity search.</summary>
public enum DistanceMetric
{
    /// <summary>Cosine distance (1 - cosine_similarity). Good for text embeddings.</summary>
    Cosine,
    /// <summary>Euclidean (L2) distance. Good for spatial/geometric data.</summary>
    Euclidean,
    /// <summary>Dot product (inner product). Good for normalized vectors, recommendation.</summary>
    DotProduct
}
```

**Why TensorPrimitives over hand-rolled SIMD**:
- **6 lines of delegation** vs ~100 lines of hand-rolled `Vector<float>` loops (or ~400+ lines to match Microsoft's quality with Vector128/256/512 codepaths)
- **AVX-512 support**: `TensorPrimitives` uses explicit `Vector512` when available; `Vector<float>` maxes out at `Vector256` on most runtimes
- **Battle-tested**: Edge cases (NaN, Infinity, zero-magnitude, alignment, remainder masking, FMA) all handled and tested by the .NET runtime team
- **ReadOnlySpan<float>` parameters**: Plugs directly into `BlobVectorCodec.Decode()` output — same API surface either way
- **Sharc core stays zero-dependency**: `System.Numerics.Tensors` lives only in the `Sharc.Vector` project

---

## Phase 1: VectorQuery Handle (The JitQuery Equivalent)

**Effort**: 2-3 days | **Impact**: Complete similarity search API

### Core Concept

Just as `JitQuery` pre-resolves table + columns + filters for reuse, `VectorQuery` pre-resolves table + vector column ordinal + distance metric + dimension count. The distance function becomes a **closure-captured delegate** — exactly like `BakedDelegate` for `FilterStar`.

### VectorQuery Type

```csharp
namespace Sharc.Vector;

/// <summary>
/// A pre-compiled vector similarity search handle. Pre-resolves table schema,
/// vector column ordinal, distance metric, and dimension count at creation time.
/// Reusable across multiple searches — only the query vector changes per call.
/// </summary>
/// <remarks>
/// <para>Created via <see cref="SharcDatabase.Vector(string, string, DistanceMetric)"/>.
/// Follows the same lifecycle pattern as <see cref="JitQuery"/>: create once,
/// search many times, dispose when done.</para>
/// <para>Internally uses <see cref="JitQuery"/> for table scanning and filter
/// composition, layering distance computation on top.</para>
/// <para>This type is <b>not thread-safe</b>.</para>
/// </remarks>
public sealed class VectorQuery : IDisposable
{
    private SharcDatabase? _db;
    private readonly JitQuery _innerJit;
    private readonly int _vectorColumnOrdinal;
    private readonly string _vectorColumnName;
    private readonly int _dimensions;
    private readonly DistanceMetric _metric;
    private readonly Func<ReadOnlySpan<float>, ReadOnlySpan<float>, float> _distanceFn;

    // Pre-decoded vector cache (LRU over rowid → float[])
    // Avoids re-decoding the same BLOB on repeated searches against the same data.
    // Mirrors CachedPageSource's pattern: fixed capacity, LRU eviction.
    private readonly VectorCache? _vectorCache;

    // Metadata filter (pre-compiled, composed with distance filter)
    private List<IFilterStar>? _metadataFilters;

    internal VectorQuery(
        SharcDatabase db,
        JitQuery innerJit,
        string vectorColumnName,
        int vectorColumnOrdinal,
        int dimensions,
        DistanceMetric metric,
        int cacheCapacity = 10_000)
    {
        _db = db;
        _innerJit = innerJit;
        _vectorColumnName = vectorColumnName;
        _vectorColumnOrdinal = vectorColumnOrdinal;
        _dimensions = dimensions;
        _metric = metric;
        _distanceFn = VectorDistanceFunctions.Resolve(metric);
        _vectorCache = cacheCapacity > 0 ? new VectorCache(cacheCapacity) : null;
    }

    // ── Metadata Filtering (pre-search) ─────────────────────────

    /// <summary>
    /// Adds a metadata filter. Applied BEFORE distance computation —
    /// rows that fail this filter are never distance-computed.
    /// This is the "pre-filter" pattern from vector DB literature.
    /// </summary>
    public VectorQuery Where(IFilterStar filter)
    {
        _metadataFilters ??= new List<IFilterStar>();
        _metadataFilters.Add(filter);
        _innerJit.Where(filter);
        return this;
    }

    /// <summary>Clears metadata filters.</summary>
    public VectorQuery ClearFilters()
    {
        _metadataFilters?.Clear();
        _innerJit.ClearFilters();
        return this;
    }

    // ── Similarity Search ───────────────────────────────────────

    /// <summary>
    /// Returns the K nearest neighbors to the query vector.
    /// Scans all rows (after metadata filter), computes distance, returns top-K.
    /// </summary>
    /// <param name="queryVector">The query vector (must match configured dimensions).</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <param name="columns">Additional columns to project alongside distance.</param>
    /// <returns>Results ordered by distance (ascending for Cosine/Euclidean, descending for DotProduct).</returns>
    public VectorSearchResult NearestTo(ReadOnlySpan<float> queryVector, int k,
        params string[]? columns)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);

        // Build projection: requested columns + vector column (for distance computation)
        var allColumns = BuildProjection(columns);

        // Execute the JitQuery scan (with pre-compiled metadata filters)
        using var reader = _innerJit.Query(allColumns);

        // Top-K via min-heap (same pattern as StreamingTopNProcessor)
        var heap = new VectorTopKHeap(k, _metric != DistanceMetric.DotProduct);

        while (reader.Read())
        {
            // Zero-copy: BlobSpan points directly into cached page buffer
            ReadOnlySpan<byte> blobBytes = reader.GetBlobSpan(_vectorColumnOrdinal);
            ReadOnlySpan<float> storedVector = BlobVectorCodec.Decode(blobBytes);

            float distance = _distanceFn(queryVector, storedVector);
            long rowid = reader.GetRowId();

            heap.TryInsert(rowid, distance);
        }

        return heap.ToResult();
    }

    /// <summary>
    /// Returns all rows within the specified distance threshold.
    /// </summary>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="maxDistance">Maximum distance threshold.</param>
    /// <param name="columns">Additional columns to project.</param>
    public VectorSearchResult WithinDistance(ReadOnlySpan<float> queryVector,
        float maxDistance, params string[]? columns)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);

        var allColumns = BuildProjection(columns);
        using var reader = _innerJit.Query(allColumns);

        var results = new List<VectorMatch>();

        while (reader.Read())
        {
            ReadOnlySpan<byte> blobBytes = reader.GetBlobSpan(_vectorColumnOrdinal);
            ReadOnlySpan<float> storedVector = BlobVectorCodec.Decode(blobBytes);

            float distance = _distanceFn(queryVector, storedVector);

            bool withinThreshold = _metric == DistanceMetric.DotProduct
                ? distance >= maxDistance  // DotProduct: higher = more similar
                : distance <= maxDistance; // Cosine/Euclidean: lower = more similar

            if (withinThreshold)
                results.Add(new VectorMatch(reader.GetRowId(), distance));
        }

        // Sort by distance
        results.Sort((a, b) => _metric == DistanceMetric.DotProduct
            ? b.Distance.CompareTo(a.Distance)
            : a.Distance.CompareTo(b.Distance));

        return new VectorSearchResult(results);
    }

    // ── Freeze to Prepared ──────────────────────────────────────

    /// <summary>
    /// Freezes the current metadata filters into a <see cref="PreparedVectorQuery"/>
    /// for maximum repeated search speed.
    /// </summary>
    public PreparedVectorQuery ToPrepared(params string[]? columns)
    {
        ThrowIfDisposed();
        var prepared = _innerJit.ToPrepared(BuildProjection(columns));
        return new PreparedVectorQuery(
            prepared, _vectorColumnOrdinal, _dimensions, _metric, _distanceFn);
    }

    public void Dispose()
    {
        _innerJit.Dispose();
        _vectorCache?.Clear();
        _metadataFilters?.Clear();
        _db = null;
    }

    private string[] BuildProjection(string[]? columns)
    {
        if (columns is null or { Length: 0 })
            return new[] { _vectorColumnName };

        // Ensure vector column is always included
        if (Array.IndexOf(columns, _vectorColumnName) >= 0)
            return columns;

        var all = new string[columns.Length + 1];
        columns.CopyTo(all, 0);
        all[columns.Length] = _vectorColumnName;
        return all;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_db is null, this);

    private void ValidateDimensions(ReadOnlySpan<float> queryVector)
    {
        if (queryVector.Length != _dimensions)
            throw new ArgumentException(
                $"Query vector has {queryVector.Length} dimensions but the index expects {_dimensions}.");
    }
}
```

### Factory Method on SharcDatabase

```csharp
// Added to SharcDatabase.cs

/// <summary>
/// Creates a vector similarity search handle for the specified table and vector column.
/// Pre-resolves table schema, vector column ordinal, and distance metric at creation time.
/// </summary>
/// <param name="tableName">The table containing vector data.</param>
/// <param name="vectorColumn">The BLOB column storing float vectors.</param>
/// <param name="metric">The distance metric to use (default: Cosine).</param>
/// <returns>A reusable <see cref="VectorQuery"/> handle.</returns>
public VectorQuery Vector(string tableName, string vectorColumn,
    DistanceMetric metric = DistanceMetric.Cosine)
{
    var jit = Jit(tableName);

    // Resolve vector column ordinal and dimensions (probe first row)
    int vectorOrdinal = -1;
    var table = jit.Table!;
    for (int i = 0; i < table.Columns.Count; i++)
    {
        if (table.Columns[i].Name.Equals(vectorColumn, StringComparison.OrdinalIgnoreCase))
        {
            vectorOrdinal = table.Columns[i].Ordinal;
            break;
        }
    }

    if (vectorOrdinal < 0)
        throw new ArgumentException($"Column '{vectorColumn}' not found in table '{tableName}'.");

    // Probe first row to determine dimensions
    int dimensions;
    using (var probe = jit.Query(vectorColumn))
    {
        if (!probe.Read())
            throw new InvalidOperationException(
                $"Table '{tableName}' is empty — cannot determine vector dimensions.");
        dimensions = BlobVectorCodec.GetDimensions(probe.GetBlobSpan(0).Length);
    }

    return new VectorQuery(this, jit, vectorColumn, vectorOrdinal, dimensions, metric);
}
```

### Result Types

```csharp
namespace Sharc.Vector;

/// <summary>A single similarity match with distance score.</summary>
public readonly record struct VectorMatch(long RowId, float Distance);

/// <summary>
/// Results of a vector similarity search, ordered by relevance.
/// </summary>
public sealed class VectorSearchResult
{
    private readonly List<VectorMatch> _matches;

    internal VectorSearchResult(List<VectorMatch> matches) => _matches = matches;

    /// <summary>Number of matches.</summary>
    public int Count => _matches.Count;

    /// <summary>Gets match at the specified index.</summary>
    public VectorMatch this[int index] => _matches[index];

    /// <summary>All matches (ordered by distance).</summary>
    public IReadOnlyList<VectorMatch> Matches => _matches;

    /// <summary>The row IDs of matched rows (for subsequent lookups).</summary>
    public IEnumerable<long> RowIds => _matches.Select(m => m.RowId);
}
```

### VectorTopKHeap

```csharp
namespace Sharc.Vector;

/// <summary>
/// Fixed-capacity min/max-heap for top-K nearest neighbor selection.
/// Same pattern as <see cref="TopNHeap"/> in the query pipeline.
/// </summary>
internal sealed class VectorTopKHeap
{
    private readonly (long RowId, float Distance)[] _heap;
    private readonly int _capacity;
    private readonly bool _isMinHeap; // true = keep smallest distances (Cosine/Euclidean)
    private int _count;

    internal VectorTopKHeap(int k, bool isMinHeap)
    {
        _capacity = k;
        _isMinHeap = isMinHeap;
        _heap = new (long, float)[k];
    }

    /// <summary>
    /// Tries to insert a candidate. If the heap is full, replaces the worst
    /// element if the candidate is better.
    /// </summary>
    internal void TryInsert(long rowId, float distance)
    {
        if (_count < _capacity)
        {
            _heap[_count] = (rowId, distance);
            _count++;
            if (_count == _capacity) BuildHeap();
        }
        else
        {
            // Compare against root (worst element)
            bool isBetter = _isMinHeap
                ? distance < _heap[0].Distance
                : distance > _heap[0].Distance;

            if (isBetter)
            {
                _heap[0] = (rowId, distance);
                SiftDown(0);
            }
        }
    }

    internal VectorSearchResult ToResult()
    {
        var results = new List<VectorMatch>(_count);
        for (int i = 0; i < _count; i++)
            results.Add(new VectorMatch(_heap[i].RowId, _heap[i].Distance));

        results.Sort((a, b) => _isMinHeap
            ? a.Distance.CompareTo(b.Distance)
            : b.Distance.CompareTo(a.Distance));

        return new VectorSearchResult(results);
    }

    private void BuildHeap()
    {
        for (int i = _count / 2 - 1; i >= 0; i--)
            SiftDown(i);
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int worst = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            // Max-heap for min-distance (root = largest distance = worst match)
            // Min-heap for max-distance/DotProduct (root = smallest = worst match)
            if (left < _count && ShouldSwap(left, worst)) worst = left;
            if (right < _count && ShouldSwap(right, worst)) worst = right;

            if (worst == i) break;
            (_heap[i], _heap[worst]) = (_heap[worst], _heap[i]);
            i = worst;
        }
    }

    private bool ShouldSwap(int candidate, int current)
    {
        return _isMinHeap
            ? _heap[candidate].Distance > _heap[current].Distance  // max-heap to evict largest
            : _heap[candidate].Distance < _heap[current].Distance; // min-heap to evict smallest
    }
}
```

---

## Phase 2: VectorCache — Decoded Float LRU

**Effort**: 1 day | **Impact**: 2-5× speedup on repeated searches over same data

### Core Concept

`CachedPageSource` caches raw page bytes. `VectorCache` caches **decoded float arrays** keyed by rowid. This eliminates the `MemoryMarshal.Cast` and potential page-fault overhead on repeated searches.

```csharp
namespace Sharc.Vector;

/// <summary>
/// LRU cache of decoded float vectors keyed by row ID.
/// Follows the same intrusive-linked-list pattern as <see cref="CachedPageSource"/>.
/// </summary>
internal sealed class VectorCache
{
    private readonly Dictionary<long, int> _lookup;
    private readonly (long RowId, float[] Vector)[] _slots;
    private readonly int[] _prev, _next;
    private readonly int _capacity;
    private int _head = -1, _tail = -1, _count;

    internal VectorCache(int capacity)
    {
        _capacity = capacity;
        _lookup = new Dictionary<long, int>(capacity);
        _slots = new (long, float[])[capacity];
        _prev = new int[capacity];
        _next = new int[capacity];
        Array.Fill(_prev, -1);
        Array.Fill(_next, -1);
    }

    /// <summary>
    /// Gets a cached vector, or null if not cached.
    /// Promotes to head on hit (LRU).
    /// </summary>
    internal float[]? Get(long rowId)
    {
        if (_lookup.TryGetValue(rowId, out int slot))
        {
            MoveToHead(slot);
            return _slots[slot].Vector;
        }
        return null;
    }

    /// <summary>
    /// Caches a decoded vector. Evicts LRU on overflow.
    /// </summary>
    internal void Put(long rowId, float[] vector)
    {
        if (_lookup.TryGetValue(rowId, out int existing))
        {
            _slots[existing].Vector = vector;
            MoveToHead(existing);
            return;
        }

        int slot;
        if (_count < _capacity)
        {
            slot = _count++;
        }
        else
        {
            slot = _tail;
            _lookup.Remove(_slots[slot].RowId);
            Remove(slot);
        }

        _slots[slot] = (rowId, vector);
        _lookup[rowId] = slot;
        AddToHead(slot);
    }

    internal void Clear()
    {
        _lookup.Clear();
        _head = _tail = -1;
        _count = 0;
    }

    // ── Intrusive linked list (same pattern as CachedPageSource) ──

    private void MoveToHead(int slot) { if (slot != _head) { Remove(slot); AddToHead(slot); } }

    private void AddToHead(int slot)
    {
        _prev[slot] = -1;
        _next[slot] = _head;
        if (_head >= 0) _prev[_head] = slot;
        _head = slot;
        if (_tail < 0) _tail = slot;
    }

    private void Remove(int slot)
    {
        int p = _prev[slot], n = _next[slot];
        if (p >= 0) _next[p] = n; else _head = n;
        if (n >= 0) _prev[n] = p; else _tail = p;
        _prev[slot] = _next[slot] = -1;
    }
}
```

---

## Phase 3: Stored Procedures for Vector Operations

**Effort**: 1 day | **Impact**: Named, reusable similarity searches

Following the `PlanCoreStoredProcedures` pattern — vector searches as registered, named, pre-compiled operations:

```csharp
// Registration (once at startup)
db.RegisterVectorProcedure("FindSimilarProducts",
    table: "products",
    vectorColumn: "embedding",
    metric: DistanceMetric.Cosine,
    metadataFilter: FilterStar.Column("category").Eq("electronics"));

// Execution (many times, skip all setup)
float[] queryEmbedding = embeddingModel.Encode("wireless headphones");
var results = db.ExecuteVectorProcedure("FindSimilarProducts",
    queryVector: queryEmbedding, k: 10);

foreach (var match in results.Matches)
    Console.WriteLine($"Row {match.RowId}: distance={match.Distance:F4}");
```

This mirrors the `db.RegisterProcedure` / `db.ExecuteProcedure` pattern from `PlanCoreStoredProcedures.md`.

---

## Phase 4: IVectorIndex Abstraction (Future ANN Support)

**Effort**: 2-3 days | **Impact**: Opens the door to HNSW, IVF, PQ without API changes

```csharp
namespace Sharc.Vector;

/// <summary>
/// Abstraction for vector index implementations.
/// Flat scan is the baseline; future implementations (HNSW, IVF, PQ)
/// implement this interface for approximate nearest neighbor search.
/// </summary>
public interface IVectorIndex : IDisposable
{
    /// <summary>Returns the K approximate nearest neighbors.</summary>
    VectorSearchResult Search(ReadOnlySpan<float> query, int k);

    /// <summary>Adds a vector to the index.</summary>
    void Add(long rowId, ReadOnlySpan<float> vector);

    /// <summary>Removes a vector from the index.</summary>
    void Remove(long rowId);

    /// <summary>Number of vectors in the index.</summary>
    int Count { get; }

    /// <summary>Dimensions of vectors in this index.</summary>
    int Dimensions { get; }
}

/// <summary>
/// Flat (brute-force) vector index. Always correct. O(n) per search.
/// Built automatically from table data on first search.
/// </summary>
internal sealed class FlatVectorIndex : IVectorIndex
{
    private readonly Dictionary<long, float[]> _vectors = new();
    private readonly DistanceMetric _metric;
    private readonly Func<ReadOnlySpan<float>, ReadOnlySpan<float>, float> _distanceFn;

    public int Count => _vectors.Count;
    public int Dimensions { get; }

    internal FlatVectorIndex(int dimensions, DistanceMetric metric)
    {
        Dimensions = dimensions;
        _metric = metric;
        _distanceFn = VectorDistanceFunctions.Resolve(metric);
    }

    public void Add(long rowId, ReadOnlySpan<float> vector)
        => _vectors[rowId] = vector.ToArray();

    public void Remove(long rowId)
        => _vectors.Remove(rowId);

    public VectorSearchResult Search(ReadOnlySpan<float> query, int k)
    {
        var heap = new VectorTopKHeap(k, _metric != DistanceMetric.DotProduct);
        foreach (var (rowId, vector) in _vectors)
            heap.TryInsert(rowId, _distanceFn(query, vector));
        return heap.ToResult();
    }

    public void Dispose() => _vectors.Clear();
}
```

The `IVectorIndex` abstraction follows the same DIP principle as `IPageSource` — `VectorQuery` depends on the abstraction, not the implementation. Swapping from `FlatVectorIndex` to a future `HnswVectorIndex` requires zero API changes.

---

## Complete Usage Examples

### Example 1: AI Embedding Search (RAG Pattern)

```csharp
// Create the database with embeddings table
using var db = SharcDatabase.Open("knowledge.db");
var writer = db.CreateWriter();

// Store documents with embeddings (embeddings from any model)
writer.Insert("documents", new Dictionary<string, object>
{
    ["title"] = "Introduction to Quantum Computing",
    ["content"] = "Quantum computers use qubits...",
    ["embedding"] = BlobVectorCodec.Encode(embeddingModel.Encode("Quantum computers use qubits...")),
    ["category"] = "science"
});

// Create vector search handle (once, reuse many times)
using var vq = db.Vector("documents", "embedding", DistanceMetric.Cosine);

// Search with metadata pre-filter
vq.Where(FilterStar.Column("category").Eq("science"));
float[] query = embeddingModel.Encode("How do quantum computers work?");
var results = vq.NearestTo(query, k: 5, "title", "content");

foreach (var match in results.Matches)
{
    // Use rowid to fetch full row data
    using var jit = db.Jit("documents");
    using var reader = jit.Where(FilterStar.Column("rowid").Eq(match.RowId))
                         .Query("title", "content");
    if (reader.Read())
        Console.WriteLine($"[{match.Distance:F3}] {reader.GetString(0)}");
}
```

### Example 2: Recommendation Engine

```csharp
// Pre-compile a similarity search (stored procedure pattern)
using var vq = db.Vector("products", "feature_vector", DistanceMetric.DotProduct);

// For each user, find similar products to their purchase history
foreach (var userPurchase in userHistory)
{
    vq.ClearFilters();
    vq.Where(FilterStar.Column("in_stock").Eq(1L));
    vq.Where(FilterStar.Column("price").Lte(userBudget));

    var similar = vq.NearestTo(userPurchase.FeatureVector, k: 20);
    recommendations.AddRange(similar.RowIds);
}
```

### Example 3: Anomaly Detection

```csharp
using var vq = db.Vector("sensor_readings", "embedding", DistanceMetric.Euclidean);

// Find readings that are far from the centroid (outliers)
float[] centroid = ComputeCentroid(allReadings);
var outliers = vq.WithinDistance(centroid, maxDistance: 2.5f);

// Invert: everything NOT within distance is an anomaly
// (use the scan directly for custom logic)
using var reader = db.Jit("sensor_readings").Query("timestamp", "value", "embedding");
while (reader.Read())
{
    var vec = BlobVectorCodec.Decode(reader.GetBlobSpan(2));
    float dist = VectorDistanceFunctions.EuclideanDistance(centroid, vec);
    if (dist > 2.5f)
        Console.WriteLine($"ANOMALY at {reader.GetInt64(0)}: distance={dist:F3}");
}
```

---

## Performance Expectations

### Flat Scan (Phase 1 Baseline)

| Dataset | Dimensions | Metric | Expected Latency | Notes |
| :--- | :--- | :--- | :--- | :--- |
| 10K vectors | 384 (MiniLM) | Cosine | ~5-10 ms | TensorPrimitives SIMD (Vector128/256/512), zero-copy BLOB decode |
| 10K vectors | 1536 (OpenAI) | Cosine | ~15-30 ms | Dominated by distance math; AVX-512 provides ~1.5× over AVX2 |
| 100K vectors | 384 | Cosine | ~50-100 ms | Linear scan; practical for many workloads |
| 1M vectors | 384 | Cosine | ~500 ms-1s | ANN index recommended at this scale |

### Allocation Budget

| Operation | Target | Rationale |
| :--- | :--- | :--- |
| `VectorQuery` creation | ~800 B | JitQuery (640B) + VectorQuery fields |
| `NearestTo(k=10)` | ~1.5 KB | Heap array + result list |
| Per-row distance computation | 0 B | Zero-copy BLOB → float span reinterpret |
| `VectorCache` hit | 0 B per hit | Cached float[] returned directly |

### Break-Even: Flat vs. ANN Index

```
Flat scan: O(n × d) where n = rows, d = dimensions
HNSW:      O(d × log(n)) per search, but O(n × d) memory + build time

Break-even point (approx):
  n < 50K:   Flat scan is fine (< 50ms for 384-dim on modern CPU)
  n > 100K:  ANN index pays for itself
  n > 1M:    ANN index is essential

Recommendation: Ship Phase 1 (flat) now, add Phase 4 (HNSW) when users hit 100K+.
```

---

## Implementation Plan

| Phase | Deliverable | Effort | Impact |
| :--- | :--- | :--- | :--- |
| 0 | `BlobVectorCodec` + `VectorDistanceFunctions` (TensorPrimitives delegation) + `DistanceMetric` | 1 day | Foundation (pure math, fully testable) |
| 1 | `VectorQuery` + `VectorTopKHeap` + `VectorSearchResult` + `db.Vector()` | 2-3 days | Complete similarity search API |
| 2 | `VectorCache` (decoded float LRU) | 1 day | 2-5× speedup on repeated searches |
| 3 | `PreparedVectorQuery` + stored procedure registration | 1 day | Named, pre-compiled searches |
| 4 | `IVectorIndex` + `FlatVectorIndex` (abstraction for future ANN) | 2-3 days | Future-proofing |
| **Total** | | **7-9 days** | |

### Recommended Minimum Viable Delivery: Phases 0 + 1 (3-4 days)

This gives users: `db.Vector("table", "col")` → `.NearestTo(vec, k)` — a complete vector search with SIMD-accelerated distance, zero-copy BLOB decode, metadata pre-filtering, and JIT-handle reuse. Everything else is optimization.

---

## Project Structure (New Files)

```
src/
├── Sharc.Vector/                              ← New project (or folder in Sharc/)
│   ├── Sharc.Vector.csproj                    ← References Sharc, Sharc.Core + System.Numerics.Tensors
│   ├── BlobVectorCodec.cs                     ← float[] ↔ BLOB encoding
│   ├── DistanceMetric.cs                      ← Enum: Cosine, Euclidean, DotProduct
│   ├── VectorDistanceFunctions.cs             ← 6-line TensorPrimitives delegation
│   ├── VectorQuery.cs                         ← The JitQuery equivalent
│   ├── VectorSearchResult.cs                  ← Result types
│   ├── VectorMatch.cs                         ← Single match record
│   ├── VectorTopKHeap.cs                      ← Top-K selection
│   ├── VectorCache.cs                         ← Decoded float LRU
│   ├── PreparedVectorQuery.cs                 ← Frozen vector search handle
│   └── Index/
│       ├── IVectorIndex.cs                    ← Abstraction for ANN
│       └── FlatVectorIndex.cs                 ← Brute-force baseline
tests/
└── Sharc.Vector.Tests/
    ├── BlobVectorCodecTests.cs
    ├── VectorDistanceFunctionTests.cs
    ├── VectorQueryTests.cs
    ├── VectorTopKHeapTests.cs
    └── VectorCacheTests.cs
```

### Sharc.Vector.csproj PackageReference

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Sharc/Sharc.csproj" />
    <ProjectReference Include="../Sharc.Core/Sharc.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- TensorPrimitives — SIMD-accelerated distance functions (CosineSimilarity, Distance, Dot).
         Microsoft 1st-party BCL package. MIT license. Zero transitive deps on .NET 8+.
         Passes all 7 criteria in PRC/DependencyPolicy.md. See PlanVectorBuildingBlocks.md. -->
    <PackageReference Include="System.Numerics.Tensors" />
  </ItemGroup>
</Project>
```

Central version management in `Directory.Packages.props`:

```xml
<PackageVersion Include="System.Numerics.Tensors" Version="9.0.8" />
```

---

## Testing Strategy

### Unit Tests

```
BlobVectorCodec_Encode_Decode_Roundtrip
BlobVectorCodec_Decode_ZeroCopy_SameMemory
BlobVectorCodec_GetDimensions_CorrectForVariousSizes
CosineDistance_IdenticalVectors_ReturnsZero
CosineDistance_OrthogonalVectors_ReturnsOne
CosineDistance_OppositeVectors_ReturnsTwo
EuclideanDistance_IdenticalVectors_ReturnsZero
EuclideanDistance_KnownPair_ReturnsExpected
DotProduct_IdenticalNormalized_ReturnsOne
DotProduct_Orthogonal_ReturnsZero
VectorTopKHeap_InsertLessThanK_ReturnsAll
VectorTopKHeap_InsertMoreThanK_ReturnsOnlyK
VectorTopKHeap_Cosine_KeepsSmallestDistances
VectorTopKHeap_DotProduct_KeepsLargestValues
VectorQuery_NearestTo_ReturnsCorrectTopK
VectorQuery_WithMetadataFilter_OnlySearchesFilteredRows
VectorQuery_ClearFilters_ResetsMetadataFilter
VectorQuery_DimensionMismatch_ThrowsArgumentException
VectorCache_Get_Miss_ReturnsNull
VectorCache_Put_Get_ReturnsVector
VectorCache_Eviction_RemovesLRU
```

### Integration Tests

- Insert 1K vectors with metadata → NearestTo returns correct neighbors
- Metadata pre-filter reduces search space correctly
- VectorQuery reuse across multiple searches produces consistent results
- Encrypted database + vector search works end-to-end
- Concurrent readers (separate VectorQuery instances) don't interfere

---

## Design Decisions

### Why BLOB, Not Separate Float Columns?

1. **Variable dimensionality**: 384-dim (MiniLM) vs 1536-dim (OpenAI) vs 3072-dim (text-embedding-3-large)
2. **Zero-copy decode**: `MemoryMarshal.Cast<byte, float>` on BLOB span is free
3. **SQLite compatibility**: Any SQLite tool can still read the database
4. **No schema explosion**: One BLOB column vs. 384+ REAL columns

### Why Use System.Numerics.Tensors? (Dependency Evaluation)

Evaluated against Sharc's 7-criteria dependency policy (`PRC/DependencyPolicy.md`):

| # | Criterion | Verdict | Details |
| :--- | :--- | :--- | :--- |
| 1 | Built-in alternative? | **Partial** | `System.Numerics.Vector<float>` is runtime-builtin but only gives raw SIMD loops — you'd hand-write Cosine/Euclidean/Dot yourself (~100 lines with `Vector<float>`, ~400+ lines to match Microsoft's quality with Vector128/256/512 codepaths). |
| 2 | Can we implement in <200 lines? | **Yes, but worse** | Hand-rolled `Vector<float>` is ~100 lines but caps at 256-bit SIMD and misses FMA, alignment, remainder masking, AVX-512 codepaths, and NaN/Infinity edge cases. Microsoft's `TensorPrimitives.CosineSimilarity` alone is ~200 lines of carefully optimized intrinsics. |
| 3 | Actively maintained? | **Yes** | Part of `dotnet/runtime`. 70 releases, latest within days. Maintained by Stephen Toub and the .NET runtime team. |
| 4 | Permissive license? | **Yes** | MIT license. |
| 5 | Transitive dependencies? | **Zero on .NET 8+** | Only `.NET Standard 2.0` / `.NET Framework` TFMs pull `Microsoft.Bcl.Numerics`. Sharc targets .NET 8+: zero transitives. |
| 6 | Used by >10K packages? | **Yes** | Millions of downloads. Used by ML.NET, Semantic Kernel, ONNX Runtime, and top 20 GitHub repos. |
| 7 | Binary size impact <100 KiB? | **Yes** | .nupkg is ~600 KB across 3 TFMs. The net8.0 DLL is the only one shipped. With IL trimming, only called methods survive — effective impact ~50-80 KB for CosineSimilarity, Distance, Dot, Normalize. |

**Result**: Passes all 7 criteria. Dependency scoped to `Sharc.Vector` only — Sharc core remains zero-dependency.

**What it buys**:
- `VectorDistanceFunctions` is **6 lines of delegation** instead of ~100-400 lines of hand-rolled SIMD
- Explicit `Vector128`/`256`/`512` intrinsics with AVX-512 support (hand-rolled `Vector<float>` maxes at 256-bit)
- Battle-tested edge case handling (NaN, Infinity, zero-magnitude, FMA, alignment, remainder masking)
- TensorPrimitives grew from 40 to ~200 overloads in .NET 9 — future operations (Normalize, SoftMax, Sigmoid) available at no additional cost

### Why Flat Scan First, Not HNSW?

1. **Correctness baseline**: Flat scan is always exact — essential for validating ANN approximation quality
2. **Sufficient for most Sharc workloads**: Sharc targets embedded/edge/mobile — typically < 100K vectors
3. **Zero structural overhead**: No graph maintenance, no rebuild on insert
4. **IVectorIndex abstraction**: When HNSW is needed, it slots in without API changes

### Why Layer On JitQuery, Not Build Separately?

1. **Metadata pre-filtering**: Users need `WHERE category = 'science' AND price < 100` before distance search. JitQuery already has the full FilterStar pipeline.
2. **Cache reuse**: JitQuery's cursor/reader cache and CachedPageSource acceleration apply automatically.
3. **Code reuse**: ~70% of VectorQuery is delegation to JitQuery. Building separately would duplicate table resolution, column projection, and filter compilation.

---

## Success Criteria

1. **One scoped dependency**: `System.Numerics.Tensors` on `Sharc.Vector` only — Sharc core stays zero-dependency
2. **< 500 lines of new code** for Phase 0+1 (excluding tests) — distance functions are 6 lines of delegation
3. **Zero-copy hot path**: `GetBlobSpan()` → `MemoryMarshal.Cast` → `TensorPrimitives` distance — no allocations per row
4. **API consistency**: `db.Vector()` feels like `db.Jit()` — same lifecycle, same filter composition, same disposal
5. **SQLite compatible**: Vector databases are regular SQLite files readable by any tool
6. **SIMD acceleration**: `TensorPrimitives` uses Vector128/256/512 intrinsics including AVX-512 when available
7. **All existing tests pass**: Zero regressions to core read/write/query paths
