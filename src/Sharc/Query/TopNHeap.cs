// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query;

/// <summary>
/// A bounded max-heap of size K that keeps the "best" K rows seen so far.
/// Used for streaming ORDER BY + LIMIT: scan N rows maintaining O(K) memory.
/// </summary>
/// <remarks>
/// The heap stores at most <c>capacity</c> rows. When full, new rows are compared
/// against the worst (root) element. If the new row is better, the root is replaced
/// and the heap is sifted down. At the end, <see cref="ExtractSorted"/> returns
/// all rows in ascending order.
///
/// The "worst" element is at the root: for ASC ordering, the root is the largest;
/// for DESC ordering, the root is the smallest. This ensures that when we evict,
/// we always keep the best K rows.
/// </remarks>
internal sealed class TopNHeap
{
    private readonly QueryValue[][] _heap;
    private readonly int _capacity;
    private readonly Comparison<QueryValue[]> _worstFirst;
    private int _count;

    /// <summary>
    /// Creates a new top-N heap.
    /// </summary>
    /// <param name="capacity">Maximum number of rows to retain (the LIMIT value).</param>
    /// <param name="worstFirst">Comparison that orders the "worst" row first (positive = a is worse).
    /// For ASC sort, this means larger values are "worse" (max-heap).
    /// For DESC sort, smaller values are "worse" (min-heap).</param>
    internal TopNHeap(int capacity, Comparison<QueryValue[]> worstFirst)
    {
        _capacity = capacity;
        _worstFirst = worstFirst;
        _heap = new QueryValue[capacity][];
        _count = 0;
    }

    /// <summary>Number of rows currently in the heap.</summary>
    internal int Count => _count;

    /// <summary>Whether the heap is at capacity.</summary>
    internal bool IsFull => _count >= _capacity;

    /// <summary>
    /// Returns the "worst" retained row (heap root) without removing it.
    /// Used for fast rejection: compare incoming ORDER BY columns against
    /// the root before allocating the full row.
    /// </summary>
    internal QueryValue[] PeekRoot() => _heap[0];

    /// <summary>
    /// Tries to insert a row. If the heap is full and the row is better than
    /// the worst element, replaces the worst and re-heapifies.
    /// </summary>
    internal void TryInsert(QueryValue[] row)
    {
        if (_count < _capacity)
        {
            _heap[_count] = row;
            _count++;
            SiftUp(_count - 1);
        }
        else if (_worstFirst(row, _heap[0]) < 0)
        {
            // New row is better than the worst — replace root
            _heap[0] = row;
            SiftDown(0);
        }
    }

    /// <summary>
    /// Extracts all rows sorted in the desired order (ascending by the original comparison).
    /// </summary>
    internal List<QueryValue[]> ExtractSorted()
    {
        var result = new List<QueryValue[]>(_count);
        // Heap-sort: repeatedly extract the root (worst remaining) to get sorted order
        int remaining = _count;
        while (remaining > 0)
        {
            result.Add(_heap[0]);
            remaining--;
            if (remaining > 0)
            {
                _heap[0] = _heap[remaining];
                _heap[remaining] = null!;
                SiftDownRange(0, remaining);
            }
        }
        // Result is in worst-first order — reverse for best-first (desired sort order)
        result.Reverse();
        return result;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_worstFirst(_heap[index], _heap[parent]) > 0)
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

            if (left < count && _worstFirst(_heap[left], _heap[worst]) > 0)
                worst = left;
            if (right < count && _worstFirst(_heap[right], _heap[worst]) > 0)
                worst = right;

            if (worst == index) break;

            (_heap[index], _heap[worst]) = (_heap[worst], _heap[index]);
            index = worst;
        }
    }
}
