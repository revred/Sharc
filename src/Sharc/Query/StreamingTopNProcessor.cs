// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Streaming ORDER BY + LIMIT via a bounded heap with fast rejection.
/// Reads only ORDER BY columns to decide whether to materialize each row,
/// avoiding allocation for rows that won't enter the top-N.
/// </summary>
internal static class StreamingTopNProcessor
{
    internal static SharcDataReader Apply(
        SharcDataReader source,
        IReadOnlyList<OrderIntent> orderBy,
        long limitValue)
    {
        int fieldCount = source.FieldCount;
        var columnNames = source.GetColumnNames();

        // Build the "worst-first" comparison for the heap.
        // For ASC ordering, the worst row is the LARGEST (max-heap evicts max).
        // For DESC ordering, the worst row is the SMALLEST.
        var ordinals = new int[orderBy.Count];
        var descending = new bool[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
        {
            ordinals[i] = QueryValueOps.ResolveOrdinal(columnNames, orderBy[i].ColumnName);
            descending[i] = orderBy[i].Descending;
        }

        Comparison<QueryValue[]> worstFirst = (a, b) =>
        {
            for (int i = 0; i < ordinals.Length; i++)
            {
                int cmp = QueryValueOps.CompareValues(a[ordinals[i]], b[ordinals[i]]);
                if (cmp != 0)
                    return descending[i] ? -cmp : cmp;
            }
            return 0;
        };

        int limit = (int)Math.Min(limitValue, int.MaxValue);
        var heap = new TopNHeap(limit, worstFirst);
        QueryValue[]? spare = null;

        while (source.Read())
        {
            // Fast rejection: when heap is full, compare only ORDER BY columns
            // against the root before allocating the full row.
            if (heap.IsFull)
            {
                var root = heap.PeekRoot();
                bool rejected = true;
                for (int i = 0; i < ordinals.Length; i++)
                {
                    var sortVal = QueryValueOps.MaterializeColumn(source, ordinals[i]);
                    int cmp = QueryValueOps.CompareValues(sortVal, root[ordinals[i]]);
                    int effectiveCmp = descending[i] ? -cmp : cmp;
                    if (effectiveCmp < 0) { rejected = false; break; } // better than root
                    if (effectiveCmp > 0) break; // worse than root — skip
                    // equal on this column — check next ORDER BY column
                }
                if (rejected) continue; // skip full materialization
            }

            var row = spare ?? new QueryValue[fieldCount];
            spare = null;
            for (int i = 0; i < fieldCount; i++)
                row[i] = QueryValueOps.MaterializeColumn(source, i);

            if (heap.TryInsert(row, out var evicted))
                spare = evicted; // recycle evicted array
            else
                spare = row; // not inserted — reuse this array
        }
        source.Dispose();

        var sorted = heap.ExtractSorted();
        return new SharcDataReader(sorted, columnNames);
    }
}
