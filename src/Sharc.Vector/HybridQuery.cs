// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Vector;

/// <summary>
/// A pre-compiled hybrid search handle combining vector similarity and text relevance
/// with Reciprocal Rank Fusion (RRF). Pre-resolves table schema, vector/text column ordinals,
/// distance metric, and dimensions at creation time.
/// </summary>
/// <remarks>
/// <para>Created via <see cref="SharcDatabaseExtensions.Hybrid"/>.
/// Follows the same lifecycle as <see cref="VectorQuery"/>: create once, search many, dispose.</para>
/// <para>Internally performs two independent scans (vector distance + text TF scoring),
/// then fuses results via RRF for a unified ranking.</para>
/// <para>This type is <b>not thread-safe</b>.</para>
/// </remarks>
public sealed class HybridQuery : IDisposable
{
    private SharcDatabase? _db;
    private readonly JitQuery _vectorJit;
    private readonly JitQuery _textJit;
    private readonly string _vectorColumnName;
    private readonly string _textColumnName;
    private readonly int _dimensions;
    private readonly DistanceMetric _metric;
    private readonly VectorDistanceFunction _distanceFn;

    internal HybridQuery(
        SharcDatabase db,
        JitQuery vectorJit,
        JitQuery textJit,
        string vectorColumnName,
        string textColumnName,
        int dimensions,
        DistanceMetric metric)
    {
        _db = db;
        _vectorJit = vectorJit;
        _textJit = textJit;
        _vectorColumnName = vectorColumnName;
        _textColumnName = textColumnName;
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
    public HybridQuery WithAgent(AgentInfo? agent)
    {
        _agent = agent;
        return this;
    }

    /// <summary>
    /// Sets a row-level access evaluator for multi-tenant isolation.
    /// Rows that fail the evaluator are silently skipped during both
    /// vector and text scans.
    /// </summary>
    /// <param name="evaluator">The evaluator, or null to clear.</param>
    public HybridQuery WithRowEvaluator(IRowAccessEvaluator? evaluator)
    {
        _vectorJit.WithRowAccess(evaluator);
        _textJit.WithRowAccess(evaluator);
        return this;
    }

    private AgentInfo? _agent;

    // ── Metadata Filtering (pre-search) ─────────────────────────

    /// <summary>
    /// Adds a metadata filter applied BEFORE both vector and text searches.
    /// Rows that fail this filter are excluded from both search paths.
    /// </summary>
    public HybridQuery Where(IFilterStar filter)
    {
        _vectorJit.Where(filter);
        _textJit.Where(filter);
        return this;
    }

    /// <summary>Clears all metadata filters from both search paths.</summary>
    public HybridQuery ClearFilters()
    {
        _vectorJit.ClearFilters();
        _textJit.ClearFilters();
        return this;
    }

    // ── Hybrid Search ───────────────────────────────────────────

    /// <summary>
    /// Performs a hybrid search combining vector similarity and text keyword relevance.
    /// </summary>
    /// <param name="queryVector">Query vector for similarity search (must match configured dimensions).</param>
    /// <param name="queryText">Query text for keyword matching (whitespace-separated terms).</param>
    /// <param name="k">Number of top results to return after fusion.</param>
    /// <param name="columnNames">Optional metadata column names to include in results.</param>
    /// <returns>Results ordered by fused RRF score (descending).</returns>
    /// <exception cref="ArgumentException">If query vector dimensions don't match or query text is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If k is less than 1.</exception>
    public HybridSearchResult Search(
        ReadOnlySpan<float> queryVector,
        string queryText,
        int k,
        params string[] columnNames)
    {
        ThrowIfDisposed();
        ValidateDimensions(queryVector);
        ArgumentException.ThrowIfNullOrEmpty(queryText);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        EnforceAgentAccess(columnNames);

        byte[][] queryTermsUtf8 = TextScorer.TokenizeQuery(queryText);
        int poolSize = k * 3;

        // ── Step 1: Vector scan ──────────────────────────────
        var vectorRanks = new Dictionary<long, int>();
        var metadataByRowId = new Dictionary<long, IReadOnlyDictionary<string, object?>?>();

        {
            string[] projection = new string[1 + columnNames.Length];
            projection[0] = _vectorColumnName;
            columnNames.CopyTo(projection, 1);

            var heap = new VectorTopKHeap(poolSize, _metric != DistanceMetric.DotProduct);

            using var reader = _vectorJit.Query(projection);
            while (reader.Read())
            {
                ReadOnlySpan<byte> blobBytes = reader.GetBlobSpan(0);
                if (blobBytes.IsEmpty) continue; // skip NULL vector

                ReadOnlySpan<float> storedVector = BlobVectorCodec.Decode(blobBytes);
                float distance = _distanceFn(queryVector, storedVector);

                if (heap.ShouldInsert(distance))
                {
                    IReadOnlyDictionary<string, object?>? metadata = null;
                    if (columnNames.Length > 0)
                        metadata = ExtractMetadata(reader, columnNames);
                    heap.ForceInsert(reader.RowId, distance, metadata);
                }
            }

            var vectorResult = heap.ToResult();
            for (int i = 0; i < vectorResult.Count; i++)
            {
                var match = vectorResult[i];
                vectorRanks[match.RowId] = i + 1; // 1-based rank
                metadataByRowId[match.RowId] = match.Metadata;
            }
        }

        // ── Step 2: Text scan ────────────────────────────────
        var textRanks = new Dictionary<long, int>();

        if (queryTermsUtf8.Length > 0)
        {
            string[] textProjection = new string[1 + columnNames.Length];
            textProjection[0] = _textColumnName;
            columnNames.CopyTo(textProjection, 1);

            // Bounded heap: keep top poolSize text results without full-list materialization.
            // Min-heap by TF score: root = worst retained score, evict when new score is better.
            var heap = new TextTopKHeap(poolSize);

            using var reader = _textJit.Query(textProjection);
            while (reader.Read())
            {
                ReadOnlySpan<byte> textBytes = reader.GetUtf8Span(0);
                float tfScore = TextScorer.Score(textBytes, queryTermsUtf8);

                if (tfScore > 0 && heap.ShouldInsert(tfScore))
                {
                    IReadOnlyDictionary<string, object?>? metadata = null;
                    if (columnNames.Length > 0 && !metadataByRowId.ContainsKey(reader.RowId))
                        metadata = ExtractMetadata(reader, columnNames);
                    heap.ForceInsert(reader.RowId, tfScore, metadata);
                }
            }

            // Drain heap sorted by TF score descending (highest first)
            var topText = heap.ToSortedDescending();
            for (int i = 0; i < topText.Count; i++)
            {
                textRanks[topText[i].RowId] = i + 1; // 1-based rank
                if (!metadataByRowId.ContainsKey(topText[i].RowId))
                    metadataByRowId[topText[i].RowId] = topText[i].Metadata;
            }
        }

        // ── Step 3: Fuse ─────────────────────────────────────
        var fused = RankFusion.Fuse(vectorRanks, textRanks, k);

        // ── Step 4: Build results ────────────────────────────
        var matches = new List<HybridMatch>(fused.Count);
        foreach (var (rowId, score, vr, tr) in fused)
        {
            metadataByRowId.TryGetValue(rowId, out var metadata);
            matches.Add(new HybridMatch(
                rowId,
                score,
                VectorRank: vr == RankFusion.UnrankedSentinel ? 0 : vr,
                TextRank: tr == RankFusion.UnrankedSentinel ? 0 : tr,
                metadata));
        }

        return new HybridSearchResult(matches);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _vectorJit.Dispose();
        _textJit.Dispose();
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
        // Enforce access to vector column, text column, and all requested metadata columns
        var allColumns = new string[2 + columnNames.Length];
        allColumns[0] = _vectorColumnName;
        allColumns[1] = _textColumnName;
        columnNames.CopyTo(allColumns, 2);
        EntitlementEnforcer.Enforce(_agent, _vectorJit.Table!.Name, allColumns);
    }

    private static Dictionary<string, object?> ExtractMetadata(SharcDataReader reader, string[] columnNames)
    {
        var dict = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
        // Metadata columns start at index 1 because index 0 is always the vector/text column.
        for (int i = 0; i < columnNames.Length; i++)
        {
            dict[columnNames[i]] = reader.GetValue(i + 1);
        }
        return dict;
    }

    /// <summary>
    /// Fixed-capacity min-heap for top-K text scoring. Root = lowest (worst) retained
    /// TF score. New candidates replace root when their score exceeds it.
    /// </summary>
    private sealed class TextTopKHeap
    {
        private readonly (long RowId, float TfScore, IReadOnlyDictionary<string, object?>? Metadata)[] _heap;
        private readonly int _capacity;
        private int _count;

        internal TextTopKHeap(int k)
        {
            _capacity = k;
            _heap = new (long, float, IReadOnlyDictionary<string, object?>?)[k];
        }

        internal bool ShouldInsert(float tfScore)
        {
            if (_count < _capacity) return true;
            return tfScore > _heap[0].TfScore;
        }

        internal void ForceInsert(long rowId, float tfScore, IReadOnlyDictionary<string, object?>? metadata)
        {
            if (_count < _capacity)
            {
                _heap[_count] = (rowId, tfScore, metadata);
                _count++;
                if (_count == _capacity) BuildHeap();
            }
            else
            {
                _heap[0] = (rowId, tfScore, metadata);
                SiftDown(0);
            }
        }

        internal List<(long RowId, float TfScore, IReadOnlyDictionary<string, object?>? Metadata)> ToSortedDescending()
        {
            var list = new List<(long RowId, float TfScore, IReadOnlyDictionary<string, object?>? Metadata)>(_count);
            for (int i = 0; i < _count; i++)
                list.Add(_heap[i]);
            list.Sort((a, b) => b.TfScore.CompareTo(a.TfScore));
            return list;
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
                int smallest = i;
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                // Min-heap: root = smallest TF score (worst match to evict)
                if (left < _count && _heap[left].TfScore < _heap[smallest].TfScore) smallest = left;
                if (right < _count && _heap[right].TfScore < _heap[smallest].TfScore) smallest = right;
                if (smallest == i) break;
                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
        }
    }
}
