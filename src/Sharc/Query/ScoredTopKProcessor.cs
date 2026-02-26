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
/// For cursor-backed readers, the hot path stores only (rowid, score) in the heap
/// and materializes full rows only for final winners.
/// For non-seekable/materialized readers, it falls back to row materialization on keep.
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

        return source.CanSeekByRowId
            ? ApplyWithLateMaterialization(source, k, scorer, fieldCount, columnNames)
            : ApplyEagerMaterialization(source, k, scorer, fieldCount, columnNames);
    }

    /// <summary>
    /// Lambda convenience overload for direct use without JitQuery.
    /// </summary>
    internal static SharcDataReader Apply(SharcDataReader source, int k, Func<IRowAccessor, double> scorer)
    {
        return Apply(source, k, new DelegateScorer(scorer));
    }

    private static SharcDataReader ApplyWithLateMaterialization(
        SharcDataReader source,
        int k,
        IRowScorer scorer,
        int fieldCount,
        string[] columnNames)
    {
        var heap = new ScoredRowIdEntry[k];
        int count = 0;

        while (source.Read())
        {
            double score = scorer.Score(source);
            long rowId = source.RowId;

            if (count < k)
            {
                heap[count] = new ScoredRowIdEntry(rowId, score);
                count++;
                SiftUp(heap, count - 1);
            }
            else if (score < heap[0].Score)
            {
                heap[0] = new ScoredRowIdEntry(rowId, score);
                SiftDown(heap, 0, count);
            }
        }

        if (count == 0)
        {
            source.Dispose();
            return new SharcDataReader(Array.Empty<QueryValue[]>(), columnNames);
        }

        var winners = ExtractSortedRowIds(heap, count);
        var rows = new QueryValue[count][];
        int write = 0;

        for (int i = 0; i < winners.Length; i++)
        {
            if (!source.Seek(winners[i].RowId))
                continue;

            rows[write++] = MaterializeRow(source, fieldCount);
        }

        source.Dispose();

        if (write == rows.Length)
            return new SharcDataReader(rows, columnNames);

        var compact = new QueryValue[write][];
        Array.Copy(rows, compact, write);
        return new SharcDataReader(compact, columnNames);
    }

    private static SharcDataReader ApplyEagerMaterialization(
        SharcDataReader source,
        int k,
        IRowScorer scorer,
        int fieldCount,
        string[] columnNames)
    {
        var heap = new ScoredEntry[k];
        int count = 0;

        while (source.Read())
        {
            double score = scorer.Score(source);

            if (count < k)
            {
                var row = MaterializeRow(source, fieldCount);
                heap[count] = new ScoredEntry(row, score);
                count++;
                SiftUp(heap, count - 1);
            }
            else if (score < heap[0].Score)
            {
                var row = MaterializeRow(source, fieldCount);
                heap[0] = new ScoredEntry(row, score);
                SiftDown(heap, 0, count);
            }
        }

        source.Dispose();

        var sorted = ExtractSortedRows(heap, count);
        return new SharcDataReader(sorted, columnNames);
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
    /// </summary>
    private static QueryValue[][] ExtractSortedRows(ScoredEntry[] heap, int count)
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

    /// <summary>
    /// Extracts rowid entries sorted ascending by score (best first).
    /// </summary>
    private static ScoredRowIdEntry[] ExtractSortedRowIds(ScoredRowIdEntry[] heap, int count)
    {
        var result = new ScoredRowIdEntry[count];
        int remaining = count;
        int writeIndex = remaining - 1;

        while (remaining > 0)
        {
            result[writeIndex--] = heap[0];
            remaining--;
            if (remaining > 0)
            {
                heap[0] = heap[remaining];
                SiftDown(heap, 0, remaining);
            }
        }

        return result;
    }

    // Max-heap operations (worst score at root for O(1) rejection)

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
            int left = (index * 2) + 1;
            int right = left + 1;
            int worst = index;

            if (left < count && heap[left].Score > heap[worst].Score)
                worst = left;
            if (right < count && heap[right].Score > heap[worst].Score)
                worst = right;

            if (worst == index)
                break;

            (heap[index], heap[worst]) = (heap[worst], heap[index]);
            index = worst;
        }
    }

    private static void SiftUp(ScoredRowIdEntry[] heap, int index)
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

    private static void SiftDown(ScoredRowIdEntry[] heap, int index, int count)
    {
        while (true)
        {
            int left = (index * 2) + 1;
            int right = left + 1;
            int worst = index;

            if (left < count && heap[left].Score > heap[worst].Score)
                worst = left;
            if (right < count && heap[right].Score > heap[worst].Score)
                worst = right;

            if (worst == index)
                break;

            (heap[index], heap[worst]) = (heap[worst], heap[index]);
            index = worst;
        }
    }

    // Internal types

    private readonly struct ScoredEntry(QueryValue[] row, double score)
    {
        internal readonly QueryValue[] Row = row;
        internal readonly double Score = score;
    }

    private readonly struct ScoredRowIdEntry(long rowId, double score)
    {
        internal readonly long RowId = rowId;
        internal readonly double Score = score;
    }

    private sealed class DelegateScorer(Func<IRowAccessor, double> fn) : IRowScorer
    {
        public double Score(IRowAccessor row) => fn(row);
    }
}
