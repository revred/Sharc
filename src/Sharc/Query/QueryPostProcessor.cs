// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Sharc.Query.Execution;
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
        bool needsCase = intent.HasCaseExpressions;

        if (!needsAggregate && !needsDistinct && !needsSort && !needsLimit && !needsCase)
            return source;

        // Streaming top-N: ORDER BY + LIMIT without full materialization
        if (needsSort && needsLimit && !needsAggregate && !needsDistinct && !needsCase
            && intent.Limit.HasValue)
        {
            return StreamingTopNProcessor.Apply(
                source, intent.OrderBy!, intent.Limit!.Value, intent.Offset ?? 0);
        }

        // Streaming aggregate: GROUP BY + aggregates without full materialization
        if (needsAggregate && !needsDistinct && !needsCase)
        {
            return StreamingAggregateProcessor.Apply(source, intent, needsSort, needsLimit);
        }

        // Fuse distinct into materialization when possible: reuse arrays
        // of duplicate rows (spare-array pattern) instead of allocating all
        // rows and then discarding duplicates in a separate pass.
        bool fuseDistinct = needsDistinct && !needsAggregate;

        // Materialize all rows from the source reader into unboxed QueryValue arrays
        var (rows, columnNames) = Materialize(source, checkDistinct: fuseDistinct);
        source.Dispose();

        // Pipeline: Aggregate+GroupBy → Sort → Limit/Offset → CASE → Distinct (SQL semantics)
        // Sort and Limit operate on physical columns (before CASE projection),
        // because ORDER BY may reference columns not in the final SELECT list.
        if (needsAggregate)
        {
            (rows, columnNames) = AggregateProcessor.Apply(
                rows, columnNames,
                intent.Aggregates!,
                intent.GroupBy,
                intent.Columns);
        }

        // Sort on physical rows (ORDER BY may use columns not in SELECT)
        if (needsSort)
            ApplyOrderBy(rows, intent.OrderBy!, columnNames);

        if (needsLimit)
            rows = ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        // CASE expression evaluation: rebuild rows from physical to output schema
        if (needsCase)
            (rows, columnNames) = ApplyCaseExpressions(rows, columnNames, intent);

        // Skip distinct if already handled during materialization
        if (needsDistinct && !fuseDistinct)
            rows = SetOperationProcessor.ApplyDistinct(rows, columnNames.Length);

        return new SharcDataReader(rows, columnNames);
    }

    // ─── CASE expression evaluation ─────────────────────────────

    /// <summary>
    /// Evaluates CASE expressions and rebuilds rows to match the intended output schema.
    /// Physical rows contain source columns; output rows have CASE results at their intended ordinals.
    /// </summary>
    private static MaterializedResultSet ApplyCaseExpressions(
        RowSet rows, string[] physicalColumnNames, QueryIntent intent)
    {
        var caseExprs = intent.CaseExpressions!;
        var outputColumns = intent.Columns!;
        int outputWidth = outputColumns.Count;
        var outputNames = new string[outputWidth];

        // Build output column names
        for (int i = 0; i < outputWidth; i++)
            outputNames[i] = outputColumns[i];

        // Build mapping: for each output ordinal, either it's a CASE expr or a physical column
        var caseByOrdinal = new Dictionary<int, CaseExpressionIntent>();
        foreach (var ce in caseExprs)
            caseByOrdinal[ce.OutputOrdinal] = ce;

        // Map non-CASE output columns to their physical column ordinal
        var physicalOrdinalMap = new int[outputWidth];
        for (int i = 0; i < outputWidth; i++)
        {
            if (caseByOrdinal.ContainsKey(i))
            {
                physicalOrdinalMap[i] = -1; // Will be computed
            }
            else
            {
                physicalOrdinalMap[i] = QueryValueOps.TryResolveOrdinal(physicalColumnNames, outputColumns[i]);
                if (physicalOrdinalMap[i] < 0)
                    throw new InvalidOperationException(
                        $"Column '{outputColumns[i]}' not found in physical result set.");
            }
        }

        // Rebuild each row
        for (int r = 0; r < rows.Count; r++)
        {
            var physicalRow = rows[r];
            var outputRow = new QueryValue[outputWidth];

            for (int c = 0; c < outputWidth; c++)
            {
                if (caseByOrdinal.TryGetValue(c, out var ce))
                {
                    outputRow[c] = CaseExpressionEvaluator.Evaluate(
                        ce.Expression, physicalRow, physicalColumnNames);
                }
                else
                {
                    outputRow[c] = physicalRow[physicalOrdinalMap[c]];
                }
            }

            rows[r] = outputRow;
        }

        return new MaterializedResultSet(rows, outputNames);
    }

    // ─── Materialization ──────────────────────────────────────────

    internal static MaterializedResultSet Materialize(SharcDataReader reader, bool checkDistinct = false)
    {
        int fieldCount = reader.FieldCount;
        var columnNames = reader.GetColumnNames();

        var rows = new RowSet();
        var dedup = checkDistinct ? new RowDeduplicator(fieldCount) : null;

        while (reader.Read())
        {
            var row = dedup?.Spare ?? new QueryValue[fieldCount];
            if (dedup != null) dedup.Spare = null;
            for (int i = 0; i < fieldCount; i++)
                row[i] = QueryValueOps.MaterializeColumn(reader, i);

            if (dedup != null && !dedup.TryAdd(row))
                continue;
            rows.Add(row);
        }

        return new MaterializedResultSet(rows, columnNames);
    }

    /// <summary>
    /// Tracks unique rows during materialization using structural equality.
    /// Recycles arrays of duplicate rows via <see cref="Spare"/>.
    /// </summary>
    private sealed class RowDeduplicator
    {
        private readonly HashSet<QueryValue[]> _seen;
        public QueryValue[]? Spare;

        public RowDeduplicator(int columnCount)
        {
            _seen = new HashSet<QueryValue[]>(new QueryValueOps.QvRowEqualityComparer(columnCount));
        }

        /// <summary>Returns true if the row is unique. On false, sets <see cref="Spare"/>.</summary>
        public bool TryAdd(QueryValue[] row)
        {
            if (_seen.Add(row)) return true;
            Spare = row;
            return false;
        }
    }

    // ─── ORDER BY ─────────────────────────────────────────────────

    internal static void ApplyOrderBy(
        RowSet rows,
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

    internal static RowSet ApplyLimitOffset(
        RowSet rows,
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
