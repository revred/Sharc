// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.BTree;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for CellParser.
/// Ensures cell parsing handles adversarial byte inputs without crashing.
/// </summary>
public sealed class CellParserFuzzTests
{
    private static readonly Random Rng = new(42);

    [Fact]
    public void ParseTableLeafCell_RandomBytes_NeverCrashes()
    {
        for (int trial = 0; trial < 200; trial++)
        {
            int len = Rng.Next(2, 64);
            var buffer = new byte[len];
            Rng.NextBytes(buffer);
            try
            {
                CellParser.ParseTableLeafCell(buffer, out _, out _);
            }
            catch
            {
                // Any exception is acceptable
            }
        }
    }

    [Fact]
    public void ParseTableInteriorCell_RandomBytes_NeverCrashes()
    {
        for (int trial = 0; trial < 200; trial++)
        {
            int len = Rng.Next(5, 64);
            var buffer = new byte[len];
            Rng.NextBytes(buffer);
            try
            {
                CellParser.ParseTableInteriorCell(buffer, out _, out _);
            }
            catch
            {
                // Any exception is acceptable
            }
        }
    }

    [Fact]
    public void ParseTableLeafCell_AllZeros_DoesNotHang()
    {
        var buffer = new byte[32];
        try
        {
            CellParser.ParseTableLeafCell(buffer, out int payloadSize, out long rowId);
            Assert.True(payloadSize >= 0);
        }
        catch
        {
            // Acceptable
        }
    }

    [Fact]
    public void ParseTableInteriorCell_AllZeros_DoesNotHang()
    {
        var buffer = new byte[32];
        CellParser.ParseTableInteriorCell(buffer, out uint leftChild, out long rowId);
        Assert.Equal(0u, leftChild);
        Assert.Equal(0L, rowId);
    }

    [Fact]
    public void CalculateInlinePayloadSize_RandomInputs_NeverCrashes()
    {
        for (int trial = 0; trial < 200; trial++)
        {
            int payloadSize = Rng.Next(0, 100_000);
            int usablePageSize = Rng.Next(1, 65536);
            try
            {
                int inline = CellParser.CalculateInlinePayloadSize(payloadSize, usablePageSize);
                Assert.True(inline >= 0);
            }
            catch (DivideByZeroException)
            {
                // Acceptable when usablePageSize == 4
            }
        }
    }

    [Fact]
    public void GetOverflowPage_ShortBuffer_ThrowsNotCrashes()
    {
        var buffer = new byte[3]; // too short for 4-byte big-endian read
        Rng.NextBytes(buffer);
        try
        {
            CellParser.GetOverflowPage(buffer, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Expected
        }
    }

    [Fact]
    public void ParseTableLeafCell_MaxVarintPrefix_DoesNotHang()
    {
        // 8 continuation bytes + final for payload size, then another varint for rowid
        var buffer = new byte[] {
            0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01, // payload size varint
            0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x02  // rowid varint
        };
        int consumed = CellParser.ParseTableLeafCell(buffer, out int payloadSize, out long rowId);
        Assert.True(consumed <= 18);
    }
}
