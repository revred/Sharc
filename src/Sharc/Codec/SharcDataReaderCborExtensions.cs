// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Codec;

namespace Sharc.Codec;

/// <summary>
/// Extension methods for reading CBOR-encoded BLOB columns from a <see cref="SharcDataReader"/>.
/// </summary>
public static class SharcDataReaderCborExtensions
{
    /// <summary>
    /// Read a CBOR BLOB column at the given ordinal and decode it into a dictionary.
    /// Uses <see cref="SharcDataReader.GetBlobSpan"/> for zero-copy access to the raw bytes.
    /// </summary>
    public static Dictionary<string, object?> GetCborMap(this SharcDataReader reader, int ordinal)
    {
        ReadOnlySpan<byte> span = reader.GetBlobSpan(ordinal);
        return CborDecoder.Decode(span);
    }

    /// <summary>
    /// Extract a single typed field from a CBOR BLOB column without decoding the entire payload.
    /// Returns default(T) if the key is not found.
    /// </summary>
    public static T? GetCborField<T>(this SharcDataReader reader, int ordinal, string fieldName)
    {
        ReadOnlySpan<byte> span = reader.GetBlobSpan(ordinal);
        return CborDecoder.ReadField<T>(span, fieldName);
    }
}
