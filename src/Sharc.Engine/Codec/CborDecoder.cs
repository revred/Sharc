// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace Sharc.Core.Codec;

/// <summary>
/// Minimal CBOR decoder (RFC 8949 subset) with selective field extraction via skip-scan.
/// Supports: maps, text strings, integers (positive and negative), doubles, bools, null, byte strings, nested maps.
/// </summary>
internal static class CborDecoder
{
    // CBOR major types
    private const byte MajorMask = 0xE0;         // top 3 bits
    private const byte AdditionalMask = 0x1F;     // bottom 5 bits
    private const byte MajorUnsignedInt = 0 << 5; // 0x00
    private const byte MajorNegativeInt = 1 << 5; // 0x20
    private const byte MajorByteString = 2 << 5;  // 0x40
    private const byte MajorTextString = 3 << 5;  // 0x60
    private const byte MajorMap = 5 << 5;         // 0xA0
    private const byte MajorSimple = 7 << 5;      // 0xE0

    /// <summary>
    /// Decodes a CBOR byte sequence into a string-keyed dictionary.
    /// </summary>
    public static Dictionary<string, object?> Decode(ReadOnlySpan<byte> cbor)
    {
        if (cbor.IsEmpty)
            throw new ArgumentException("CBOR data is empty.", nameof(cbor));

        int pos = 0;
        var result = ReadMap(cbor, ref pos);
        return result;
    }

    /// <summary>
    /// Extracts a single field from a CBOR-encoded map without decoding the entire payload.
    /// Returns null if the key is not found.
    /// </summary>
    public static object? ReadField(ReadOnlySpan<byte> cbor, string fieldName)
    {
        if (cbor.IsEmpty)
            throw new ArgumentException("CBOR data is empty.", nameof(cbor));

        int pos = 0;
        var (major, count) = ReadHeader(cbor, ref pos);
        if (major != MajorMap)
            throw new InvalidOperationException("CBOR root is not a map.");

        // Pre-encode field name to UTF-8 once
        int maxBytes = Encoding.UTF8.GetMaxByteCount(fieldName.Length);
        Span<byte> fieldNameUtf8 = maxBytes <= 256
            ? stackalloc byte[maxBytes]
            : new byte[maxBytes];
        int actualBytes = Encoding.UTF8.GetBytes(fieldName.AsSpan(), fieldNameUtf8);
        fieldNameUtf8 = fieldNameUtf8[..actualBytes];

        for (int i = 0; i < (int)count; i++)
        {
            if (MatchTextString(cbor, ref pos, fieldNameUtf8))
                return ReadValue(cbor, ref pos);

            // Skip value — the key-optimization
            SkipValue(cbor, ref pos);
        }

        return null; // key not found
    }

    /// <summary>
    /// Typed selective field extraction.
    /// Throws when the field exists but cannot be cast to the requested type.
    /// </summary>
    public static TFieldValue? ReadField<TFieldValue>(ReadOnlySpan<byte> cbor, string fieldName)
    {
        var value = ReadField(cbor, fieldName);
        if (value is null)
            return default;

        if (value is TFieldValue typedValue)
            return typedValue;

        throw new InvalidCastException(
            $"CBOR field '{fieldName}' is '{value.GetType().Name}', expected '{typeof(TFieldValue).Name}'.");
    }

    // ── Map reader ─────────────────────────────────────────────────

    private static Dictionary<string, object?> ReadMap(ReadOnlySpan<byte> cbor, ref int pos)
    {
        var (major, count) = ReadHeader(cbor, ref pos);
        if (major != MajorMap)
            throw new InvalidOperationException($"Expected CBOR map, got major type {major >> 5}.");

        var map = new Dictionary<string, object?>((int)count);
        for (int i = 0; i < (int)count; i++)
        {
            string key = ReadTextString(cbor, ref pos);
            object? value = ReadValue(cbor, ref pos);
            map[key] = value;
        }
        return map;
    }

    // ── Value reader ───────────────────────────────────────────────

    private static object? ReadValue(ReadOnlySpan<byte> cbor, ref int pos)
    {
        if (pos >= cbor.Length)
            throw new InvalidOperationException("Unexpected end of CBOR data.");

        byte initial = cbor[pos];
        byte major = (byte)(initial & MajorMask);

        switch (major)
        {
            case MajorUnsignedInt:
            {
                var (_, arg) = ReadHeader(cbor, ref pos);
                return (long)arg;
            }

            case MajorNegativeInt:
            {
                var (_, arg) = ReadHeader(cbor, ref pos);
                // CBOR negative: value = -1 - arg
                return -1L - (long)arg;
            }

            case MajorByteString:
            {
                var (_, len) = ReadHeader(cbor, ref pos);
                int length = (int)len;
                var bytes = cbor.Slice(pos, length).ToArray();
                pos += length;
                return bytes;
            }

            case MajorTextString:
            {
                return ReadTextString(cbor, ref pos);
            }

            case MajorMap:
            {
                return ReadMap(cbor, ref pos);
            }

            case MajorSimple:
            {
                byte additional = (byte)(initial & AdditionalMask);
                switch (additional)
                {
                    case 20: // false
                        pos++;
                        return false;
                    case 21: // true
                        pos++;
                        return true;
                    case 22: // null
                        pos++;
                        return null;
                    case 27: // double (8 bytes)
                        pos++;
                        double d = BinaryPrimitives.ReadDoubleBigEndian(cbor.Slice(pos));
                        pos += 8;
                        return d;
                    default:
                        throw new NotSupportedException($"CBOR simple value {additional} not supported.");
                }
            }

            default:
                throw new NotSupportedException($"CBOR major type {major >> 5} not supported.");
        }
    }

    // ── Skip-scan ──────────────────────────────────────────────────

    /// <summary>
    /// Advances pos past one complete CBOR value without decoding it.
    /// This is the key to O(1) selective field extraction.
    /// </summary>
    private static void SkipValue(ReadOnlySpan<byte> cbor, ref int pos)
    {
        if (pos >= cbor.Length)
            throw new InvalidOperationException("Unexpected end of CBOR data during skip.");

        byte initial = cbor[pos];
        byte major = (byte)(initial & MajorMask);

        switch (major)
        {
            case MajorUnsignedInt:
            case MajorNegativeInt:
            {
                // Just skip the header (no payload beyond the uint argument)
                ReadHeader(cbor, ref pos);
                break;
            }

            case MajorByteString:
            case MajorTextString:
            {
                var (_, len) = ReadHeader(cbor, ref pos);
                pos += (int)len; // skip past the string/bytes content
                break;
            }

            case MajorMap:
            {
                var (_, count) = ReadHeader(cbor, ref pos);
                for (int i = 0; i < (int)count; i++)
                {
                    SkipValue(cbor, ref pos); // skip key
                    SkipValue(cbor, ref pos); // skip value
                }
                break;
            }

            case MajorSimple:
            {
                byte additional = (byte)(initial & AdditionalMask);
                pos++; // skip initial byte
                if (additional == 27) // double
                    pos += 8;
                break;
            }

            default:
                throw new NotSupportedException($"CBOR skip: major type {major >> 5} not supported.");
        }
    }

    // ── Primitives ─────────────────────────────────────────────────

    /// <summary>
    /// Checks if the next CBOR text string equals the expected UTF-8 bytes without allocating a string.
    /// Advances pos past the text string regardless of match result.
    /// </summary>
    private static bool MatchTextString(ReadOnlySpan<byte> cbor, ref int pos, ReadOnlySpan<byte> expectedUtf8)
    {
        var (major, len) = ReadHeader(cbor, ref pos);
        if (major != MajorTextString)
            throw new InvalidOperationException($"Expected text string, got major type {major >> 5}.");

        int length = (int)len;
        bool match = length == expectedUtf8.Length
            && cbor.Slice(pos, length).SequenceEqual(expectedUtf8);
        pos += length;
        return match;
    }

    private static string ReadTextString(ReadOnlySpan<byte> cbor, ref int pos)
    {
        var (major, len) = ReadHeader(cbor, ref pos);
        if (major != MajorTextString)
            throw new InvalidOperationException($"Expected text string, got major type {major >> 5}.");

        int length = (int)len;
        string s = Encoding.UTF8.GetString(cbor.Slice(pos, length));
        pos += length;
        return s;
    }

    /// <summary>
    /// Reads a CBOR header: extracts the major type and the argument value.
    /// Advances pos past the header bytes.
    /// </summary>
    private static (byte majorType, ulong argument) ReadHeader(ReadOnlySpan<byte> cbor, ref int pos)
    {
        if (pos >= cbor.Length)
            throw new InvalidOperationException("Unexpected end of CBOR data.");

        byte initial = cbor[pos];
        byte major = (byte)(initial & MajorMask);
        byte additional = (byte)(initial & AdditionalMask);
        pos++;

        ulong argument;
        if (additional < 24)
        {
            argument = additional;
        }
        else if (additional == 24)
        {
            argument = cbor[pos];
            pos += 1;
        }
        else if (additional == 25)
        {
            argument = BinaryPrimitives.ReadUInt16BigEndian(cbor.Slice(pos));
            pos += 2;
        }
        else if (additional == 26)
        {
            argument = BinaryPrimitives.ReadUInt32BigEndian(cbor.Slice(pos));
            pos += 4;
        }
        else if (additional == 27)
        {
            argument = BinaryPrimitives.ReadUInt64BigEndian(cbor.Slice(pos));
            pos += 8;
        }
        else
        {
            throw new NotSupportedException($"CBOR additional info {additional} not supported.");
        }

        return (major, argument);
    }
}
