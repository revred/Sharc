// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Views;

namespace Sharc.Query;

/// <summary>
/// Streaming scored top-K processor. Scans a reader, scores each row via
/// an <see cref="IRowScorer"/>, maintains a bounded max-heap of size K,
/// and returns a materialized reader with K rows sorted ascending by score.
/// </summary>
/// <remarks>
/// Rows that score worse than the current worst in the heap are never
/// materialized (no <see cref="QueryValue"/>[] allocation). The scorer
/// runs on the lazy <see cref="IRowAccessor"/> which only decodes accessed columns.
/// </remarks>
internal static class ScoredTopKProcessor
{
    /// <summary>
    /// Applies streaming top-K selection with a custom scorer.
    /// </summary>
    internal static SharcDataReader Apply(SharcDataReader source, int k, IRowScorer scorer)
    {
        int fieldCount = source.FieldCount;
        var columnNames = source.GetColumnNames();

        if (k <= 0)
        {
            source.Dispose();
            return new SharcDataReader(Array.Empty<QueryValue[]>(), columnNames);
        }

        // Scored heap entries: (materialized row, score)
        var heap = new ScoredEntry[k];
        int count = 0;

        while (source.Read())
        {
            double score = scorer.Score(source);

            if (count < k)
            {
                // Heap not full - always insert
                var row = MaterializeRow(source, fieldCount);
                heap[count] = new ScoredEntry(row, score);
                count++;
                SiftUp(heap, count - 1);
            }
            else if (score < heap[0].Score)
            {
                // Better than worst in heap - replace root
                var row = MaterializeRow(source, fieldCount);
                heap[0] = new ScoredEntry(row, score);
                SiftDown(heap, 0, count);
            }
            // else: worse than heap worst - skip (no materialization)
        }

        source.Dispose();

        // Extract sorted ascending by score
        var sorted = ExtractSorted(heap, count);
        return new SharcDataReader(sorted, columnNames);
    }

    /// <summary>
    /// Lambda convenience overload for direct use without JitQuery.
    /// </summary>
    internal static SharcDataReader Apply(SharcDataReader source, int k, Func<IRowAccessor, double> scorer)
    {
        return Apply(source, k, new DelegateScorer(scorer));
    }

    private static QueryValue[] MaterializeRow(SharcDataReader source, int fieldCount)
    {
        var row = new QueryValue[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            row[i] = QueryValueOps.MaterializeColumn(source, i);
        return row;
    }

    /// <summary>
    /// Extracts all entries sorted ascending by score (best first).
    /// Uses heap-sort: repeatedly extract root (worst remaining),
    /// write into reversed position to produce best-first order.
    /// </summary>
    private static QueryValue[][] ExtractSorted(ScoredEntry[] heap, int count)
    {
        var result = new QueryValue[count][];
        int remaining = count;
        int writeIndex = remaining - 1;

        while (remaining > 0)
        {
            result[writeIndex--] = heap[0].Row;
            remaining--;
            if (remaining > 0)
            {
                heap[0] = heap[remaining];
                SiftDown(heap, 0, remaining);
            }
        }

        return result;
    }

    // ── Max-heap operations (worst score at root for O(1) rejection) ──

    private static void SiftUp(ScoredEntry[] heap, int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[index].Score > heap[parent].Score)
            {
                (heap[index], heap[parent]) = (heap[parent], heap[index]);
                index = parent;
            }
            else
            {
                break;
            }
        }
    }

    private static void SiftDown(ScoredEntry[] heap, int index, int count)
    {
        while (true)
        {
            int left = 2 * index + 1;
            int right = 2 * index + 2;
            int worst = index;

            if (left < count && heap[left].Score > heap[worst].Score)
                worst = left;
            if (right < count && heap[right].Score > heap[worst].Score)
                worst = right;

            if (worst == index) break;
            (heap[index], heap[worst]) = (heap[worst], heap[index]);
            index = worst;
        }
    }

    // ── Internal types ──────────────────────────────────────────────

    private struct ScoredEntry(QueryValue[] row, double score)
    {
        internal readonly QueryValue[] Row = row;
        internal readonly double Score = score;
    }

    private sealed class DelegateScorer(Func<IRowAccessor, double> fn) : IRowScorer
    {
        public double Score(IRowAccessor row) => fn(row);
    }
}
