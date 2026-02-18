// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Applies post-processing to query results: DISTINCT, ORDER BY, LIMIT/OFFSET.
/// Materializes the source reader into unboxed <see cref="QueryValue"/> rows,
/// transforms, and returns a new reader.
/// </summary>
internal static class QueryPostProcessor
{
    /// <summary>
    /// Applies post-processing pipeline if needed. Returns the source reader unchanged
    /// if no post-processing is required.
    /// </summary>
    internal static SharcDataReader Apply(
        SharcDataReader source,
        QueryIntent intent)
    {
        bool needsAggregate = intent.HasAggregates;
        bool needsDistinct = intent.IsDistinct;
        bool needsSort = intent.OrderBy is { Count: > 0 };
        bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;

        if (!needsAggregate && !needsDistinct && !needsSort && !needsLimit)
            return source;

        // Streaming top-N: ORDER BY + LIMIT without full materialization
        if (needsSort && needsLimit && !needsAggregate && !needsDistinct
            && intent.Limit.HasValue)
        {
            return StreamingTopNProcessor.Apply(
                source, intent.OrderBy!, intent.Limit!.Value, intent.Offset ?? 0);
        }

        // Streaming aggregate: GROUP BY + aggregates without full materialization
        if (needsAggregate && !needsDistinct)
        {
            return StreamingAggregateProcessor.Apply(source, intent, needsSort, needsLimit);
        }

        // Materialize all rows from the source reader into unboxed QueryValue arrays
        var (rows, columnNames) = Materialize(source);
        source.Dispose();

        // Pipeline: Aggregate+GroupBy → Distinct → Sort → Limit/Offset (SQL semantics)
        if (needsAggregate)
        {
            (rows, columnNames) = AggregateProcessor.Apply(
                rows, columnNames,
                intent.Aggregates!,
                intent.GroupBy,
                intent.Columns);
        }

        if (needsDistinct)
            rows = SetOperationProcessor.ApplyDistinct(rows, columnNames.Length);

        if (needsSort)
            ApplyOrderBy(rows, intent.OrderBy!, columnNames);

        if (needsLimit)
            rows = ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        return new SharcDataReader(rows, columnNames);
    }

    // ─── Materialization ──────────────────────────────────────────

    internal static MaterializedResultSet Materialize(SharcDataReader reader)
    {
        int fieldCount = reader.FieldCount;
        var columnNames = reader.GetColumnNames();

        var rows = new List<QueryValue[]>();
        while (reader.Read())
        {
            var row = new QueryValue[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                row[i] = QueryValueOps.MaterializeColumn(reader, i);
            rows.Add(row);
        }

        return new MaterializedResultSet(rows, columnNames);
    }

    // ─── ORDER BY ─────────────────────────────────────────────────

    internal static void ApplyOrderBy(
        List<QueryValue[]> rows,
        IReadOnlyList<OrderIntent> orderBy,
        string[] columnNames)
    {
        var ordinals = new int[orderBy.Count];
        var descending = new bool[orderBy.Count];

        for (int i = 0; i < orderBy.Count; i++)
        {
            ordinals[i] = QueryValueOps.ResolveOrdinal(columnNames, orderBy[i].ColumnName);
            descending[i] = orderBy[i].Descending;
        }

        // Span.Sort with struct comparer: JIT specializes the generic,
        // eliminating delegate allocation and enabling inlining.
        var comparer = new QueryValueOps.RowComparer(ordinals, descending);
        CollectionsMarshal.AsSpan(rows).Sort(comparer);
    }

    // RowComparer moved to QueryValueOps.RowComparer — use that directly.

    // Forwarders to QueryValueOps — kept for backward compatibility until Phase 2 decomposition.
    internal static int ResolveOrdinal(string[] columnNames, string name)
        => QueryValueOps.ResolveOrdinal(columnNames, name);

    internal static int CompareValues(QueryValue a, QueryValue b)
        => QueryValueOps.CompareValues(a, b);

    // ─── LIMIT / OFFSET ──────────────────────────────────────────

    internal static List<QueryValue[]> ApplyLimitOffset(
        List<QueryValue[]> rows,
        long? limit,
        long? offset)
    {
        int start = offset.HasValue ? (int)Math.Min(offset.Value, rows.Count) : 0;
        int remaining = rows.Count - start;
        int count = limit.HasValue ? (int)Math.Min(limit.Value, remaining) : remaining;
        count = Math.Max(0, count);

        if (start == 0 && count == rows.Count)
            return rows;

        // In-place trimming: trim end first (no shifting), then trim start.
        // Avoids GetRange() which allocates a new List + copies all references.
        int end = start + count;
        if (end < rows.Count)
            rows.RemoveRange(end, rows.Count - end);
        if (start > 0)
            rows.RemoveRange(0, start);
        return rows;
    }

}
