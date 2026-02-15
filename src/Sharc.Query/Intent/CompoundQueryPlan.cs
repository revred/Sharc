// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Set operation for compound SELECT queries.
/// </summary>
public enum CompoundOperator : byte
{
    /// <summary>UNION — combines results and removes duplicates.</summary>
    Union,

    /// <summary>UNION ALL — combines results keeping all duplicates.</summary>
    UnionAll,

    /// <summary>INTERSECT — returns only rows present in both sides.</summary>
    Intersect,

    /// <summary>EXCEPT — returns rows in the left side that are not in the right side.</summary>
    Except
}

/// <summary>
/// A compiled compound query plan (UNION/INTERSECT/EXCEPT) as a right-recursive tree.
/// Each leaf is a <see cref="QueryIntent"/> targeting a single table.
/// </summary>
public sealed class CompoundQueryPlan
{
    /// <summary>The left-side sub-query.</summary>
    public required QueryIntent Left { get; init; }

    /// <summary>The set operation connecting left and right.</summary>
    public required CompoundOperator Operator { get; init; }

    /// <summary>The right-side as a simple query (set when the right is a leaf).</summary>
    public QueryIntent? RightSimple { get; init; }

    /// <summary>The right-side as another compound (set when the right is a chain).</summary>
    public CompoundQueryPlan? RightCompound { get; init; }

    /// <summary>ORDER BY hoisted from the deepest rightmost leaf — applies to the FINAL combined result.</summary>
    public IReadOnlyList<OrderIntent>? FinalOrderBy { get; init; }

    /// <summary>LIMIT hoisted from the deepest rightmost leaf — applies to the FINAL combined result.</summary>
    public long? FinalLimit { get; init; }

    /// <summary>OFFSET hoisted from the deepest rightmost leaf — applies to the FINAL combined result.</summary>
    public long? FinalOffset { get; init; }
}
