// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Reciprocal Rank Fusion (RRF) algorithm for combining ranked result lists
/// from heterogeneous retrieval systems.
/// </summary>
/// <remarks>
/// Implements the RRF formula from Cormack, Clarke, and Buettcher (2009):
/// <c>RRF(d) = Σ(1 / (k + rank_i(d)))</c> where k is a smoothing constant (default 60).
/// </remarks>
internal static class RankFusion
{
    /// <summary>Sentinel value indicating a document was not present in a ranked list.</summary>
    internal const int UnrankedSentinel = int.MaxValue;

    /// <summary>
    /// Computes the RRF score for a single document given its ranks across result lists.
    /// </summary>
    /// <param name="ranks">The 1-based rank of the document in each result list.
    /// Use <see cref="UnrankedSentinel"/> if the document is absent from a list.</param>
    /// <param name="k">The RRF smoothing constant (default 60).</param>
    /// <returns>The fused RRF score (higher = more relevant).</returns>
    internal static float ComputeScore(ReadOnlySpan<int> ranks, int k = 60)
    {
        float score = 0f;
        for (int i = 0; i < ranks.Length; i++)
        {
            if (ranks[i] != UnrankedSentinel)
                score += 1.0f / (k + ranks[i]);
        }
        return score;
    }

    /// <summary>
    /// Fuses two ranked lists into a single list ordered by RRF score descending.
    /// </summary>
    /// <param name="vectorRanked">RowId-to-rank mapping from vector search (1-based ranks).</param>
    /// <param name="textRanked">RowId-to-rank mapping from text search (1-based ranks).</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="k">RRF smoothing constant (default 60).</param>
    /// <returns>Row IDs with their fused scores, vector ranks, and text ranks,
    /// ordered by fused score descending.</returns>
    internal static List<(long RowId, float Score, int VectorRank, int TextRank)> Fuse(
        Dictionary<long, int> vectorRanked,
        Dictionary<long, int> textRanked,
        int topK,
        int k = 60)
    {
        // Build union of all row IDs
        var allRowIds = new HashSet<long>(vectorRanked.Keys);
        foreach (var rowId in textRanked.Keys)
            allRowIds.Add(rowId);

        var candidates = new List<(long RowId, float Score, int VectorRank, int TextRank)>(allRowIds.Count);
        Span<int> ranks = stackalloc int[2];

        foreach (long rowId in allRowIds)
        {
            int vr = vectorRanked.TryGetValue(rowId, out int vRank) ? vRank : UnrankedSentinel;
            int tr = textRanked.TryGetValue(rowId, out int tRank) ? tRank : UnrankedSentinel;

            ranks[0] = vr;
            ranks[1] = tr;
            float score = ComputeScore(ranks, k);

            candidates.Add((rowId, score, vr, tr));
        }

        // Sort by score descending, then by vector rank ascending (tie-break),
        // then by text rank ascending, then by row ID ascending (determinism)
        candidates.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            cmp = a.VectorRank.CompareTo(b.VectorRank);
            if (cmp != 0) return cmp;
            cmp = a.TextRank.CompareTo(b.TextRank);
            if (cmp != 0) return cmp;
            return a.RowId.CompareTo(b.RowId);
        });

        if (candidates.Count > topK)
            candidates.RemoveRange(topK, candidates.Count - topK);

        return candidates;
    }
}
