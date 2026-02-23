// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.BTree;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

public class IndexCellParserTests
{
    /// <summary>
    /// Builds an index leaf cell: [payloadSize varint] [payload bytes]
    /// </summary>
    private static byte[] BuildIndexLeafCell(int payloadSize, byte[]? payload = null)
    {
        var buf = new byte[9 + (payload?.Length ?? payloadSize)]; // max varint = 9
        int offset = VarintDecoder.Write(buf, payloadSize);
        if (payload != null)
            payload.CopyTo(buf, offset);
        return buf;
    }

    /// <summary>
    /// Builds an index interior cell: [4-byte BE left child] [payloadSize varint] [payload bytes]
    /// </summary>
    private static byte[] BuildIndexInteriorCell(uint leftChildPage, int payloadSize, byte[]? payload = null)
    {
        var buf = new byte[13 + (payload?.Length ?? payloadSize)]; // 4 + max 9 varint
        buf[0] = (byte)(leftChildPage >> 24);
        buf[1] = (byte)(leftChildPage >> 16);
        buf[2] = (byte)(leftChildPage >> 8);
        buf[3] = (byte)(leftChildPage & 0xFF);
        int offset = 4 + VarintDecoder.Write(buf.AsSpan(4), payloadSize);
        if (payload != null)
            payload.CopyTo(buf, offset);
        return buf;
    }

    [Fact]
    public void ParseIndexLeafCell_SimpleCell_ReturnsPayloadSize()
    {
        var cell = BuildIndexLeafCell(50);

        int headerLen = IndexCellParser.ParseIndexLeafCell(cell, out int payloadSize);

        Assert.Equal(50, payloadSize);
        Assert.True(headerLen >= 1); // at least 1 byte varint
    }

    [Fact]
    public void ParseIndexLeafCell_LargePayload_ReturnsCorrectSize()
    {
        var cell = BuildIndexLeafCell(5000);

        IndexCellParser.ParseIndexLeafCell(cell, out int payloadSize);

        Assert.Equal(5000, payloadSize);
    }

    [Fact]
    public void ParseIndexLeafCell_ZeroPayload_ReturnsZero()
    {
        var cell = BuildIndexLeafCell(0);

        int headerLen = IndexCellParser.ParseIndexLeafCell(cell, out int payloadSize);

        Assert.Equal(0, payloadSize);
        Assert.Equal(1, headerLen); // single-byte varint for 0
    }

    [Fact]
    public void ParseIndexInteriorCell_ReturnsLeftChildAndPayloadSize()
    {
        var cell = BuildIndexInteriorCell(42, 100);

        IndexCellParser.ParseIndexInteriorCell(cell, out uint leftChild, out int payloadSize);

        Assert.Equal(42u, leftChild);
        Assert.Equal(100, payloadSize);
    }

    [Fact]
    public void ParseIndexInteriorCell_LargePageNumber_ReturnsCorrectly()
    {
        var cell = BuildIndexInteriorCell(100_000, 200);

        IndexCellParser.ParseIndexInteriorCell(cell, out uint leftChild, out int payloadSize);

        Assert.Equal(100_000u, leftChild);
        Assert.Equal(200, payloadSize);
    }

    [Theory]
    [InlineData(50, 4096, 50)]       // Small payload — all inline
    [InlineData(100, 4096, 100)]     // Medium — still inline
    [InlineData(1001, 4096, 1001)]   // Exactly at index threshold — all inline
    public void CalculateIndexInlinePayloadSize_SmallPayload_ReturnsFullSize(
        int payloadSize, int usablePageSize, int expected)
    {
        int result = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, usablePageSize);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateIndexInlinePayloadSize_LargePayload_UsesIndexFormula()
    {
        // For 4096 usable: X = ((4096-12)*64/255)-23 = (4084*64/255)-23 = 1024-23 = 1001
        // M = ((4096-12)*32/255)-23 = 512-23 = 489
        // P=1500: K = 489 + (1500-489)%4092 = 489 + 1011 = 1500; but wait 1500%4092 = 1011
        // K = 489 + 1011 = 1500; 1500 > 1001 => use M = 489
        // Actually: K = 489 + (1500-489)%(4096-4) = 489 + 1011%4092 = 489 + 1011 = 1500
        // 1500 > X=1001, so return M=489
        int payloadSize = 1500;
        int usablePageSize = 4096;

        int result = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, usablePageSize);

        int expectedM = ((4096 - 12) * 32 / 255) - 23; // 489
        Assert.Equal(expectedM, result);
    }

    [Fact]
    public void CalculateIndexInlinePayloadSize_DiffersFromTableFormula()
    {
        // Table threshold: X = 4096 - 35 = 4061
        // Index threshold: X = ((4096-12)*64/255) - 23 = 1001
        // A payload of 2000 is inline for table but overflows for index
        int payloadSize = 2000;
        int usablePageSize = 4096;

        int tableInline = CellParser.CalculateInlinePayloadSize(payloadSize, usablePageSize);
        int indexInline = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, usablePageSize);

        Assert.Equal(2000, tableInline);   // All inline for table (2000 <= 4061)
        Assert.True(indexInline < payloadSize); // Overflows for index (2000 > 1001)
    }

    [Fact]
    public void CalculateIndexInlinePayloadSize_PageSize1024_CorrectThreshold()
    {
        // For 1024 usable:
        // X = ((1024-12)*64/255)-23 = (1012*64/255)-23 = (64768/255)-23 = 254-23 = 231
        // M = ((1024-12)*32/255)-23 = (1012*32/255)-23 = (32384/255)-23 = 127-23 = 104
        int payloadSize = 300;
        int usablePageSize = 1024;

        int result = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, usablePageSize);

        // K = 104 + (300-104)%(1024-4) = 104 + 196%1020 = 104 + 196 = 300
        // 300 > X=231, so return M=104
        // Wait: 300 > 231, so we check K. K = 104 + (300-104)%1020 = 104 + 196 = 300. 300 > 231, use M.
        int expectedM = ((1024 - 12) * 32 / 255) - 23;
        Assert.Equal(expectedM, result);
    }
}
