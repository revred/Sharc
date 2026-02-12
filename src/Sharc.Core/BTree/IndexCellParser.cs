using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;

namespace Sharc.Core.BTree;

/// <summary>
/// Parses SQLite index b-tree cell structures from raw page bytes.
/// </summary>
internal static class IndexCellParser
{
    /// <summary>
    /// Parses an index leaf cell (page type 0x0A).
    /// </summary>
    /// <param name="cellData">Bytes starting at the cell offset within the page.</param>
    /// <param name="payloadSize">Total payload size in bytes.</param>
    /// <returns>Number of header bytes consumed (offset to start of payload).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseIndexLeafCell(ReadOnlySpan<byte> cellData, out int payloadSize)
    {
        int offset = VarintDecoder.Read(cellData, out long rawPayloadSize);
        payloadSize = (int)rawPayloadSize;
        return offset;
    }

    /// <summary>
    /// Parses an index interior cell (page type 0x02).
    /// </summary>
    /// <param name="cellData">Bytes starting at the cell offset within the page.</param>
    /// <param name="leftChildPage">The left child page number.</param>
    /// <param name="payloadSize">Total payload size in bytes.</param>
    /// <returns>Number of bytes consumed (offset to start of payload).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseIndexInteriorCell(ReadOnlySpan<byte> cellData, out uint leftChildPage, out int payloadSize)
    {
        leftChildPage = BinaryPrimitives.ReadUInt32BigEndian(cellData);
        int offset = 4 + VarintDecoder.Read(cellData[4..], out long rawPayloadSize);
        payloadSize = (int)rawPayloadSize;
        return offset;
    }

    /// <summary>
    /// Calculates the number of inline payload bytes for an index cell.
    /// Uses the index-specific formula: X = ((U-12)*64/255)-23.
    /// </summary>
    /// <param name="payloadSize">Total payload size.</param>
    /// <param name="usablePageSize">Usable page size (PageSize - ReservedBytes).</param>
    /// <returns>Number of bytes stored inline on this page.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateIndexInlinePayloadSize(int payloadSize, int usablePageSize)
    {
        int x = ((usablePageSize - 12) * 64 / 255) - 23;
        if (payloadSize <= x)
            return payloadSize;

        int m = ((usablePageSize - 12) * 32 / 255) - 23;
        int k = m + (payloadSize - m) % (usablePageSize - 4);
        return k <= x ? k : m;
    }
}
