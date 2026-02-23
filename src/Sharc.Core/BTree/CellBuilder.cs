// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;

namespace Sharc.Core.BTree;

/// <summary>
/// Builds B-tree cell structures for writing.
/// This is the write-side inverse of <see cref="CellParser"/>.
/// </summary>
internal static class CellBuilder
{
    /// <summary>
    /// Builds a table leaf cell (page type 0x0D).
    /// Format: payload-size varint + rowid varint + inline payload [+ 4-byte overflow page pointer].
    /// </summary>
    /// <param name="rowId">The rowid for this row.</param>
    /// <param name="recordPayload">The full record payload bytes.</param>
    /// <param name="destination">Buffer to write the cell into.</param>
    /// <param name="usablePageSize">Usable page size (PageSize - ReservedBytes).</param>
    /// <returns>Total bytes written to destination.</returns>
    public static int BuildTableLeafCell(long rowId, ReadOnlySpan<byte> recordPayload,
        Span<byte> destination, int usablePageSize)
    {
        int pos = 0;

        // Write payload-size varint
        pos += VarintDecoder.Write(destination, recordPayload.Length);

        // Write rowid varint
        pos += VarintDecoder.Write(destination[pos..], rowId);

        // Determine inline vs overflow
        int inlineSize = CellParser.CalculateInlinePayloadSize(recordPayload.Length, usablePageSize);

        // Write inline payload
        recordPayload[..inlineSize].CopyTo(destination[pos..]);
        pos += inlineSize;

        // If overflow, append a 4-byte overflow page pointer (0 = caller fills later)
        if (inlineSize < recordPayload.Length)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[pos..], 0);
            pos += 4;
        }

        return pos;
    }

    /// <summary>
    /// Builds a table interior cell (page type 0x05).
    /// Format: 4-byte left child page (big-endian) + rowid varint.
    /// </summary>
    /// <param name="leftChildPage">The left child page number.</param>
    /// <param name="rowId">The rowid key.</param>
    /// <param name="destination">Buffer to write the cell into.</param>
    /// <returns>Total bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildTableInteriorCell(uint leftChildPage, long rowId, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, leftChildPage);
        int pos = 4;
        pos += VarintDecoder.Write(destination[pos..], rowId);
        return pos;
    }

    /// <summary>
    /// Pre-computes the total cell size for a table leaf cell without writing it.
    /// </summary>
    /// <param name="rowId">The rowid.</param>
    /// <param name="recordPayloadSize">Total record payload size in bytes.</param>
    /// <param name="usablePageSize">Usable page size.</param>
    /// <returns>Total cell size in bytes.</returns>
    public static int ComputeTableLeafCellSize(long rowId, int recordPayloadSize, int usablePageSize)
    {
        int size = VarintDecoder.GetEncodedLength(recordPayloadSize)
                 + VarintDecoder.GetEncodedLength(rowId);

        int inlineSize = CellParser.CalculateInlinePayloadSize(recordPayloadSize, usablePageSize);
        size += inlineSize;

        if (inlineSize < recordPayloadSize)
            size += 4; // overflow page pointer

        return size;
    }
}
