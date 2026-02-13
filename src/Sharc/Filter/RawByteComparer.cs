/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Sharc;

/// <summary>
/// Compares raw SQLite record body bytes against typed filter values
/// without allocating ColumnValue structs or managed strings.
/// </summary>
internal static class RawByteComparer
{
    /// <summary>
    /// Decodes an integer from raw big-endian bytes based on serial type and compares to a filter value.
    /// Handles all SQLite integer serial types (1-6, 8, 9).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CompareInt64(ReadOnlySpan<byte> data, long serialType, long filterValue)
    {
        long columnValue = DecodeInt64(data, serialType);
        return columnValue.CompareTo(filterValue);
    }

    /// <summary>
    /// Decodes an integer from raw bytes based on serial type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long DecodeInt64(ReadOnlySpan<byte> data, long serialType)
    {
        return serialType switch
        {
            1 => (sbyte)data[0],
            2 => BinaryPrimitives.ReadInt16BigEndian(data),
            3 => DecodeInt24(data),
            4 => BinaryPrimitives.ReadInt32BigEndian(data),
            5 => DecodeInt48(data),
            6 => BinaryPrimitives.ReadInt64BigEndian(data),
            8 => 0L,
            9 => 1L,
            _ => 0L
        };
    }

    /// <summary>
    /// Decodes a double from raw bytes (serial type 7) and compares to a filter value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CompareDouble(ReadOnlySpan<byte> data, double filterValue)
    {
        double columnValue = BinaryPrimitives.ReadDoubleBigEndian(data);
        return columnValue.CompareTo(filterValue);
    }

    /// <summary>
    /// Decodes a double from raw bytes (serial type 7).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double DecodeDouble(ReadOnlySpan<byte> data)
    {
        return BinaryPrimitives.ReadDoubleBigEndian(data);
    }

    /// <summary>
    /// Compares two UTF-8 byte sequences lexicographically (ordinal comparison).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Utf8Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return a.SequenceCompareTo(b);
    }

    /// <summary>
    /// Overload for JIT path (Expression Trees do not support Spans).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Utf8Compare(ReadOnlySpan<byte> a, byte[] b)
    {
        return a.SequenceCompareTo(b);
    }

    /// <summary>
    /// Checks if raw UTF-8 bytes start with the given prefix. No string allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8StartsWith(ReadOnlySpan<byte> columnUtf8, ReadOnlySpan<byte> prefixUtf8)
    {
        return columnUtf8.StartsWith(prefixUtf8);
    }

    /// <summary>
    /// Overload for JIT path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8StartsWith(ReadOnlySpan<byte> columnUtf8, byte[] prefixUtf8)
    {
        return columnUtf8.StartsWith(prefixUtf8);
    }

    /// <summary>
    /// Checks if raw UTF-8 bytes end with the given suffix. No string allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8EndsWith(ReadOnlySpan<byte> columnUtf8, ReadOnlySpan<byte> suffixUtf8)
    {
        return columnUtf8.EndsWith(suffixUtf8);
    }

    /// <summary>
    /// Overload for JIT path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8EndsWith(ReadOnlySpan<byte> columnUtf8, byte[] suffixUtf8)
    {
        return columnUtf8.EndsWith(suffixUtf8);
    }

    /// <summary>
    /// Searches for a UTF-8 substring within raw column bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8Contains(ReadOnlySpan<byte> columnUtf8, ReadOnlySpan<byte> patternUtf8)
    {
        return columnUtf8.IndexOf(patternUtf8) >= 0;
    }

    /// <summary>
    /// Overload for JIT path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Utf8Contains(ReadOnlySpan<byte> columnUtf8, byte[] patternUtf8)
    {
        return columnUtf8.IndexOf(patternUtf8) >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt24(ReadOnlySpan<byte> data)
    {
        int raw = (data[0] << 16) | (data[1] << 8) | data[2];
        // Sign-extend from 24-bit
        if ((raw & 0x800000) != 0)
            raw |= unchecked((int)0xFF000000);
        return raw;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt48(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8)  | data[5];
        // Sign-extend from 48-bit
        if ((raw & 0x800000000000L) != 0)
            raw |= unchecked((long)0xFFFF000000000000L);
        return raw;
    }
}
