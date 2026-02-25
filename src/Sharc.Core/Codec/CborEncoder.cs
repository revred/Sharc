// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace Sharc.Core.Codec;

/// <summary>
/// Minimal CBOR encoder (RFC 8949 subset) for structured BLOB column payloads.
/// Supports: maps, text strings, integers (positive and negative), doubles, bools, null, byte strings, nested maps.
/// Stateless, zero external dependencies.
/// </summary>
internal static class CborEncoder
{
    // CBOR major types (high 3 bits of initial byte)
    private const byte MajorUnsignedInt = 0 << 5; // 0x00
    private const byte MajorNegativeInt = 1 << 5; // 0x20
    private const byte MajorByteString = 2 << 5;  // 0x40
    private const byte MajorTextString = 3 << 5;   // 0x60
    private const byte MajorMap = 5 << 5;           // 0xA0
    private const byte SimpleFalse = 0xF4;
    private const byte SimpleTrue = 0xF5;
    private const byte SimpleNull = 0xF6;
    private const byte DoublePrecision = 0xFB;

    /// <summary>
    /// Encodes a string-keyed map to CBOR bytes.
    /// </summary>
    public static byte[] Encode(IDictionary<string, object?> map)
    {
        // Two-pass: measure then write.
        int size = MeasureMap(map);
        var buffer = new byte[size];
        int written = WriteMap(buffer, map);
        if (written != size)
            throw new InvalidOperationException($"CBOR size mismatch: measured {size}, wrote {written}");
        return buffer;
    }

    // ── Measure pass ───────────────────────────────────────────────

    private static int MeasureMap(IDictionary<string, object?> map)
    {
        int size = MeasureUintHeader(map.Count); // map header
        foreach (var kvp in map)
        {
            size += MeasureTextString(kvp.Key);
            size += MeasureValue(kvp.Value);
        }
        return size;
    }

    private static int MeasureValue(object? value)
    {
        return value switch
        {
            null => 1,
            bool => 1,
            string s => MeasureTextString(s),
            long l => l >= 0 ? MeasureUintHeader(l) : MeasureUintHeader(-1 - l),
            int i => i >= 0 ? MeasureUintHeader(i) : MeasureUintHeader(-1 - i),
            double => 9, // 1 byte tag + 8 bytes IEEE 754
            float => 9,  // promoted to double
            byte[] b => MeasureUintHeader(b.Length) + b.Length,
            IDictionary<string, object?> nested => MeasureMap(nested),
            _ => throw new NotSupportedException($"CBOR encode: unsupported type {value.GetType().Name}")
        };
    }

    private static int MeasureTextString(string s)
    {
        int utf8Len = Encoding.UTF8.GetByteCount(s);
        return MeasureUintHeader(utf8Len) + utf8Len;
    }

    private static int MeasureUintHeader(long value)
    {
        if (value < 24) return 1;
        if (value <= 0xFF) return 2;
        if (value <= 0xFFFF) return 3;
        if (value <= 0xFFFF_FFFF) return 5;
        return 9;
    }

    // ── Write pass ─────────────────────────────────────────────────

    private static int WriteMap(Span<byte> dest, IDictionary<string, object?> map)
    {
        int pos = WriteUintHeader(dest, MajorMap, map.Count);
        foreach (var kvp in map)
        {
            pos += WriteTextString(dest.Slice(pos), kvp.Key);
            pos += WriteValue(dest.Slice(pos), kvp.Value);
        }
        return pos;
    }

    private static int WriteValue(Span<byte> dest, object? value)
    {
        switch (value)
        {
            case null:
                dest[0] = SimpleNull;
                return 1;

            case bool b:
                dest[0] = b ? SimpleTrue : SimpleFalse;
                return 1;

            case string s:
                return WriteTextString(dest, s);

            case long l:
                return WriteInt64(dest, l);

            case int i:
                return WriteInt64(dest, i);

            case double d:
                return WriteDouble(dest, d);

            case float f:
                return WriteDouble(dest, f);

            case byte[] bytes:
                return WriteByteString(dest, bytes);

            case IDictionary<string, object?> nested:
                return WriteMap(dest, nested);

            default:
                throw new NotSupportedException($"CBOR encode: unsupported type {value.GetType().Name}");
        }
    }

    private static int WriteTextString(Span<byte> dest, string s)
    {
        int utf8Len = Encoding.UTF8.GetByteCount(s);
        int pos = WriteUintHeader(dest, MajorTextString, utf8Len);
        Encoding.UTF8.GetBytes(s, dest.Slice(pos));
        return pos + utf8Len;
    }

    private static int WriteInt64(Span<byte> dest, long value)
    {
        if (value >= 0)
            return WriteUintHeader(dest, MajorUnsignedInt, value);

        // Negative: major type 1, argument = -1 - value
        return WriteUintHeader(dest, MajorNegativeInt, -1 - value);
    }

    private static int WriteDouble(Span<byte> dest, double value)
    {
        dest[0] = DoublePrecision;
        BinaryPrimitives.WriteDoubleBigEndian(dest.Slice(1), value);
        return 9;
    }

    private static int WriteByteString(Span<byte> dest, ReadOnlySpan<byte> data)
    {
        int pos = WriteUintHeader(dest, MajorByteString, data.Length);
        data.CopyTo(dest.Slice(pos));
        return pos + data.Length;
    }

    /// <summary>
    /// Writes a CBOR uint header: major type (high 3 bits) + argument value.
    /// </summary>
    private static int WriteUintHeader(Span<byte> dest, byte majorType, long value)
    {
        if (value < 24)
        {
            dest[0] = (byte)(majorType | value);
            return 1;
        }
        if (value <= 0xFF)
        {
            dest[0] = (byte)(majorType | 24);
            dest[1] = (byte)value;
            return 2;
        }
        if (value <= 0xFFFF)
        {
            dest[0] = (byte)(majorType | 25);
            BinaryPrimitives.WriteUInt16BigEndian(dest.Slice(1), (ushort)value);
            return 3;
        }
        if (value <= 0xFFFF_FFFF)
        {
            dest[0] = (byte)(majorType | 26);
            BinaryPrimitives.WriteUInt32BigEndian(dest.Slice(1), (uint)value);
            return 5;
        }

        dest[0] = (byte)(majorType | 27);
        BinaryPrimitives.WriteUInt64BigEndian(dest.Slice(1), (ulong)value);
        return 9;
    }
}
