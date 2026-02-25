// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Text;
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
        long limitValue,
        long offsetValue = 0)
    {
        int fieldCount = source.FieldCount;
        var columnNames = source.GetColumnNames();
        int heapSize = (int)Math.Min(limitValue + offsetValue, int.MaxValue);

        if (heapSize <= 0)
        {
            source.Dispose();
            return new SharcDataReader(Array.Empty<QueryValue[]>(), columnNames);
        }

        if (orderBy.Count == 1)
        {
            int ordinal = QueryValueOps.ResolveOrdinal(columnNames, orderBy[0].ColumnName);
            return ApplySingleOrderByDeferred(
                source,
                columnNames,
                fieldCount,
                ordinal,
                orderBy[0].Descending,
                heapSize,
                offsetValue);
        }

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

        var comparer = new WorstFirstComparer(ordinals, descending);
        var heap = new TopNHeap<WorstFirstComparer>(heapSize, comparer);
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
                    if (effectiveCmp > 0) break; // worse than root - skip
                    // equal on this column - check next ORDER BY column
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
                spare = row; // not inserted - reuse this array
        }
        source.Dispose();

        var sorted = heap.ExtractSorted();
        return BuildReaderWithOffset(sorted, columnNames, offsetValue);
    }

    private static SharcDataReader ApplySingleOrderByDeferred(
        SharcDataReader source,
        string[] columnNames,
        int fieldCount,
        int sortOrdinal,
        bool descending,
        int heapSize,
        long offsetValue)
    {
        var comparer = new DeferredSingleKeyComparer(descending);
        var heap = new DeferredRowHeap(heapSize, comparer);
        DeferredRow? spare = null;

        while (source.Read())
        {
            var sortValue = QueryValueOps.MaterializeColumn(source, sortOrdinal);

            if (heap.IsFull)
            {
                var root = heap.PeekRoot();
                int cmp = QueryValueOps.CompareValues(sortValue, root.SortKey);
                int effectiveCmp = descending ? -cmp : cmp;
                if (effectiveCmp >= 0)
                    continue;
            }

            var row = spare ?? new DeferredRow(fieldCount);
            spare = null;
            CaptureDeferredRow(source, row, fieldCount, sortOrdinal, sortValue);

            if (heap.TryInsert(row, out var evicted))
                spare = evicted;
            else
                spare = row;
        }

        source.Dispose();
        var sorted = heap.ExtractSorted();
        return BuildReaderFromDeferred(sorted, columnNames, offsetValue, fieldCount);
    }

    private static SharcDataReader BuildReaderWithOffset(
        QueryValue[][] sorted,
        string[] columnNames,
        long offsetValue)
    {
        if (offsetValue > 0 && offsetValue < sorted.Length)
        {
            int skip = (int)Math.Min(offsetValue, sorted.Length);
            sorted = sorted[skip..];
        }
        else if (offsetValue >= sorted.Length)
        {
            sorted = [];
        }

        return new SharcDataReader(sorted, columnNames);
    }

    private static void CaptureDeferredRow(
        SharcDataReader source,
        DeferredRow target,
        int fieldCount,
        int sortOrdinal,
        QueryValue sortValue)
    {
        target.SortKey = sortValue;
        for (int i = 0; i < fieldCount; i++)
        {
            ref var cell = ref target.Cells[i];

            // Reuse already materialized sort key to avoid double decoding.
            if (i == sortOrdinal)
            {
                CaptureFromQueryValue(ref cell, sortValue);
                continue;
            }

            var type = source.GetColumnType(i);
            switch (type)
            {
                case SharcColumnType.Integral:
                    cell.Type = QueryValueType.Int64;
                    cell.Int64Value = source.GetInt64(i);
                    cell.Length = 0;
                    break;

                case SharcColumnType.Real:
                    cell.Type = QueryValueType.Double;
                    cell.DoubleValue = source.GetDouble(i);
                    cell.Length = 0;
                    break;

                case SharcColumnType.Text:
                {
                    var utf8 = source.GetUtf8Span(i);
                    EnsureCapacity(ref cell, utf8.Length);
                    if (utf8.Length > 0)
                        utf8.CopyTo(cell.Buffer!);
                    cell.Type = QueryValueType.Text;
                    cell.Length = utf8.Length;
                    break;
                }

                case SharcColumnType.Blob:
                {
                    var blob = source.GetBlobSpan(i);
                    EnsureCapacity(ref cell, blob.Length);
                    if (blob.Length > 0)
                        blob.CopyTo(cell.Buffer!);
                    cell.Type = QueryValueType.Blob;
                    cell.Length = blob.Length;
                    break;
                }

                default:
                    cell.Type = QueryValueType.Null;
                    cell.Length = 0;
                    break;
            }
        }
    }

    private static void CaptureFromQueryValue(ref DeferredCell cell, QueryValue value)
    {
        switch (value.Type)
        {
            case QueryValueType.Int64:
                cell.Type = QueryValueType.Int64;
                cell.Int64Value = value.AsInt64();
                cell.Length = 0;
                return;

            case QueryValueType.Double:
                cell.Type = QueryValueType.Double;
                cell.DoubleValue = value.AsDouble();
                cell.Length = 0;
                return;

            case QueryValueType.Text:
            {
                var utf8 = Encoding.UTF8.GetBytes(value.AsString());
                EnsureCapacity(ref cell, utf8.Length);
                if (utf8.Length > 0)
                    utf8.CopyTo(cell.Buffer!, 0);
                cell.Type = QueryValueType.Text;
                cell.Length = utf8.Length;
                return;
            }

            case QueryValueType.Blob:
            {
                var blob = value.AsBlob();
                EnsureCapacity(ref cell, blob.Length);
                if (blob.Length > 0)
                    blob.CopyTo(cell.Buffer!, 0);
                cell.Type = QueryValueType.Blob;
                cell.Length = blob.Length;
                return;
            }

            default:
                cell.Type = QueryValueType.Null;
                cell.Length = 0;
                return;
        }
    }

    private static void EnsureCapacity(ref DeferredCell cell, int length)
    {
        if (length == 0)
        {
            if (cell.Buffer != null)
                cell.Buffer.AsSpan().Clear();
            return;
        }

        if (cell.Buffer == null || cell.Buffer.Length < length)
            cell.Buffer = new byte[length];
    }

    private static SharcDataReader BuildReaderFromDeferred(
        DeferredRow[] sorted,
        string[] columnNames,
        long offsetValue,
        int fieldCount)
    {
        int skip = offsetValue > 0
            ? (int)Math.Min(offsetValue, sorted.Length)
            : 0;

        int outputCount = Math.Max(0, sorted.Length - skip);
        var output = new QueryValue[outputCount][];
        for (int i = 0; i < outputCount; i++)
            output[i] = MaterializeDeferredRow(sorted[skip + i], fieldCount);

        return new SharcDataReader(output, columnNames);
    }

    private static QueryValue[] MaterializeDeferredRow(DeferredRow row, int fieldCount)
    {
        var values = new QueryValue[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            ref var cell = ref row.Cells[i];
            values[i] = cell.Type switch
            {
                QueryValueType.Int64 => QueryValue.FromInt64(cell.Int64Value),
                QueryValueType.Double => QueryValue.FromDouble(cell.DoubleValue),
                QueryValueType.Text => QueryValue.FromString(
                    cell.Length == 0 ? string.Empty : Encoding.UTF8.GetString(cell.Buffer!, 0, cell.Length)),
                QueryValueType.Blob => QueryValue.FromBlob(
                    cell.Length == 0 ? Array.Empty<byte>() : CopyExact(cell.Buffer!, cell.Length)),
                _ => QueryValue.Null,
            };
        }
        return values;
    }

    private static byte[] CopyExact(byte[] source, int length)
    {
        if (length == source.Length)
            return source;

        var copy = new byte[length];
        Buffer.BlockCopy(source, 0, copy, 0, length);
        return copy;
    }

    /// <summary>
    /// Struct comparer for the TopN heap - enables JIT specialization (no delegate/closure alloc).
    /// Orders rows so the "worst" compares as positive (max-heap root = worst retained row).
    /// </summary>
    internal readonly struct WorstFirstComparer : IComparer<QueryValue[]>
    {
        private readonly int[] _ordinals;
        private readonly bool[] _descending;

        internal WorstFirstComparer(int[] ordinals, bool[] descending)
        {
            _ordinals = ordinals;
            _descending = descending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(QueryValue[]? a, QueryValue[]? b)
        {
            for (int i = 0; i < _ordinals.Length; i++)
            {
                int cmp = QueryValueOps.CompareValues(a![_ordinals[i]], b![_ordinals[i]]);
                if (cmp != 0)
                    return _descending[i] ? -cmp : cmp;
            }
            return 0;
        }
    }

    /// <summary>
    /// Single-key variant that avoids the per-comparison ORDER BY loop.
    /// </summary>
    internal readonly struct SingleKeyWorstFirstComparer : IComparer<QueryValue[]>
    {
        private readonly int _ordinal;
        private readonly bool _descending;

        internal SingleKeyWorstFirstComparer(int ordinal, bool descending)
        {
            _ordinal = ordinal;
            _descending = descending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(QueryValue[]? a, QueryValue[]? b)
        {
            int cmp = QueryValueOps.CompareValues(a![_ordinal], b![_ordinal]);
            return _descending ? -cmp : cmp;
        }
    }

    private sealed class DeferredRowHeap
    {
        private readonly DeferredRow[] _heap;
        private readonly int _capacity;
        private readonly DeferredSingleKeyComparer _comparer;
        private int _count;

        internal DeferredRowHeap(int capacity, DeferredSingleKeyComparer comparer)
        {
            _capacity = capacity;
            _comparer = comparer;
            _heap = new DeferredRow[capacity];
            _count = 0;
        }

        internal bool IsFull => _count >= _capacity;

        internal DeferredRow PeekRoot() => _heap[0];

        internal bool TryInsert(DeferredRow row, out DeferredRow? evicted)
        {
            evicted = null;
            if (_count < _capacity)
            {
                _heap[_count] = row;
                _count++;
                SiftUp(_count - 1);
                return true;
            }

            if (_comparer.Compare(row, _heap[0]) < 0)
            {
                evicted = _heap[0];
                _heap[0] = row;
                SiftDown(0);
                return true;
            }

            return false;
        }

        internal DeferredRow[] ExtractSorted()
        {
            var result = new DeferredRow[_count];
            int remaining = _count;
            int writeIndex = remaining - 1;
            while (remaining > 0)
            {
                result[writeIndex--] = _heap[0];
                remaining--;
                if (remaining > 0)
                {
                    _heap[0] = _heap[remaining];
                    _heap[remaining] = null!;
                    SiftDownRange(0, remaining);
                }
            }
            return result;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_comparer.Compare(_heap[index], _heap[parent]) > 0)
                {
                    (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
                    index = parent;
                }
                else
                {
                    break;
                }
            }
        }

        private void SiftDown(int index) => SiftDownRange(index, _count);

        private void SiftDownRange(int index, int count)
        {
            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int worst = index;

                if (left < count && _comparer.Compare(_heap[left], _heap[worst]) > 0)
                    worst = left;
                if (right < count && _comparer.Compare(_heap[right], _heap[worst]) > 0)
                    worst = right;

                if (worst == index) break;
                (_heap[index], _heap[worst]) = (_heap[worst], _heap[index]);
                index = worst;
            }
        }
    }

    private readonly struct DeferredSingleKeyComparer : IComparer<DeferredRow>
    {
        private readonly bool _descending;

        internal DeferredSingleKeyComparer(bool descending)
        {
            _descending = descending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(DeferredRow? a, DeferredRow? b)
        {
            int cmp = QueryValueOps.CompareValues(a!.SortKey, b!.SortKey);
            return _descending ? -cmp : cmp;
        }
    }

    private sealed class DeferredRow
    {
        internal readonly DeferredCell[] Cells;
        internal QueryValue SortKey;

        internal DeferredRow(int fieldCount)
        {
            Cells = new DeferredCell[fieldCount];
            SortKey = QueryValue.Null;
        }
    }

    private struct DeferredCell
    {
        internal QueryValueType Type;
        internal long Int64Value;
        internal double DoubleValue;
        internal byte[]? Buffer;
        internal int Length;
    }
}
