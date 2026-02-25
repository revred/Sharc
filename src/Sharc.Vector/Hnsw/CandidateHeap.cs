// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// ArrayPool-backed priority queue for HNSW beam search candidates.
/// Configurable as min-heap or max-heap.
/// </summary>
/// <remarks>
/// Used as:
/// - Min-heap for "W" working set (candidates to explore, pop nearest first)
/// - Max-heap for "R" result set (retain nearest, pop farthest to evict worst)
/// </remarks>
internal struct CandidateHeap : IDisposable
{
    private (float Distance, int NodeIndex)[] _items;
    private int _count;
    private readonly bool _isMinHeap;
    private readonly int _maxCapacity;

    /// <summary>Creates a candidate heap with the given capacity.</summary>
    /// <param name="capacity">Maximum number of elements.</param>
    /// <param name="isMinHeap">True for min-heap (pop smallest first), false for max-heap.</param>
    internal CandidateHeap(int capacity, bool isMinHeap)
    {
        _maxCapacity = capacity;
        _isMinHeap = isMinHeap;
        _items = ArrayPool<(float, int)>.Shared.Rent(capacity);
        _count = 0;
    }

    /// <summary>Number of elements in the heap.</summary>
    internal readonly int Count => _count;

    /// <summary>True if the heap is empty.</summary>
    internal readonly bool IsEmpty => _count == 0;

    /// <summary>Returns the root element without removing it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly (float Distance, int NodeIndex) Peek()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");
        return _items[0];
    }

    /// <summary>Adds an element to the heap.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Push(float distance, int nodeIndex)
    {
        if (_count >= _maxCapacity)
            throw new InvalidOperationException("Heap is full.");

        _items[_count] = (distance, nodeIndex);
        SiftUp(_count);
        _count++;
    }

    /// <summary>Removes and returns the root element.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (float Distance, int NodeIndex) Pop()
    {
        if (_count == 0)
            throw new InvalidOperationException("Heap is empty.");

        var result = _items[0];
        _count--;
        if (_count > 0)
        {
            _items[0] = _items[_count];
            SiftDown(0);
        }
        return result;
    }

    /// <summary>Returns the root distance without removing it or copying the tuple.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly float PeekDistance() => _items[0].Distance;

    /// <summary>Clears the heap without returning the array.</summary>
    internal void Clear() => _count = 0;

    /// <summary>
    /// Drains all elements into a list sorted nearest-first (ascending distance).
    /// The heap is empty after this call.
    /// </summary>
    internal List<(float Distance, int NodeIndex)> DrainToList()
    {
        var list = new List<(float Distance, int NodeIndex)>(_count);
        // Copy unsorted heap items, then sort — O(n log n) but avoids n pops (each O(log n))
        for (int i = 0; i < _count; i++)
            list.Add(_items[i]);
        _count = 0;
        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return list;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (ShouldSwap(i, parent))
            {
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
            else break;
        }
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int target = i;
            int left = (i << 1) + 1;
            int right = (i << 1) + 2;

            if (left < _count && ShouldSwap(left, target)) target = left;
            if (right < _count && ShouldSwap(right, target)) target = right;

            if (target == i) break;
            (_items[i], _items[target]) = (_items[target], _items[i]);
            i = target;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool ShouldSwap(int candidate, int current)
    {
        return _isMinHeap
            ? _items[candidate].Distance < _items[current].Distance
            : _items[candidate].Distance > _items[current].Distance;
    }

    /// <summary>Returns the rented array to the pool.</summary>
    public void Dispose()
    {
        if (_items != null)
        {
            ArrayPool<(float, int)>.Shared.Return(_items);
            _items = null!;
        }
    }
}
