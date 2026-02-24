// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

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
