// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sharc;

/// <summary>
/// 128-bit FNV-1a hasher with metadata tracking. Zero allocation (ref struct, stack-only).
/// Computes dual FNV-1a hashes (64-bit primary + 32-bit guard from second lane)
/// while accumulating payload byte count and column type tags.
/// Collision probability: P ≈ N²/2⁹⁷ ≈ 10⁻¹⁶ at 6M rows.
/// </summary>
internal ref struct Fnv1aHasher
{
    private ulong _hashLo;
    private ulong _hashHi;
    private int _byteCount;
    private ushort _typeMask;
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    // Second seed: FNV offset XOR a large prime to ensure independence
    private const ulong FnvOffsetBasis2 = 0x6C62272E07BB0142UL;
    private const ulong FnvPrime2 = 0x100000001B3UL;

    public Fnv1aHasher()
    {
        _hashLo = FnvOffsetBasis;
        _hashHi = FnvOffsetBasis2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<byte> data)
    {
        _byteCount += data.Length;
        for (int i = 0; i < data.Length; i++)
        {
            _hashLo ^= data[i]; _hashLo *= FnvPrime;
            _hashHi ^= data[i]; _hashHi *= FnvPrime2;
        }
    }

    /// <summary>
    /// Hashes a string as UTF-8 bytes — matches the cursor path which hashes
    /// raw UTF-8 payload bytes directly from the SQLite record. Required for
    /// correct UNION/EXCEPT/INTERSECT dedup when mixing materialized (QueryValue)
    /// and cursor-backed rows.
    /// </summary>
    public void AppendString(string s)
    {
        // Short strings: stackalloc avoids allocation (128 chars * 3 bytes/char = 384 max)
        if (s.Length <= 128)
        {
            Span<byte> utf8 = stackalloc byte[384];
            int written = System.Text.Encoding.UTF8.GetBytes(s, utf8);
            Append(utf8.Slice(0, written));
        }
        else
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(
                System.Text.Encoding.UTF8.GetMaxByteCount(s.Length));
            int written = System.Text.Encoding.UTF8.GetBytes(s, rented);
            Append(rented.AsSpan(0, written));
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLong(long value)
    {
        _byteCount += 8;
        ulong v = (ulong)value;
        for (int shift = 0; shift < 64; shift += 8)
        {
            ulong b = (v >> shift) & 0xFF;
            _hashLo ^= b; _hashLo *= FnvPrime;
            _hashHi ^= b; _hashHi *= FnvPrime2;
        }
    }

    /// <summary>
    /// Adds a column type tag to the structural signature.
    /// Uses rotating XOR over 16 bits — each column shifts left by (colIndex * 2) mod 16,
    /// so up to 8 columns get distinct bit positions for instant structural rejection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTypeTag(int colIndex, byte typeId)
    {
        int shift = (colIndex * 2) & 0xF; // mod 16
        _typeMask ^= (ushort)(typeId << shift);
    }

    public readonly Fingerprint128 Hash =>
        new(_hashLo, (uint)_hashHi, (ushort)Math.Min(_byteCount, 65535), _typeMask);
}
