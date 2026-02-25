// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sharc.Query.Execution;

/// <summary>
/// Bit-packed matched-row tracker backed by <see cref="ArrayPool{T}"/>.
/// Uses 1 bit per row (Col&lt;bit&gt;) — for 8,192 rows that's only 1 KB.
/// Tier I (≤256 rows, 32 bytes) fits in L1 cache.
/// Tier II (≤8,192 rows, 1 KB) fits in L2 cache.
/// </summary>
internal struct PooledBitArray : IDisposable
{
    private byte[]? _buffer;
    private readonly int _count;

    private PooledBitArray(byte[] buffer, int count)
    {
        _buffer = buffer;
        _count = count;
    }

    /// <summary>Number of bits in this array.</summary>
    public readonly int Count => _count;

    /// <summary>
    /// Computes the number of bytes needed to store <paramref name="bitCount"/> bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ByteCount(int bitCount) => (bitCount + 7) >> 3;

    /// <summary>
    /// Creates a new pooled bit array with <paramref name="count"/> bits, all initially false.
    /// </summary>
    public static PooledBitArray Create(int count)
    {
        if (count == 0)
            return new PooledBitArray(Array.Empty<byte>(), 0);

        int byteCount = ByteCount(count);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        buffer.AsSpan(0, byteCount).Clear();
        return new PooledBitArray(buffer, count);
    }

    /// <summary>
    /// Sets bit at <paramref name="index"/> to true (matched).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index)
    {
        _buffer![index >> 3] |= (byte)(1 << (index & 7));
    }

    /// <summary>
    /// Sets bit at <paramref name="index"/> to true and returns true only on
    /// a 0 -&gt; 1 transition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySet(int index)
    {
        ref var cell = ref _buffer![index >> 3];
        byte mask = (byte)(1 << (index & 7));
        byte prior = cell;
        if ((prior & mask) != 0)
            return false;

        cell = (byte)(prior | mask);
        return true;
    }

    /// <summary>
    /// Gets the value of the bit at <paramref name="index"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Get(int index)
    {
        return (_buffer![index >> 3] & (1 << (index & 7))) != 0;
    }

    /// <summary>
    /// Returns the rented buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        var buf = _buffer;
        if (buf != null && buf.Length > 0)
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
