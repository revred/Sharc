// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Codec;

namespace Sharc.Codec;

/// <summary>
/// CBOR encoding/decoding for structured BLOB column payloads.
/// Stores data 30-50% smaller than JSON with zero string allocation on read.
/// Uses a minimal RFC 8949 subset (maps, strings, integers, doubles, bools, null, byte arrays, nested maps).
/// </summary>
public static class SharcCbor
{
    /// <summary>Encode a string-keyed map to CBOR bytes for storage in a BLOB column.</summary>
    public static byte[] Encode(IDictionary<string, object?> map)
        => CborEncoder.Encode(map);

    /// <summary>Decode a CBOR BLOB to a string-keyed dictionary.</summary>
    public static Dictionary<string, object?> Decode(ReadOnlySpan<byte> cbor)
        => CborDecoder.Decode(cbor);

    /// <summary>
    /// Extract a single field from a CBOR BLOB without decoding the entire payload.
    /// Returns null if the key is not found.
    /// </summary>
    public static object? ReadField(ReadOnlySpan<byte> cbor, string fieldName)
        => CborDecoder.ReadField(cbor, fieldName);

    /// <summary>
    /// Extract a single typed field from a CBOR BLOB without decoding the entire payload.
    /// Returns default(T) if the key is not found.
    /// </summary>
    public static T? ReadField<T>(ReadOnlySpan<byte> cbor, string fieldName)
        => CborDecoder.ReadField<T>(cbor, fieldName);

    /// <summary>
    /// Create a <see cref="ColumnValue"/> BLOB from a map, ready for <c>SharcWriter.Insert()</c>.
    /// The serial type is computed as <c>2 * byteLength + 12</c> per SQLite format 3 rules.
    /// </summary>
    public static ColumnValue ToColumnValue(IDictionary<string, object?> map)
    {
        byte[] encoded = CborEncoder.Encode(map);
        long serialType = 2L * encoded.Length + 12;
        return ColumnValue.Blob(serialType, encoded);
    }
}
