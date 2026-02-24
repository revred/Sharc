// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// A pre-compiled vector similarity search handle. Pre-resolves table schema,
/// vector column ordinal, distance metric, and dimension count at creation time.
/// Reusable across multiple searches — only the query vector changes per call.
/// </summary>
/// <remarks>
/// <para>Created via <see cref="SharcDatabaseExtensions.Vector"/>.
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
    private readonly string _vectorColumnName;
    private readonly int _dimensions;
    private readonly DistanceMetric _metric;
    private readonly VectorDistanceFunction _distanceFn;

    internal VectorQuery(
        SharcDatabase db,
        JitQuery innerJit,
        string vectorColumnName,
        int dimensions,
        DistanceMetric metric)
    {
        _db = db;
        _innerJit = innerJit;
        _vectorColumnName = vectorColumnName;
        _dimensions = dimensions;
        _metric = metric;
        _distanceFn = VectorDistanceFunctions.Resolve(metric);
    }

    // ── Metadata Filtering (pre-search) ─────────────────────────

    /// <summary>
    /// Adds a metadata filter. Applied BEFORE distance computation —
    /// rows that fail this filter are never distance-computed.
    /// This is the "pre-filter" pattern from vector DB literature.
    /// </summary>
    public VectorQuery Where(IFilterStar filter)
    {
        _innerJit.Where(filter);
        return this;
    }

    /// <summary>Clears all metadata filters.</summary>
    public VectorQuery ClearFilters()
    {
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
    /// <param name="columnNames">Optional list of column names to retrieve as metadata.</param>
    /// <returns>Results ordered by distance (ascending for Cosine/Euclidean, descending for DotProduct).</returns>
    public VectorSearchResult NearestTo(ReadOnlySpan<float> queryVector, int k, params string[] columnNames)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);

        // Project the vector column plus any requested metadata columns
        string[] projection = new string[1 + columnNames.Length];
        projection[0] = _vectorColumnName;
        columnNames.CopyTo(projection, 1);

        using var reader = _innerJit.Query(projection);

        // Top-K via heap
        var heap = new VectorTopKHeap(k, _metric != DistanceMetric.DotProduct);

        while (reader.Read())
        {
            // Zero-copy: BlobSpan points directly into cached page buffer
            ReadOnlySpan<byte> blobBytes = reader.GetBlobSpan(0);
            ReadOnlySpan<float> storedVector = BlobVectorCodec.Decode(blobBytes);

            float distance = _distanceFn(queryVector, storedVector);
            long rowid = reader.RowId;

            // Only extract metadata when the heap will actually keep this candidate,
            // avoiding a Dictionary allocation for every scanned row.
            if (heap.ShouldInsert(distance))
            {
                IReadOnlyDictionary<string, object?>? metadata = null;
                if (columnNames.Length > 0)
                    metadata = ExtractMetadata(reader, columnNames);
                heap.ForceInsert(rowid, distance, metadata);
            }
        }

        return heap.ToResult();
    }

    /// <summary>
    /// Returns all rows within the specified distance threshold.
    /// </summary>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="maxDistance">
    /// Maximum distance threshold. For DotProduct, this is a minimum similarity threshold.
    /// </param>
    /// <param name="columnNames">Optional list of column names to retrieve as metadata.</param>
    public VectorSearchResult WithinDistance(ReadOnlySpan<float> queryVector, float maxDistance, params string[] columnNames)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);

        string[] projection = new string[1 + columnNames.Length];
        projection[0] = _vectorColumnName;
        columnNames.CopyTo(projection, 1);

        using var reader = _innerJit.Query(projection);

        var results = new List<VectorMatch>();

        while (reader.Read())
        {
            ReadOnlySpan<byte> blobBytes = reader.GetBlobSpan(0);
            ReadOnlySpan<float> storedVector = BlobVectorCodec.Decode(blobBytes);

            float distance = _distanceFn(queryVector, storedVector);

            bool withinThreshold = _metric == DistanceMetric.DotProduct
                ? distance >= maxDistance  // DotProduct: higher = more similar
                : distance <= maxDistance; // Cosine/Euclidean: lower = more similar

            if (withinThreshold)
            {
                IReadOnlyDictionary<string, object?>? metadata = null;
                if (columnNames.Length > 0)
                    metadata = ExtractMetadata(reader, columnNames);

                results.Add(new VectorMatch(reader.RowId, distance, metadata));
            }
        }

        // Sort by distance
        results.Sort((a, b) => _metric == DistanceMetric.DotProduct
            ? b.Distance.CompareTo(a.Distance)
            : a.Distance.CompareTo(b.Distance));

        return new VectorSearchResult(results);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _innerJit.Dispose();
        _db = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_db is null, this);
    }

    private void ValidateDimensions(ReadOnlySpan<float> queryVector)
    {
        if (queryVector.Length != _dimensions)
            throw new ArgumentException(
                $"Query vector has {queryVector.Length} dimensions but the index expects {_dimensions}.");
    }

    private static Dictionary<string, object?> ExtractMetadata(SharcDataReader reader, string[] columnNames)
    {
        var dict = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
        // Metadata columns start at index 1 because index 0 is always the vector column.
        for (int i = 0; i < columnNames.Length; i++)
        {
            dict[columnNames[i]] = reader.GetValue(i + 1);
        }
        return dict;
    }
}
