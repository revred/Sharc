// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Results of a hybrid (vector + text) search, ordered by fused RRF score.
/// </summary>
public sealed class HybridSearchResult
{
    private readonly List<HybridMatch> _matches;

    internal HybridSearchResult(List<HybridMatch> matches) => _matches = matches;

    /// <summary>Number of matches.</summary>
    public int Count => _matches.Count;

    /// <summary>Gets match at the specified index.</summary>
    public HybridMatch this[int index] => _matches[index];

    /// <summary>All matches (ordered by fused RRF score descending).</summary>
    public IReadOnlyList<HybridMatch> Matches => _matches;

    /// <summary>The row IDs of matched rows.</summary>
    public IEnumerable<long> RowIds => _matches.Select(m => m.RowId);
}
