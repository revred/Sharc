// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Public API facade for HNSW approximate nearest neighbor index.
/// Thread-safe for concurrent Search calls after Build/Load.
/// </summary>
public sealed class HnswIndex : IDisposable
{
    private readonly HnswGraph _graph;
    private readonly IVectorResolver _resolver;
    private readonly VectorDistanceFunction _distanceFn;
    private readonly DistanceMetric _metric;
    private readonly HnswConfig _config;
    private bool _disposed;

    internal HnswIndex(HnswGraph graph, IVectorResolver resolver,
        DistanceMetric metric, HnswConfig config)
    {
        _graph = graph;
        _resolver = resolver;
        _distanceFn = VectorDistanceFunctions.Resolve(metric);
        _metric = metric;
        _config = config;
    }

    /// <summary>Number of vectors in the index.</summary>
    public int Count => _graph.NodeCount;

    /// <summary>Dimensions per vector.</summary>
    public int Dimensions => _resolver.Dimensions;

    /// <summary>Distance metric used by the index.</summary>
    public DistanceMetric Metric => _metric;

    /// <summary>Configuration used to build the index.</summary>
    public HnswConfig Config => _config;

    /// <summary>The internal graph (for persistence).</summary>
    internal HnswGraph Graph => _graph;

    /// <summary>
    /// Builds an HNSW index from vectors stored in a Sharc database table.
    /// Optionally persists the index to a shadow table for fast reload.
    /// </summary>
    /// <param name="db">The database instance.</param>
    /// <param name="tableName">Table containing vector data.</param>
    /// <param name="vectorColumn">BLOB column storing float vectors.</param>
    /// <param name="metric">Distance metric (default: Cosine).</param>
    /// <param name="config">HNSW configuration (default: HnswConfig.Default).</param>
    /// <param name="persist">If true, saves the index to a shadow table (default: true).</param>
    /// <returns>A ready-to-search HNSW index.</returns>
    public static HnswIndex Build(SharcDatabase db, string tableName,
        string vectorColumn, DistanceMetric metric = DistanceMetric.Cosine,
        HnswConfig? config = null, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);

        var cfg = config ?? HnswConfig.Default;
        cfg.Validate();

        // Scan all vectors from the table
        var vectors = new List<float[]>();
        var rowIds = new List<long>();
        int? expectedDims = null;

        using var jit = db.Jit(tableName);
        using var reader = jit.Query(vectorColumn);
        while (reader.Read())
        {
            long rowId = reader.RowId;
            var blobSpan = reader.GetBlobSpan(0);
            float[] vector = BlobVectorCodec.Decode(blobSpan).ToArray();

            // Validate consistent dimensions
            if (expectedDims == null)
                expectedDims = vector.Length;
            else if (vector.Length != expectedDims.Value)
                throw new InvalidOperationException(
                    $"Vector at rowid {rowId} has {vector.Length} dimensions but expected {expectedDims.Value}.");

            vectors.Add(vector);
            rowIds.Add(rowId);
        }

        if (vectors.Count == 0)
            throw new InvalidOperationException(
                $"Table '{tableName}' has no rows — cannot build HNSW index.");

        var resolver = new MemoryVectorResolver(vectors.ToArray());
        int dimensions = vectors[0].Length;
        var graph = HnswGraphBuilder.Build(resolver, rowIds.ToArray(), metric, cfg);

        if (persist)
        {
            string shadowName = HnswShadowTable.GetTableName(tableName, vectorColumn);
            HnswShadowTable.Save(db, shadowName, graph, cfg, dimensions, metric);
        }

        return new HnswIndex(graph, resolver, metric, cfg);
    }

    /// <summary>
    /// Loads a previously persisted HNSW index from its shadow table.
    /// Vectors are re-read from the source table and bound to graph nodes by rowId.
    /// </summary>
    /// <returns>The loaded index, or null if no persisted index exists.</returns>
    public static HnswIndex? Load(SharcDatabase db, string tableName,
        string vectorColumn)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentException.ThrowIfNullOrEmpty(vectorColumn);

        string shadowName = HnswShadowTable.GetTableName(tableName, vectorColumn);
        var loaded = HnswShadowTable.Load(db, shadowName);
        if (loaded == null)
            return null;

        var (graph, config, dimensions, metric) = loaded.Value;

        // Build rowId → nodeIndex map for correct binding
        var rowIdToIndex = new Dictionary<long, int>(graph.NodeCount);
        for (int i = 0; i < graph.NodeCount; i++)
            rowIdToIndex[graph.GetRowId(i)] = i;

        // Re-read vectors from source table and bind by rowId
        var vectors = new float[graph.NodeCount][];
        int resolved = 0;

        using var jit = db.Jit(tableName);
        using var reader = jit.Query(vectorColumn);
        while (reader.Read())
        {
            long rowId = reader.RowId;
            if (rowIdToIndex.TryGetValue(rowId, out int nodeIndex))
            {
                var blobSpan = reader.GetBlobSpan(0);
                vectors[nodeIndex] = BlobVectorCodec.Decode(blobSpan).ToArray();
                resolved++;
            }
        }

        if (resolved != graph.NodeCount)
            throw new InvalidOperationException(
                $"HNSW index is stale: graph has {graph.NodeCount} nodes but only {resolved} " +
                $"matching rows found in table '{tableName}'. Rebuild the index.");

        var resolver = new MemoryVectorResolver(vectors);
        return new HnswIndex(graph, resolver, metric, config);
    }

    /// <summary>
    /// Loads an existing persisted index if available and its metric matches,
    /// otherwise builds and persists. Throws if the loaded index has a different
    /// metric than requested.
    /// </summary>
    public static HnswIndex LoadOrBuild(SharcDatabase db, string tableName,
        string vectorColumn, DistanceMetric metric = DistanceMetric.Cosine,
        HnswConfig? config = null)
    {
        var loaded = Load(db, tableName, vectorColumn);
        if (loaded != null)
        {
            // Validate metric matches
            if (loaded.Metric != metric)
                throw new InvalidOperationException(
                    $"Persisted HNSW index uses {loaded.Metric} but {metric} was requested. " +
                    $"Rebuild the index with Build(persist: true) using the desired metric.");
            return loaded;
        }

        return Build(db, tableName, vectorColumn, metric, config, persist: true);
    }

    /// <summary>
    /// Builds an HNSW index from in-memory vectors (for testing and non-database use).
    /// </summary>
    internal static HnswIndex BuildFromMemory(float[][] vectors, long[] rowIds,
        DistanceMetric metric = DistanceMetric.Cosine, HnswConfig? config = null)
    {
        var cfg = config ?? HnswConfig.Default;
        cfg.Validate();

        if (vectors.Length == 0)
            throw new InvalidOperationException("Cannot build HNSW index from zero vectors.");

        if (vectors.Length != rowIds.Length)
            throw new ArgumentException(
                $"vectors.Length ({vectors.Length}) must match rowIds.Length ({rowIds.Length}).",
                nameof(rowIds));

        var resolver = new MemoryVectorResolver(vectors);
        var graph = HnswGraphBuilder.Build(resolver, rowIds, metric, cfg);

        return new HnswIndex(graph, resolver, metric, cfg);
    }

    /// <summary>
    /// Searches for the k nearest neighbors to the query vector.
    /// </summary>
    /// <param name="queryVector">The query vector (must match index dimensions).</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <param name="ef">Beam width (higher = better recall, slower). Null = use config default.</param>
    /// <returns>Search results ordered by distance.</returns>
    public VectorSearchResult Search(ReadOnlySpan<float> queryVector, int k, int? ef = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (queryVector.Length != Dimensions)
            throw new ArgumentException(
                $"Query vector has {queryVector.Length} dimensions but index has {Dimensions}.",
                nameof(queryVector));

        if (k <= 0)
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be positive.");

        int effectiveEf = ef ?? _config.EfSearch;
        effectiveEf = Math.Max(effectiveEf, k);

        return HnswGraphSearcher.Search(_graph, queryVector, k, effectiveEf,
            _resolver, _distanceFn, _metric);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }
}
