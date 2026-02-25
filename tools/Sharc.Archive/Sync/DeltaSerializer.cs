// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;

namespace Sharc.Archive.Sync;

/// <summary>
/// Binary-framed serializer for ledger deltas.
/// Format: <c>[4-byte count][4-byte len₁][bytes₁][4-byte len₂][bytes₂]...</c>
/// All lengths are little-endian 32-bit integers.
/// </summary>
public static class DeltaSerializer
{
    /// <summary>Serialize a list of delta byte arrays into a single binary payload.</summary>
    public static byte[] Serialize(IReadOnlyList<byte[]> deltas)
    {
        // Calculate total size: 4 (count) + sum(4 + len) per delta
        int totalSize = 4;
        for (int i = 0; i < deltas.Count; i++)
            totalSize += 4 + deltas[i].Length;

        var buffer = new byte[totalSize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), deltas.Count);

        int offset = 4;
        for (int i = 0; i < deltas.Count; i++)
        {
            var delta = deltas[i];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), delta.Length);
            offset += 4;
            delta.CopyTo(buffer, offset);
            offset += delta.Length;
        }

        return buffer;
    }

    /// <summary>Deserialize a binary payload back into individual delta byte arrays.</summary>
    public static List<byte[]> Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            throw new ArgumentException("Payload too short for delta header.");

        int count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
        var deltas = new List<byte[]>(count);

        int offset = 4;
        for (int i = 0; i < count; i++)
        {
            if (offset + 4 > payload.Length)
                throw new ArgumentException($"Truncated delta payload at entry {i}.");

            int len = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;

            if (offset + len > payload.Length)
                throw new ArgumentException($"Delta entry {i} extends past payload end.");

            deltas.Add(payload.Slice(offset, len).ToArray());
            offset += len;
        }

        return deltas;
    }
}
