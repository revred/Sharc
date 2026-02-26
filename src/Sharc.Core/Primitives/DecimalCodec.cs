// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sharc.Core.Primitives;

/// <summary>
/// Encodes and decodes <see cref="decimal"/> values to a canonical 16-byte payload.
/// Stored as a SQLite BLOB (serial type 44).
/// </summary>
public static class DecimalCodec
{
    /// <summary>Number of bytes used to store a decimal payload.</summary>
    public const int ByteCount = 16;

    /// <summary>SQLite serial type for a 16-byte BLOB.</summary>
    public const long DecimalSerialType = 44;

    /// <summary>
    /// Encodes a decimal to canonical bytes.
    /// Trailing fractional zeros are normalized so numerically equal values map to the same payload.
    /// </summary>
    public static byte[] Encode(decimal value)
    {
        Span<byte> buffer = stackalloc byte[ByteCount];
        Encode(value, buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Encodes a decimal to canonical bytes into the destination span.
    /// </summary>
    public static void Encode(decimal value, Span<byte> destination)
    {
        if (destination.Length < ByteCount)
            throw new ArgumentException("Destination span is too small.", nameof(destination));

        int[] bits = decimal.GetBits(Normalize(value));
        BinaryPrimitives.WriteInt32BigEndian(destination[..4], bits[0]);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(4, 4), bits[1]);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(8, 4), bits[2]);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(12, 4), bits[3]);
    }

    /// <summary>
    /// Decodes a decimal from a 16-byte payload.
    /// </summary>
    public static decimal Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length != ByteCount)
            throw new ArgumentException("Decimal payload must be 16 bytes.", nameof(source));

        int[] bits =
        [
            BinaryPrimitives.ReadInt32BigEndian(source[..4]),
            BinaryPrimitives.ReadInt32BigEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadInt32BigEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadInt32BigEndian(source.Slice(12, 4)),
        ];

        return new decimal(bits);
    }

    /// <summary>
    /// Attempts to decode a decimal from a 16-byte payload.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> source, out decimal value)
    {
        if (source.Length != ByteCount)
        {
            value = default;
            return false;
        }

        try
        {
            value = Decode(source);
            return true;
        }
        catch (ArgumentException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Normalizes a decimal by removing trailing fractional zeros.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Normalize(decimal value)
    {
        if (value == 0m)
            return 0m;

        int[] bits = decimal.GetBits(value);
        int scale = (bits[3] >> 16) & 0x7F;
        if (scale == 0)
            return value;

        BigInteger magnitude = (uint)bits[0];
        magnitude |= (BigInteger)(uint)bits[1] << 32;
        magnitude |= (BigInteger)(uint)bits[2] << 64;

        while (scale > 0)
        {
            BigInteger remainder;
            magnitude = BigInteger.DivRem(magnitude, 10, out remainder);
            if (remainder != 0)
            {
                magnitude = (magnitude * 10) + remainder;
                break;
            }
            scale--;
        }

        bits[0] = (int)(uint)(magnitude & uint.MaxValue);
        bits[1] = (int)(uint)((magnitude >> 32) & uint.MaxValue);
        bits[2] = (int)(uint)((magnitude >> 64) & uint.MaxValue);

        int signFlag = bits[3] & unchecked((int)0x80000000);
        bits[3] = signFlag | (scale << 16);

        return new decimal(bits);
    }
}
