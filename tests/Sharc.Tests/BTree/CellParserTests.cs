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

using Sharc.Core.BTree;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

public class CellParserTests
{
    /// <summary>
    /// Builds a table leaf cell: [payloadSize varint] [rowId varint] [payload bytes]
    /// </summary>
    private static byte[] BuildTableLeafCell(int payloadSize, long rowId, byte[]? payload = null)
    {
        var buf = new byte[18 + (payload?.Length ?? payloadSize)]; // max varint = 9 each
        int offset = VarintDecoder.Write(buf, payloadSize);
        offset += VarintDecoder.Write(buf.AsSpan(offset), rowId);
        if (payload != null)
            payload.CopyTo(buf, offset);
        return buf;
    }

    /// <summary>
    /// Builds a table interior cell: [4-byte BE left child] [rowId varint]
    /// </summary>
    private static byte[] BuildTableInteriorCell(uint leftChildPage, long rowId)
    {
        var buf = new byte[13]; // 4 + max 9 varint
        buf[0] = (byte)(leftChildPage >> 24);
        buf[1] = (byte)(leftChildPage >> 16);
        buf[2] = (byte)(leftChildPage >> 8);
        buf[3] = (byte)(leftChildPage & 0xFF);
        VarintDecoder.Write(buf.AsSpan(4), rowId);
        return buf;
    }

    [Fact]
    public void ParseTableLeafCell_SimpleCell_ReturnsPayloadSizeAndRowId()
    {
        var cell = BuildTableLeafCell(50, 42);

        int headerLen = CellParser.ParseTableLeafCell(cell, out int payloadSize, out long rowId);

        Assert.Equal(50, payloadSize);
        Assert.Equal(42L, rowId);
        Assert.True(headerLen >= 2); // at least 1 byte each varint
    }

    [Fact]
    public void ParseTableLeafCell_LargePayload_ReturnsCorrectSize()
    {
        var cell = BuildTableLeafCell(5000, 1);

        CellParser.ParseTableLeafCell(cell, out int payloadSize, out long rowId);

        Assert.Equal(5000, payloadSize);
        Assert.Equal(1L, rowId);
    }

    [Fact]
    public void ParseTableLeafCell_LargeRowId_ReturnsCorrectValue()
    {
        var cell = BuildTableLeafCell(10, 1_000_000);

        CellParser.ParseTableLeafCell(cell, out int payloadSize, out long rowId);

        Assert.Equal(10, payloadSize);
        Assert.Equal(1_000_000L, rowId);
    }

    [Fact]
    public void ParseTableInteriorCell_ReturnsLeftChildAndRowId()
    {
        var cell = BuildTableInteriorCell(42, 100);

        CellParser.ParseTableInteriorCell(cell, out uint leftChild, out long rowId);

        Assert.Equal(42u, leftChild);
        Assert.Equal(100L, rowId);
    }

    [Fact]
    public void ParseTableInteriorCell_LargePageNumber_ReturnsCorrectly()
    {
        var cell = BuildTableInteriorCell(100_000, 999);

        CellParser.ParseTableInteriorCell(cell, out uint leftChild, out long rowId);

        Assert.Equal(100_000u, leftChild);
        Assert.Equal(999L, rowId);
    }

    [Theory]
    [InlineData(50, 4096, 50)]       // Small payload â€” all inline
    [InlineData(100, 4096, 100)]     // Medium â€” still inline
    [InlineData(4061, 4096, 4061)]   // Exactly at threshold (4096 - 35 = 4061) â€” all inline
    public void CalculateInlinePayloadSize_SmallPayload_ReturnsFullSize(
        int payloadSize, int usablePageSize, int expected)
    {
        int result = CellParser.CalculateInlinePayloadSize(payloadSize, usablePageSize);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateInlinePayloadSize_LargePayload_ReturnsM()
    {
        // For 4096 usable: X = 4061, M = 489
        // SQLite btree.c: K = M + (P-M)%(U-4); if K<=X use K, else use M
        // P=5000: K = 489 + (5000-489)%4092 = 489 + 419 = 908; 908 <= 4061 â†’ 908
        int payloadSize = 5000;
        int usablePageSize = 4096;

        int result = CellParser.CalculateInlinePayloadSize(payloadSize, usablePageSize);

        Assert.Equal(908, result);
    }

    [Fact]
    public void CalculateInlinePayloadSize_PageSize1024_CorrectThreshold()
    {
        // For 1024 usable: X = 989, M = 103
        // K = 103 + (1000-103)%1020 = 103 + 897 = 1000; 1000 > 989 â†’ M = 103
        int payloadSize = 1000;
        int usablePageSize = 1024;

        int result = CellParser.CalculateInlinePayloadSize(payloadSize, usablePageSize);

        int expectedM = ((1024 - 12) * 32 / 255) - 23;
        Assert.Equal(expectedM, result);
    }

    [Fact]
    public void GetOverflowPage_ReadsCorrectValue()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x2A]; // page 42
        Assert.Equal(42u, CellParser.GetOverflowPage(data, 0));
    }

    [Fact]
    public void GetOverflowPage_Zero_MeansNoOverflow()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x00];
        Assert.Equal(0u, CellParser.GetOverflowPage(data, 0));
    }
}
