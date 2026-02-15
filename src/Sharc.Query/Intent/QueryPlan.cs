// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A compiled query plan: either a single-table intent or a compound intent,
/// optionally preceded by CTE definitions. This is the top-level cache unit.
/// </summary>
public sealed class QueryPlan
{
    /// <summary>The simple single-table intent (set when the query is not compound).</summary>
    public QueryIntent? Simple { get; init; }

    /// <summary>The compound query plan (set for UNION/INTERSECT/EXCEPT queries).</summary>
    public CompoundQueryPlan? Compound { get; init; }

    /// <summary>Common Table Expressions preceding the main query, or null if none.</summary>
    public IReadOnlyList<CteIntent>? Ctes { get; init; }

    /// <summary>True if the query is a compound (UNION/INTERSECT/EXCEPT).</summary>
    public bool IsCompound => Compound is not null;

    /// <summary>True if the query has CTE definitions.</summary>
    public bool HasCtes => Ctes is { Count: > 0 };
}
