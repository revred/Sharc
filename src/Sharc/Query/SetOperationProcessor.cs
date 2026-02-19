// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Set operations on materialized <see cref="QueryValue"/> rows:
/// DISTINCT, UNION, INTERSECT, EXCEPT.
/// </summary>
internal static class SetOperationProcessor
{
    /// <summary>
    /// Removes duplicate rows using structural equality (element-by-element comparison).
    /// Preserves first-occurrence ordering.
    /// </summary>
    internal static RowSet ApplyDistinct(RowSet rows, int columnCount)
    {
        var comparer = new QueryValueOps.QvRowEqualityComparer(columnCount);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet(rows.Count);

        foreach (var row in rows)
        {
            if (seen.Add(row))
                result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// Applies the specified compound set operation (UNION ALL, UNION, INTERSECT, EXCEPT).
    /// </summary>
    internal static RowSet Apply(
        CompoundOperator op,
        RowSet left,
        RowSet right,
        int columnCount)
    {
        return op switch
        {
            CompoundOperator.UnionAll => UnionAll(left, right),
            CompoundOperator.Union => Union(left, right, columnCount),
            CompoundOperator.Intersect => Intersect(left, right, columnCount),
            CompoundOperator.Except => Except(left, right, columnCount),
            _ => throw new NotSupportedException($"Unknown compound operator: {op}"),
        };
    }

    private static RowSet UnionAll(RowSet left, RowSet right)
    {
        var result = new RowSet(left.Count + right.Count);
        result.AddRange(left);
        result.AddRange(right);
        return result;
    }

    private static RowSet Union(RowSet left, RowSet right, int colCount)
    {
        var comparer = new QueryValueOps.QvRowEqualityComparer(colCount);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet(left.Count + right.Count);

        foreach (var row in left)
        {
            if (seen.Add(row))
                result.Add(row);
        }
        foreach (var row in right)
        {
            if (seen.Add(row))
                result.Add(row);
        }
        return result;
    }

    private static RowSet Intersect(RowSet left, RowSet right, int colCount)
    {
        var comparer = new QueryValueOps.QvRowEqualityComparer(colCount);
        var rightSet = new HashSet<QueryValue[]>(right, comparer);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet();

        foreach (var row in left)
        {
            if (rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
        }
        return result;
    }

    private static RowSet Except(RowSet left, RowSet right, int colCount)
    {
        var comparer = new QueryValueOps.QvRowEqualityComparer(colCount);
        var rightSet = new HashSet<QueryValue[]>(right, comparer);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new RowSet();

        foreach (var row in left)
        {
            if (!rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
        }
        return result;
    }
}
