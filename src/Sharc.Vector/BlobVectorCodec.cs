// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sharc.Vector;

/// <summary>
/// Encodes and decodes float vectors to/from SQLite BLOB format.
/// Layout: IEEE 754 little-endian, 4 bytes per dimension.
/// A 384-dim vector = 1,536 byte BLOB.
/// </summary>
public static class BlobVectorCodec
{
    /// <summary>Decodes a BLOB span into a float span (zero-copy reinterpret).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<float> Decode(ReadOnlySpan<byte> blob)
        => MemoryMarshal.Cast<byte, float>(blob);

    /// <summary>Encodes a float array into a BLOB byte array for storage.</summary>
    public static byte[] Encode(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector).CopyTo(bytes);
        return bytes;
    }

    /// <summary>Encodes into a caller-provided buffer (zero-alloc).</summary>
    public static int Encode(ReadOnlySpan<float> vector, Span<byte> destination)
    {
        var source = MemoryMarshal.AsBytes(vector);
        source.CopyTo(destination);
        return source.Length;
    }

    /// <summary>Returns the dimensionality of a vector stored in the given BLOB.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDimensions(int blobByteLength) => blobByteLength / sizeof(float);
}
