// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Bitset with dirty-list reset for tracking visited nodes during HNSW search.
/// Uses ArrayPool-backed storage for zero per-search allocation.
/// </summary>
/// <remarks>
/// Reset is O(dirtyCount) instead of O(nodeCount) — at 1M nodes the bitset is 125 KB
/// but reset only touches the words that were actually dirtied (typically ef ~50-200 words).
/// </remarks>
internal struct VisitedSet : IDisposable
{
    private int[] _bits;
    private int[] _dirty;
    private int _dirtyCount;
    private readonly int _capacity;

    /// <summary>Creates a visited set for the given number of nodes.</summary>
    internal VisitedSet(int nodeCount)
    {
        if (nodeCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeCount), nodeCount, "Node count must be positive.");

        _capacity = nodeCount;
        int wordCount = (nodeCount + 31) >> 5; // ceil(nodeCount / 32)
        _bits = ArrayPool<int>.Shared.Rent(wordCount);
        _dirty = ArrayPool<int>.Shared.Rent(wordCount); // worst case: every word dirtied
        _dirtyCount = 0;
        Array.Clear(_bits, 0, wordCount);
    }

    /// <summary>Returns true if the node has been visited.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool IsVisited(int index)
    {
        AssertIndexInRange(index, _capacity);
        int word = index >> 5;
        int bit = 1 << (index & 31);
        return (_bits[word] & bit) != 0;
    }

    /// <summary>Marks the node as visited.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Visit(int index)
    {
        AssertIndexInRange(index, _capacity);
        int word = index >> 5;
        int bit = 1 << (index & 31);
        if ((_bits[word] & bit) == 0)
        {
            if (_bits[word] == 0)
                _dirty[_dirtyCount++] = word;
            _bits[word] |= bit;
        }
    }

    /// <summary>Resets only the dirty words — O(dirtyCount) instead of O(nodeCount).</summary>
    internal void Reset()
    {
        for (int i = 0; i < _dirtyCount; i++)
            _bits[_dirty[i]] = 0;
        _dirtyCount = 0;
    }

    /// <summary>Returns rented arrays to the pool.</summary>
    public void Dispose()
    {
        if (_bits != null)
        {
            ArrayPool<int>.Shared.Return(_bits, clearArray: false);
            _bits = null!;
        }
        if (_dirty != null)
        {
            ArrayPool<int>.Shared.Return(_dirty, clearArray: false);
            _dirty = null!;
        }
    }

    [Conditional("DEBUG")]
    private static void AssertIndexInRange(int index, int capacity)
    {
        if ((uint)index >= (uint)capacity)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Node index is out of range.");
    }
}
