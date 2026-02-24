// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sharc.Query.Execution;

/// <summary>
/// ArrayPool-backed open-addressing hash table with backward-shift deletion.
/// Designed for Tier III destructive probe: after probing, matched entries are
/// removed and the residual set represents unmatched build rows.
/// </summary>
/// <remarks>
/// Uses linear probing with backward-shift deletion to maintain probe chains
/// without tombstones. The table is sized to ~1.5× capacity for acceptable load factor.
/// </remarks>
/// <typeparam name="TKey">The key type. Must implement <see cref="IEquatable{TKey}"/>.</typeparam>
internal sealed class OpenAddressHashTable<TKey> : IDisposable
{
    // Slot layout: parallel arrays for keys, values, and occupancy.
    private TKey[]? _keys;
    private int[]? _values;
    private bool[]? _occupied;
    private readonly int _capacity;
    private int _count;
    private readonly IEqualityComparer<TKey>? _comparer;

    /// <summary>Number of entries currently in the table.</summary>
    public int Count => _count;

    /// <summary>
    /// Creates a new open-address hash table with capacity for at least
    /// <paramref name="expectedCount"/> entries.
    /// </summary>
    public OpenAddressHashTable(int expectedCount, IEqualityComparer<TKey>? comparer = null)
    {
        _comparer = comparer;
        // Size to ~1.5× for ~67% max load factor
        _capacity = Math.Max(NextPowerOfTwo((int)(expectedCount * 1.5)), 16);
        _keys = ArrayPool<TKey>.Shared.Rent(_capacity);
        _values = ArrayPool<int>.Shared.Rent(_capacity);
        _occupied = ArrayPool<bool>.Shared.Rent(_capacity);
        Array.Clear(_occupied, 0, _capacity);
    }

    /// <summary>
    /// Adds a key-value pair to the table.
    /// Supports duplicate keys (multiple entries with the same key).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(TKey key, int value)
    {
        int slot = FindSlot(key, findEmpty: true);
        _keys![slot] = key;
        _values![slot] = value;
        _occupied![slot] = true;
        _count++;
    }

    /// <summary>
    /// Tries to get the first value associated with <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetHash(TKey key) => _comparer != null ? _comparer.GetHashCode(key!) : key!.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool KeyEquals(TKey a, TKey b) => _comparer != null
        ? _comparer.Equals(a, b)
        : EqualityComparer<TKey>.Default.Equals(a, b);

    /// <summary>
    /// Tries to get the first value associated with <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFirst(TKey key, out int value)
    {
        int mask = _capacity - 1;
        int slot = GetHash(key) & mask;

        for (int i = 0; i < _capacity; i++)
        {
            int idx = (slot + i) & mask;
            if (!_occupied![idx])
            {
                value = default;
                return false;
            }
            if (KeyEquals(_keys![idx], key))
            {
                value = _values![idx];
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Collects all values associated with <paramref name="key"/> into <paramref name="results"/>.
    /// </summary>
    public void GetAll(TKey key, List<int> results)
    {
        int mask = _capacity - 1;
        int slot = GetHash(key) & mask;

        for (int i = 0; i < _capacity; i++)
        {
            int idx = (slot + i) & mask;
            if (!_occupied![idx])
                return;
            if (KeyEquals(_keys![idx], key))
                results.Add(_values![idx]);
        }
    }

    /// <summary>
    /// Removes the first entry matching <paramref name="key"/>.
    /// Uses backward-shift deletion to maintain probe chains.
    /// </summary>
    public bool Remove(TKey key)
    {
        int mask = _capacity - 1;
        int slot = GetHash(key) & mask;

        for (int i = 0; i < _capacity; i++)
        {
            int idx = (slot + i) & mask;
            if (!_occupied![idx])
                return false;
            if (KeyEquals(_keys![idx], key))
            {
                BackwardShiftDelete(idx);
                _count--;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes all entries matching <paramref name="key"/> in a single scan.
    /// More efficient than chaining individual <see cref="Remove"/> calls for
    /// keys with many duplicates — avoids restarting the probe chain each time.
    /// </summary>
    public int RemoveAll(TKey key)
    {
        int mask = _capacity - 1;
        int slot = GetHash(key) & mask;
        int removed = 0;

        for (int i = 0; i < _capacity; i++)
        {
            int idx = (slot + i) & mask;
            if (!_occupied![idx])
                break;
            if (KeyEquals(_keys![idx], key))
            {
                BackwardShiftDelete(idx);
                _count--;
                removed++;
                // After backward-shift, the entry at idx may be a shifted entry.
                // Re-check the same position (don't advance i).
                i--;
            }
        }

        return removed;
    }

    /// <summary>
    /// Backward-shift deletion: after removing the entry at <paramref name="emptySlot"/>,
    /// shifts subsequent entries backward to fill the gap and maintain probe chains.
    /// </summary>
    private void BackwardShiftDelete(int emptySlot)
    {
        int mask = _capacity - 1;
        _occupied![emptySlot] = false;

        int gap = emptySlot;
        int next = (gap + 1) & mask;

        while (_occupied[next])
        {
            int home = GetHash(_keys![next]) & mask;

            // The entry at 'next' should shift to 'gap' if its home is NOT
            // between (gap, next] in circular order — i.e., the gap is in
            // the entry's probe chain from home to next.
            if (ShouldShift(gap, next, home, mask))
            {
                _keys[gap] = _keys[next];
                _values![gap] = _values[next];
                _occupied[gap] = true;
                _occupied[next] = false;
                gap = next;
            }

            next = (next + 1) & mask;
        }
    }

    /// <summary>
    /// Determines if an entry at <paramref name="entrySlot"/> with home bucket <paramref name="homeSlot"/>
    /// should be shifted to fill the gap at <paramref name="gapSlot"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldShift(int gapSlot, int entrySlot, int homeSlot, int mask)
    {
        // Standard backward-shift deletion condition for linear probing.
        // The entry at entrySlot (with home bucket homeSlot) should be shifted
        // to gapSlot if gapSlot lies in the probe chain [homeSlot → entrySlot].
        //
        // Equivalently: DON'T shift if homeSlot is strictly between gapSlot and
        // entrySlot in the circular forward direction (the entry is "past" the gap
        // relative to its home). In all other cases, shift.
        //
        // Normalize: dGapToEntry = distance from gap to entry (forward),
        //            dGapToHome  = distance from gap to home (forward).
        // Shift if dGapToHome == 0 (home IS gap) OR dGapToHome > dGapToEntry.
        int dGapToEntry = (entrySlot - gapSlot) & mask;
        int dGapToHome = (homeSlot - gapSlot) & mask;
        return dGapToHome == 0 || dGapToHome > dGapToEntry;
    }

    /// <summary>
    /// Finds a slot for insertion (findEmpty=true) or lookup (findEmpty=false).
    /// </summary>
    /// <summary>
    /// Enumerates all values remaining in the table (residual set after destructive probe).
    /// </summary>
    public IEnumerable<int> EnumerateValues()
    {
        for (int i = 0; i < _capacity; i++)
        {
            if (_occupied![i])
                yield return _values![i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindSlot(TKey key, bool findEmpty)
    {
        int mask = _capacity - 1;
        int slot = GetHash(key) & mask;

        for (int i = 0; i < _capacity; i++)
        {
            int idx = (slot + i) & mask;
            if (!_occupied![idx])
                return idx;
            if (!findEmpty && KeyEquals(_keys![idx], key))
                return idx;
        }

        // Table is full — should not happen with 1.5× sizing
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }

    public void Dispose()
    {
        if (_keys != null)
        {
            ArrayPool<TKey>.Shared.Return(_keys, clearArray: true);
            _keys = null;
        }
        if (_values != null)
        {
            ArrayPool<int>.Shared.Return(_values);
            _values = null;
        }
        if (_occupied != null)
        {
            ArrayPool<bool>.Shared.Return(_occupied, clearArray: true);
            _occupied = null;
        }
    }
}
