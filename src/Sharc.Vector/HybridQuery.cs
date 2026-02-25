// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

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

            var scored = new List<(long RowId, float TfScore, IReadOnlyDictionary<string, object?>? Metadata)>();

            using var reader = _textJit.Query(textProjection);
            while (reader.Read())
            {
                ReadOnlySpan<byte> textBytes = reader.GetUtf8Span(0);
                float tfScore = TextScorer.Score(textBytes, queryTermsUtf8);

                if (tfScore > 0)
                {
                    IReadOnlyDictionary<string, object?>? metadata = null;
                    if (columnNames.Length > 0 && !metadataByRowId.ContainsKey(reader.RowId))
                        metadata = ExtractMetadata(reader, columnNames);
                    scored.Add((reader.RowId, tfScore, metadata));
                }
            }

            // Sort by TF score descending, take top poolSize
            scored.Sort((a, b) => b.TfScore.CompareTo(a.TfScore));
            int textCount = Math.Min(scored.Count, poolSize);
            for (int i = 0; i < textCount; i++)
            {
                textRanks[scored[i].RowId] = i + 1; // 1-based rank
                // Store metadata for text-only rows
                if (!metadataByRowId.ContainsKey(scored[i].RowId))
                    metadataByRowId[scored[i].RowId] = scored[i].Metadata;
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
}
