// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A compiled query plan: either a single-table intent or a compound intent,
/// optionally preceded by Cote definitions. This is the top-level cache unit.
/// </summary>
public sealed class QueryPlan
{
    /// <summary>The simple single-table intent (set when the query is not compound).</summary>
    public QueryIntent? Simple { get; init; }

    /// <summary>The compound query plan (set for UNION/INTERSECT/EXCEPT queries).</summary>
    public CompoundQueryPlan? Compound { get; init; }

    /// <summary>Cote (Common Table Expression) definitions preceding the main query, or null if none.</summary>
    public IReadOnlyList<CoteIntent>? Cotes { get; init; }

    /// <summary>True if the query is a compound (UNION/INTERSECT/EXCEPT).</summary>
    public bool IsCompound => Compound is not null;

    /// <summary>True if the query has Cote definitions.</summary>
    public bool HasCotes => Cotes is { Count: > 0 };

    /// <summary>
    /// Cached resolved intent for simple Cote queries (Cote → SELECT WHERE).
    /// Set on first execution so the same object reference is reused,
    /// enabling reader-info cache hits in <c>CreateReaderFromIntent</c>.
    /// </summary>
    internal QueryIntent? ResolvedSimple { get; set; }

    /// <summary>
    /// Cached resolved compound plan for Cote + compound queries.
    /// Set on first execution so streaming paths reuse cached reader info.
    /// </summary>
    internal CompoundQueryPlan? ResolvedCompound { get; set; }
}
