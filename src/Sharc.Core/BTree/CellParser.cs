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
using Sharc.Core.Primitives;

namespace Sharc.Core.BTree;

/// <summary>
/// Parses SQLite b-tree cell structures from raw page bytes.
/// </summary>
internal static class CellParser
{
    /// <summary>
    /// Parses a table leaf cell (page type 0x0D).
    /// </summary>
    /// <param name="cellData">Bytes starting at the cell offset within the page.</param>
    /// <param name="payloadSize">Total payload size in bytes.</param>
    /// <param name="rowId">The rowid of this row.</param>
    /// <returns>Number of header bytes consumed (offset to start of payload).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseTableLeafCell(ReadOnlySpan<byte> cellData, out int payloadSize, out long rowId)
    {
        int offset = VarintDecoder.Read(cellData, out long rawPayloadSize);
        payloadSize = (int)rawPayloadSize;
        offset += VarintDecoder.Read(cellData[offset..], out rowId);
        return offset;
    }

    /// <summary>
    /// Parses a table interior cell (page type 0x05).
    /// </summary>
    /// <param name="cellData">Bytes starting at the cell offset within the page.</param>
    /// <param name="leftChildPage">The left child page number.</param>
    /// <param name="rowId">The rowid key.</param>
    /// <returns>Number of bytes consumed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseTableInteriorCell(ReadOnlySpan<byte> cellData, out uint leftChildPage, out long rowId)
    {
        leftChildPage = BinaryPrimitives.ReadUInt32BigEndian(cellData);
        int offset = 4;
        offset += VarintDecoder.Read(cellData[offset..], out rowId);
        return offset;
    }

    /// <summary>
    /// Calculates the number of inline payload bytes for a table leaf cell.
    /// </summary>
    /// <param name="payloadSize">Total payload size.</param>
    /// <param name="usablePageSize">Usable page size (PageSize - ReservedBytes).</param>
    /// <returns>Number of bytes stored inline on this page.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateInlinePayloadSize(int payloadSize, int usablePageSize)
    {
        int x = usablePageSize - 35;
        if (payloadSize <= x)
            return payloadSize;

        // SQLite btree.c: surplus = M + (P-M) % (U-4)
        // If surplus <= X, use surplus; otherwise fall back to M
        int m = ((usablePageSize - 12) * 32 / 255) - 23;
        int k = m + (payloadSize - m) % (usablePageSize - 4);
        return k <= x ? k : m;
    }

    /// <summary>
    /// Reads the 4-byte big-endian overflow page pointer at the given offset.
    /// </summary>
    /// <param name="data">Data containing the overflow pointer.</param>
    /// <param name="offset">Offset of the 4-byte pointer.</param>
    /// <returns>The overflow page number (0 means no more overflow).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetOverflowPage(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    }
}
