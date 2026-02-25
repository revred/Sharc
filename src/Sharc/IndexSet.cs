// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sharc;

/// <summary>
/// Mode for index-based streaming dedup in set operations.
/// </summary>
internal enum SetDedupMode
{
    /// <summary>UNION: emit row if index not yet seen.</summary>
    Union,
    /// <summary>INTERSECT: emit row if index exists in right set and not yet emitted.</summary>
    Intersect,
    /// <summary>EXCEPT: emit row if index NOT in right set and not yet emitted.</summary>
    Except,
}

/// <summary>
/// Pooled open-addressing index set for Fingerprint128 values.
/// Lo and Hi stored in separate contiguous arrays (distributed, cache-friendly probing).
/// Arrays rented from <see cref="ArrayPool{T}.Shared"/>; instances pooled via ThreadStatic.
/// After warmup: zero managed allocation — both arrays and instances are reused.
/// </summary>
internal sealed class IndexSet : IDisposable
{
    // Per-thread instance pool — avoids class allocation after warmup.
    // 2 slots covers INTERSECT/EXCEPT (rightSet + seenSet).
    [ThreadStatic] private static IndexSet? s_pool1;
    [ThreadStatic] private static IndexSet? s_pool2;

    // Maximum capacity: 16M entries. Beyond this, throw to prevent OOM.
    // At 75% load in a 16M table: 2 arrays × 16M × 8 bytes = 256 MB.
    private const int MaxCapacity = 1 << 24;

    private ulong[]? _lo;   // Fingerprint128.Lo — primary index key (probe)
    private ulong[]? _hi;   // Fingerprint128.Hi — packed guard+meta (verification)
    private int _count;
    private int _mask;       // capacity - 1 (power of 2 for branchless modulo)

    private IndexSet() { }

    /// <summary>
    /// Rents an IndexSet from the thread-local pool, or creates a new one.
    /// Arrays from previous use are preserved (pre-sized) — zero allocation after warmup.
    /// </summary>
    internal static IndexSet Rent()
    {
        var set = s_pool1;
        if (set != null) { s_pool1 = null; return set; }
        set = s_pool2;
        if (set != null) { s_pool2 = null; return set; }
        return new IndexSet();
    }

    // Sentinel handling: (0,0) marks empty slots. FNV-1a's non-zero offset basis
    // makes Lo=0 astronomically unlikely, and Hi=0 requires guard=0 AND
    // payloadLen=0 AND typeTag=0 simultaneously. As a safety net, if a real
    // index entry is (0,0), we flip bit 0 of Lo to distinguish from empty.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EscapeSentinel(ref ulong lo, ref ulong hi)
    {
        if (lo == 0 & hi == 0) { lo = 1; hi = 1; }
    }

    /// <summary>
    /// Adds an entry. Returns true if new, false if already present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Add(in Fingerprint128 fp)
    {
        if (_lo == null || _count >= ((_mask + 1) * 3) >> 2) // 75% load factor
            Grow();

        ulong fpLo = fp.Lo, fpHi = fp.Hi;
        EscapeSentinel(ref fpLo, ref fpHi);

        int slot = (int)(fpLo >> 1) & _mask;
        int capacity = _mask + 1;
        // Bounded probe: at 75% load, average probe ≤ 2.5. Capacity bound
        // is a safety net — prevents infinite loop if table is corrupted.
        for (int probe = 0; probe < capacity; probe++)
        {
            ulong lo = _lo![slot];
            ulong hi = _hi![slot];
            // Bitwise & intentional: both sides are trivial ulong comparisons
            // with no side effects; avoids branch prediction overhead.
            if (lo == 0 & hi == 0)
            {
                _lo[slot] = fpLo;
                _hi[slot] = fpHi;
                _count++;
                return true;
            }
            if (lo == fpLo & hi == fpHi)
                return false;
            slot = (slot + 1) & _mask;
        }
        throw new InvalidOperationException("IndexSet probe sequence exhausted.");
    }

    /// <summary>
    /// Checks if an entry is present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(in Fingerprint128 fp)
    {
        if (_lo == null || _hi == null) return false;

        ulong fpLo = fp.Lo, fpHi = fp.Hi;
        EscapeSentinel(ref fpLo, ref fpHi);

        int slot = (int)(fpLo >> 1) & _mask;
        int capacity = _mask + 1;
        for (int probe = 0; probe < capacity; probe++)
        {
            ulong lo = _lo[slot];
            ulong hi = _hi[slot];
            if (lo == 0 & hi == 0) return false;
            if (lo == fpLo & hi == fpHi) return true;
            slot = (slot + 1) & _mask;
        }
        return false;
    }

    /// <summary>
    /// Removes an entry if present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Remove(in Fingerprint128 fp)
    {
        if (_lo == null || _hi == null) return false;

        ulong fpLo = fp.Lo, fpHi = fp.Hi;
        EscapeSentinel(ref fpLo, ref fpHi);

        int slot = (int)(fpLo >> 1) & _mask;
        int capacity = _mask + 1;
        for (int probe = 0; probe < capacity; probe++)
        {
            ulong lo = _lo[slot];
            ulong hi = _hi[slot];
            if (lo == 0 & hi == 0) return false;
            if (lo == fpLo & hi == fpHi)
            {
                DeleteAndRehashCluster(slot);
                _count--;
                return true;
            }
            slot = (slot + 1) & _mask;
        }
        return false;
    }

    /// <summary>
    /// Deletes the slot and rehashes the subsequent linear-probe cluster.
    /// </summary>
    private void DeleteAndRehashCluster(int deletedSlot)
    {
        _lo![deletedSlot] = 0;
        _hi![deletedSlot] = 0;

        int slot = (deletedSlot + 1) & _mask;
        while (true)
        {
            ulong lo = _lo[slot];
            ulong hi = _hi[slot];
            if (lo == 0 & hi == 0)
                return;

            // Remove entry from current slot, then reinsert at its ideal cluster position.
            _lo[slot] = 0;
            _hi[slot] = 0;

            int reinsert = (int)(lo >> 1) & _mask;
            while (_lo[reinsert] != 0 | _hi[reinsert] != 0)
                reinsert = (reinsert + 1) & _mask;

            _lo[reinsert] = lo;
            _hi[reinsert] = hi;
            slot = (slot + 1) & _mask;
        }
    }

    private void Grow()
    {
        int oldCapacity = _lo != null ? _mask + 1 : 0;
        int newCapacity = oldCapacity == 0 ? 16 : oldCapacity << 1;

        if (newCapacity > MaxCapacity)
            throw new InvalidOperationException(
                $"IndexSet exceeded maximum capacity ({MaxCapacity:N0}).");

        var oldLo = _lo;
        var oldHi = _hi;

        _lo = ArrayPool<ulong>.Shared.Rent(newCapacity);
        _hi = ArrayPool<ulong>.Shared.Rent(newCapacity);
        Array.Clear(_lo, 0, newCapacity);
        Array.Clear(_hi, 0, newCapacity);
        _mask = newCapacity - 1;
        _count = 0;

        // Rehash existing entries into new arrays.
        // New table is at most 37.5% full, so probing always terminates.
        if (oldLo != null)
        {
            int oldMask = oldCapacity - 1;
            for (int i = 0; i <= oldMask; i++)
            {
                ulong lo = oldLo[i];
                ulong hi = oldHi![i];
                if (lo != 0 | hi != 0)
                {
                    int slot = (int)(lo >> 1) & _mask;
                    while (_lo[slot] != 0 | _hi[slot] != 0)
                        slot = (slot + 1) & _mask;
                    _lo[slot] = lo;
                    _hi[slot] = hi;
                    _count++;
                }
            }
            ArrayPool<ulong>.Shared.Return(oldLo);
            ArrayPool<ulong>.Shared.Return(oldHi!);
        }
    }

    /// <summary>
    /// Returns this instance to the thread-local pool for reuse.
    /// Arrays are cleared but kept — next Rent() gets pre-sized arrays.
    /// If pool is full, arrays are returned to ArrayPool.
    /// </summary>
    public void Dispose()
    {
        // Clear data but keep arrays for reuse
        if (_lo != null) Array.Clear(_lo, 0, _mask + 1);
        if (_hi != null) Array.Clear(_hi, 0, _mask + 1);
        _count = 0;

        // Return instance to pool
        if (s_pool1 == null) { s_pool1 = this; return; }
        if (s_pool2 == null) { s_pool2 = this; return; }

        // Pool full — release arrays to ArrayPool
        if (_lo != null) { ArrayPool<ulong>.Shared.Return(_lo); _lo = null; }
        if (_hi != null) { ArrayPool<ulong>.Shared.Return(_hi); _hi = null; }
        _mask = 0;
    }
}
