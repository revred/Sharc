// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;
using Sharc.Vector.Hnsw;

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

    // ── Agent Entitlements ─────────────────────────────────────

    /// <summary>
    /// Sets the agent whose entitlements are enforced on every search.
    /// Table and column access is validated at search time; throws
    /// <see cref="UnauthorizedAccessException"/> if the agent lacks permission.
    /// </summary>
    /// <param name="agent">The agent to enforce, or null to clear.</param>
    public VectorQuery WithAgent(AgentInfo? agent)
    {
        _agent = agent;
        return this;
    }

    /// <summary>
    /// Sets a row-level access evaluator for multi-tenant isolation.
    /// Rows that fail the evaluator are silently skipped during scans —
    /// they are never distance-computed or returned in results.
    /// </summary>
    /// <param name="evaluator">The evaluator, or null to clear.</param>
    public VectorQuery WithRowEvaluator(IRowAccessEvaluator? evaluator)
    {
        _innerJit.WithRowAccess(evaluator);
        return this;
    }

    private AgentInfo? _agent;

    // ── HNSW Index ────────────────────────────────────────────────

    private HnswIndex? _hnswIndex;
    private const int PostFilterSelectivityMultiplier = 2;
    private const int PostFilterMinCandidateThreshold = 8;

    /// <summary>
    /// Execution diagnostics for the most recent
    /// <see cref="NearestTo(ReadOnlySpan{float}, int, VectorSearchOptions, string[])"/> call.
    /// Useful for benchmark instrumentation and planner validation.
    /// </summary>
    public VectorExecutionInfo LastExecutionInfo { get; private set; } = VectorExecutionInfo.None;

    /// <summary>
    /// Attaches an HNSW index for approximate nearest neighbor search.
    /// When set, <see cref="NearestTo(ReadOnlySpan{float}, int, VectorSearchOptions, string[])"/> dispatches to the index when no active
    /// filters are present. Falls back to flat scan transparently when filters
    /// are active (HNSW doesn't support pre-filtering).
    /// </summary>
    /// <param name="index">The HNSW index to use, or null to detach.</param>
    public VectorQuery UseIndex(HnswIndex? index)
    {
        if (index != null)
        {
            if (index.Dimensions != _dimensions)
                throw new ArgumentException(
                    $"Index has {index.Dimensions} dimensions but VectorQuery expects {_dimensions}.",
                    nameof(index));
            if (index.Metric != _metric)
                throw new ArgumentException(
                    $"Index uses {index.Metric} but VectorQuery expects {_metric}.",
                    nameof(index));
        }
        _hnswIndex = index;
        return this;
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
        => NearestTo(queryVector, k, VectorSearchOptions.Default, columnNames);

    /// <summary>
    /// Returns the K nearest neighbors using explicit execution options.
    /// </summary>
    /// <param name="queryVector">The query vector (must match configured dimensions).</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <param name="options">Execution controls (flat-scan forcing, ef override).</param>
    /// <param name="columnNames">Optional list of column names to retrieve as metadata.</param>
    /// <returns>Results ordered by distance (ascending for Cosine/Euclidean, descending for DotProduct).</returns>
    public VectorSearchResult NearestTo(
        ReadOnlySpan<float> queryVector, int k, VectorSearchOptions options, params string[] columnNames)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);
        EnforceAgentAccess(columnNames);

        if (options.ForceFlatScan)
        {
            var forcedFlat = FlatScanNearestTo(queryVector, k, columnNames, out int forcedScannedRows);
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.FlatScan,
                CandidateCount: forcedScannedRows,
                RequestedK: k,
                ReturnedCount: forcedFlat.Count,
                UsedFallbackScan: false);
            return forcedFlat;
        }

        // HNSW fast path: when index is attached AND no filters/row evaluator/metadata requested
        bool canUseHnsw = _hnswIndex != null && !_innerJit.HasActiveFilters && !_innerJit.HasRowAccessEvaluator;

        if (canUseHnsw && columnNames.Length == 0)
        {
            var result = _hnswIndex!.Search(queryVector, k, options.EfSearch);
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.HnswDirect,
                CandidateCount: _hnswIndex.Count,
                RequestedK: k,
                ReturnedCount: result.Count,
                UsedFallbackScan: false);
            return result;
        }

        // HNSW with metadata enrichment: search HNSW then enrich results from table
        if (canUseHnsw && columnNames.Length > 0)
        {
            var hnswResult = _hnswIndex!.Search(queryVector, k, options.EfSearch);
            var enriched = EnrichWithMetadata(hnswResult, columnNames);
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.HnswMetadataEnrichment,
                CandidateCount: _hnswIndex.Count,
                RequestedK: k,
                ReturnedCount: enriched.Count,
                UsedFallbackScan: false);
            return enriched;
        }

        // Filter-aware HNSW path: build filtered allow-list then widen ANN candidates.
        if (_hnswIndex != null && _innerJit.HasActiveFilters && !_innerJit.HasRowAccessEvaluator)
        {
            return IndexedPostFilterNearestTo(queryVector, k, options, columnNames);
        }

        // Flat scan path (no HNSW index or row evaluator active).
        var flat = FlatScanNearestTo(queryVector, k, columnNames, out int scannedRows);
        LastExecutionInfo = new VectorExecutionInfo(
            Strategy: VectorExecutionStrategy.FlatScan,
            CandidateCount: scannedRows,
            RequestedK: k,
            ReturnedCount: flat.Count,
            UsedFallbackScan: false);
        return flat;
    }

    /// <summary>Flat brute-force scan for NearestTo.</summary>
    private VectorSearchResult FlatScanNearestTo(
        ReadOnlySpan<float> queryVector, int k, string[] columnNames, out int scannedRows)
    {
        scannedRows = 0;

        // Project the vector column plus any requested metadata columns
        string[] projection = new string[1 + columnNames.Length];
        projection[0] = _vectorColumnName;
        columnNames.CopyTo(projection, 1);

        using var reader = _innerJit.Query(projection);

        // Top-K via heap
        var heap = new VectorTopKHeap(k, _metric != DistanceMetric.DotProduct);

        while (reader.Read())
        {
            scannedRows++;

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

    private VectorSearchResult IndexedPostFilterNearestTo(
        ReadOnlySpan<float> queryVector, int k, VectorSearchOptions options, string[] columnNames)
    {
        var allowList = BuildFilterAllowList();
        if (allowList.Count == 0)
        {
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.HnswPostFilterWidening,
                CandidateCount: 0,
                RequestedK: k,
                ReturnedCount: 0,
                UsedFallbackScan: false);
            return new VectorSearchResult(new List<VectorMatch>());
        }

        int selectiveThreshold = Math.Max(k * PostFilterSelectivityMultiplier, PostFilterMinCandidateThreshold);
        if (allowList.Count <= selectiveThreshold)
        {
            var selectiveFlat = FlatScanNearestTo(queryVector, k, columnNames, out int scannedRows);
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.FlatScan,
                CandidateCount: scannedRows,
                RequestedK: k,
                ReturnedCount: selectiveFlat.Count,
                UsedFallbackScan: false);
            return selectiveFlat;
        }

        var index = _hnswIndex!;
        int targetCount = Math.Min(k, allowList.Count);
        var heap = new VectorTopKHeap(targetCount, isMinHeap: _metric != DistanceMetric.DotProduct);
        var seen = new HashSet<long>();

        int searchK = Math.Min(index.Count, Math.Max(k * 4, 32));
        while (true)
        {
            var candidateBatch = index.Search(queryVector, searchK, options.EfSearch);
            for (int i = 0; i < candidateBatch.Count; i++)
            {
                var match = candidateBatch[i];
                if (!allowList.Contains(match.RowId) || !seen.Add(match.RowId))
                    continue;

                heap.TryInsert(match.RowId, match.Distance);
            }

            if (heap.Count >= targetCount || searchK >= index.Count)
                break;

            int next = Math.Min(index.Count, searchK * 2);
            if (next == searchK)
                break;

            searchK = next;
        }

        if (heap.Count < targetCount)
        {
            var fallback = FlatScanNearestTo(queryVector, k, columnNames, out int scannedRows);
            LastExecutionInfo = new VectorExecutionInfo(
                Strategy: VectorExecutionStrategy.HnswPostFilterWidening,
                CandidateCount: scannedRows,
                RequestedK: k,
                ReturnedCount: fallback.Count,
                UsedFallbackScan: true);
            return fallback;
        }

        var filtered = heap.ToResult();
        if (columnNames.Length > 0)
            filtered = EnrichWithMetadata(filtered, columnNames);

        LastExecutionInfo = new VectorExecutionInfo(
            Strategy: VectorExecutionStrategy.HnswPostFilterWidening,
            CandidateCount: allowList.Count,
            RequestedK: k,
            ReturnedCount: filtered.Count,
            UsedFallbackScan: false);
        return filtered;
    }

    private HashSet<long> BuildFilterAllowList()
    {
        string probeColumn = GetFilterProbeColumn();
        using var reader = _innerJit.Query(probeColumn);
        var allowed = new HashSet<long>();
        while (reader.Read())
            allowed.Add(reader.RowId);
        return allowed;
    }

    private string GetFilterProbeColumn()
    {
        var table = _innerJit.Table;
        if (table is null || table.Columns.Count == 0)
            return _vectorColumnName;

        int rowIdAliasOrdinal = _innerJit.RowidAliasOrdinal;
        if (rowIdAliasOrdinal >= 0 && rowIdAliasOrdinal < table.Columns.Count)
            return table.Columns[rowIdAliasOrdinal].Name;

        return table.Columns[0].Name;
    }

    /// <summary>Enriches HNSW results with metadata columns from the source table.</summary>
    private VectorSearchResult EnrichWithMetadata(VectorSearchResult hnswResult, string[] columnNames)
    {
        var enriched = new List<VectorMatch>(hnswResult.Count);
        var db = _db!;
        var table = _innerJit.Table!;

        // Seek each result row and extract metadata
        using var reader = db.CreateReader(table.Name, columnNames);
        for (int i = 0; i < hnswResult.Count; i++)
        {
            var match = hnswResult[i];
            if (reader.Seek(match.RowId))
            {
                var metadata = ExtractMetadataFromSeek(reader, columnNames);
                enriched.Add(new VectorMatch(match.RowId, match.Distance, metadata));
            }
            else
            {
                enriched.Add(match); // row may have been deleted; keep result without metadata
            }
        }

        return new VectorSearchResult(enriched);
    }

    private static Dictionary<string, object?> ExtractMetadataFromSeek(SharcDataReader reader, string[] columnNames)
    {
        var dict = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columnNames.Length; i++)
            dict[columnNames[i]] = reader.GetValue(i);
        return dict;
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
        EnforceAgentAccess(columnNames);

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

    private void EnforceAgentAccess(string[] columnNames)
    {
        if (_agent == null) return;
        // Build full column list: vector column + requested metadata columns
        var allColumns = new string[1 + columnNames.Length];
        allColumns[0] = _vectorColumnName;
        columnNames.CopyTo(allColumns, 1);
        EntitlementEnforcer.Enforce(_agent, _innerJit.Table!.Name, allColumns);
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
