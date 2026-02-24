// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Fixed-capacity min/max-heap for top-K nearest neighbor selection.
/// Modeled after <c>TopNHeap&lt;T&gt;</c> in the query pipeline.
/// </summary>
/// <remarks>
/// For Cosine/Euclidean (lower = better), uses a max-heap so the root holds the
/// worst (largest) retained distance — new candidates replace the root if they're
/// better (smaller). For DotProduct (higher = better), uses a min-heap with
/// inverted comparison logic.
/// </remarks>
internal sealed class VectorTopKHeap
{
    private readonly (long RowId, float Distance, IReadOnlyDictionary<string, object?>? Metadata)[] _heap;
    private readonly int _capacity;
    private readonly bool _isMinHeap;
    private int _count;

    /// <summary>
    /// Creates a new top-K heap.
    /// </summary>
    /// <param name="k">Maximum number of results to retain.</param>
    /// <param name="isMinHeap">
    /// True = keep smallest distances (Cosine/Euclidean).
    /// False = keep largest values (DotProduct).
    /// </param>
    internal VectorTopKHeap(int k, bool isMinHeap)
    {
        _capacity = k;
        _isMinHeap = isMinHeap;
        _heap = new (long, float, IReadOnlyDictionary<string, object?>?)[k];
    }

    /// <summary>
    /// Tries to insert a candidate. If the heap is full, replaces the worst
    /// element if the candidate is better.
    /// </summary>
    internal void TryInsert(long rowId, float distance, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        if (_count < _capacity)
        {
            _heap[_count] = (rowId, distance, metadata);
            _count++;
            if (_count == _capacity) BuildHeap();
        }
        else
        {
            // Compare against root (worst element)
            bool isBetter = _isMinHeap
                ? distance < _heap[0].Distance
                : distance > _heap[0].Distance;

            if (isBetter)
            {
                _heap[0] = (rowId, distance, metadata);
                SiftDown(0);
            }
        }
    }

    /// <summary>
    /// Extracts all results sorted by distance (ascending for min-heap, descending for max-heap).
    /// </summary>
    internal VectorSearchResult ToResult()
    {
        var results = new List<VectorMatch>(_count);
        for (int i = 0; i < _count; i++)
            results.Add(new VectorMatch(_heap[i].RowId, _heap[i].Distance, _heap[i].Metadata));

        results.Sort((a, b) => _isMinHeap
            ? a.Distance.CompareTo(b.Distance)
            : b.Distance.CompareTo(a.Distance));

        return new VectorSearchResult(results);
    }

    private void BuildHeap()
    {
        for (int i = _count / 2 - 1; i >= 0; i--)
            SiftDown(i);
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int worst = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            // Max-heap for min-distance (root = largest distance = worst match)
            // Min-heap for max-distance/DotProduct (root = smallest = worst match)
            if (left < _count && ShouldSwap(left, worst)) worst = left;
            if (right < _count && ShouldSwap(right, worst)) worst = right;

            if (worst == i) break;
            (_heap[i], _heap[worst]) = (_heap[worst], _heap[i]);
            i = worst;
        }
    }

    private bool ShouldSwap(int candidate, int current)
    {
        return _isMinHeap
            ? _heap[candidate].Distance > _heap[current].Distance  // max-heap to evict largest
            : _heap[candidate].Distance < _heap[current].Distance; // min-heap to evict smallest
    }
}
