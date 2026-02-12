/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.BTree;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Inference-based tests for CellParser.
/// Verifies overflow threshold math, zero-payload cells, and boundary conditions
/// derived from the SQLite b-tree cell format specification.
/// </summary>
public class CellParserInferenceTests
{
    // --- Overflow threshold boundary: X = U - 35 ---
    // When payload ≤ X, all bytes are inline.
    // When payload > X: K = M + (P-M)%(U-4); if K ≤ X use K, else use M.
    // M = ((U-12)*32/255) - 23

    [Fact]
    public void CalculateInlinePayloadSize_ExactlyAtThreshold_ReturnsFullSize()
    {
        int U = 4096;
        int X = U - 35; // 4061
        int result = CellParser.CalculateInlinePayloadSize(X, U);
        Assert.Equal(X, result); // All inline
    }

    [Fact]
    public void CalculateInlinePayloadSize_OneOverThreshold_ReturnsM()
    {
        // SQLite btree.c: K = M + (P-M) % (U-4); if K <= X use K, else use M
        // P=X+1=4062: K = 489 + (4062-489) % 4092 = 489 + 3573 = 4062; 4062 > 4061 → M = 489
        int U = 4096;
        int M = ((U - 12) * 32 / 255) - 23; // 489

        int result = CellParser.CalculateInlinePayloadSize(U - 35 + 1, U);
        Assert.Equal(M, result); // K > X, falls back to M
    }

    [Fact]
    public void CalculateInlinePayloadSize_MassivePayload_StillReturnsM()
    {
        // SQLite btree.c: K = M + (P-M) % (U-4); if K <= X use K, else use M
        // P=1_000_000_000: K = 489 + 999999511 % 4092 = 489 + 643 = 1132; 1132 <= 4061 → 1132
        int U = 4096;
        int M = ((U - 12) * 32 / 255) - 23;
        int P = 1_000_000_000;
        int X = U - 35;
        int k = M + (P - M) % (U - 4);
        int expected = k <= X ? k : M;

        int result = CellParser.CalculateInlinePayloadSize(P, U);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateInlinePayloadSize_ZeroPayload_ReturnsZero()
    {
        int result = CellParser.CalculateInlinePayloadSize(0, 4096);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateInlinePayloadSize_OneBytePayload_ReturnsOne()
    {
        int result = CellParser.CalculateInlinePayloadSize(1, 4096);
        Assert.Equal(1, result);
    }

    // --- Different page sizes ---

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(65536)]
    public void CalculateInlinePayloadSize_AtThreshold_AllInline(int pageSize)
    {
        int X = pageSize - 35;
        int result = CellParser.CalculateInlinePayloadSize(X, pageSize);
        Assert.Equal(X, result);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void CalculateInlinePayloadSize_OverThreshold_ReturnsM(int pageSize)
    {
        // SQLite btree.c: K = M + (P-M) % (U-4); if K <= X use K, else use M
        // For P = X+1, K = M + (X+1-M)%(U-4) = X+1; X+1 > X → falls back to M
        int M = ((pageSize - 12) * 32 / 255) - 23;

        int result = CellParser.CalculateInlinePayloadSize(pageSize - 35 + 1, pageSize);
        Assert.Equal(M, result);
    }

    // --- ParseTableLeafCell with zero-length payload ---

    [Fact]
    public void ParseTableLeafCell_ZeroPayload_ReturnsZero()
    {
        // Build cell: [payloadSize=0 varint] [rowId=1 varint]
        var cell = new byte[9];
        int off = VarintDecoder.Write(cell, 0);
        VarintDecoder.Write(cell.AsSpan(off), 1);

        int headerLen = CellParser.ParseTableLeafCell(cell, out int payloadSize, out long rowId);

        Assert.Equal(0, payloadSize);
        Assert.Equal(1L, rowId);
        Assert.True(headerLen >= 2);
    }

    // --- RowId boundaries ---

    [Fact]
    public void ParseTableLeafCell_RowId0_Valid()
    {
        var cell = new byte[18];
        int off = VarintDecoder.Write(cell, 10);
        VarintDecoder.Write(cell.AsSpan(off), 0);

        CellParser.ParseTableLeafCell(cell, out _, out long rowId);
        Assert.Equal(0L, rowId);
    }

    [Fact]
    public void ParseTableLeafCell_MaxRowId_Valid()
    {
        var cell = new byte[18];
        int off = VarintDecoder.Write(cell, 10);
        VarintDecoder.Write(cell.AsSpan(off), long.MaxValue);

        CellParser.ParseTableLeafCell(cell, out _, out long rowId);
        Assert.Equal(long.MaxValue, rowId);
    }

    // --- Overflow page pointer ---

    [Fact]
    public void GetOverflowPage_AtOffset0_ReadsFirst4Bytes()
    {
        byte[] data = [0x00, 0x00, 0x01, 0x00]; // page 256
        Assert.Equal(256u, CellParser.GetOverflowPage(data, 0));
    }

    [Fact]
    public void GetOverflowPage_AtOffset4_ReadsCorrectPosition()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x05];
        Assert.Equal(5u, CellParser.GetOverflowPage(data, 4));
    }

    [Fact]
    public void GetOverflowPage_Zero_MeansNoMorePages()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x00];
        Assert.Equal(0u, CellParser.GetOverflowPage(data, 0));
    }

    // --- ParseTableInteriorCell ---

    [Fact]
    public void ParseTableInteriorCell_LargeValues_PreservesCorrectly()
    {
        // Left child = uint.MaxValue, rowId = long.MaxValue
        var cell = new byte[13];
        cell[0] = 0xFF; cell[1] = 0xFF; cell[2] = 0xFF; cell[3] = 0xFF;
        VarintDecoder.Write(cell.AsSpan(4), long.MaxValue);

        CellParser.ParseTableInteriorCell(cell, out uint leftChild, out long rowId);

        Assert.Equal(uint.MaxValue, leftChild);
        Assert.Equal(long.MaxValue, rowId);
    }
}
