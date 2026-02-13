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

using Sharc.Core.Format;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for SQLite b-tree page header parsing.
/// </summary>
public class BTreePageHeaderTests
{
    private static byte[] CreateLeafTableHeader(ushort cellCount = 5, ushort contentOffset = 1000)
    {
        var data = new byte[12];
        data[0] = 0x0D; // Leaf table
        // First freeblock offset (0 = none)
        data[1] = 0; data[2] = 0;
        // Cell count (big-endian)
        data[3] = (byte)(cellCount >> 8);
        data[4] = (byte)(cellCount & 0xFF);
        // Cell content offset (big-endian)
        data[5] = (byte)(contentOffset >> 8);
        data[6] = (byte)(contentOffset & 0xFF);
        // Fragmented free bytes
        data[7] = 0;
        return data;
    }

    private static byte[] CreateInteriorTableHeader(ushort cellCount = 3, uint rightChild = 42)
    {
        var data = new byte[12];
        data[0] = 0x05; // Interior table
        data[1] = 0; data[2] = 0; // freeblock
        data[3] = (byte)(cellCount >> 8);
        data[4] = (byte)(cellCount & 0xFF);
        data[5] = 0x03; data[6] = 0xE8; // content offset = 1000
        data[7] = 0; // fragmented
        // Right child pointer (big-endian 4 bytes)
        data[8] = (byte)(rightChild >> 24);
        data[9] = (byte)(rightChild >> 16);
        data[10] = (byte)(rightChild >> 8);
        data[11] = (byte)(rightChild & 0xFF);
        return data;
    }

    [Fact]
    public void Parse_LeafTablePage_ReturnsCorrectType()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(BTreePageType.LeafTable, header.PageType);
    }

    [Fact]
    public void Parse_LeafTablePage_IsLeafTrue()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.True(header.IsLeaf);
    }

    [Fact]
    public void Parse_LeafTablePage_IsTableTrue()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.True(header.IsTable);
    }

    [Fact]
    public void Parse_LeafTablePage_CorrectCellCount()
    {
        var data = CreateLeafTableHeader(cellCount: 17);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal((ushort)17, header.CellCount);
    }

    [Fact]
    public void Parse_LeafTablePage_HeaderSize8()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(8, header.HeaderSize);
    }

    [Fact]
    public void Parse_InteriorTablePage_ReturnsCorrectType()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(BTreePageType.InteriorTable, header.PageType);
    }

    [Fact]
    public void Parse_InteriorTablePage_IsLeafFalse()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.False(header.IsLeaf);
    }

    [Fact]
    public void Parse_InteriorTablePage_HeaderSize12()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(12, header.HeaderSize);
    }

    [Fact]
    public void Parse_InteriorTablePage_CorrectRightChild()
    {
        var data = CreateInteriorTableHeader(rightChild: 99);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(99u, header.RightChildPage);
    }

    [Fact]
    public void Parse_LeafIndexPage_CorrectType()
    {
        var data = CreateLeafTableHeader();
        data[0] = 0x0A; // Leaf index
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(BTreePageType.LeafIndex, header.PageType);
        Assert.True(header.IsLeaf);
        Assert.False(header.IsTable);
    }

    [Fact]
    public void Parse_InteriorIndexPage_CorrectType()
    {
        var data = CreateInteriorTableHeader();
        data[0] = 0x02; // Interior index
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(BTreePageType.InteriorIndex, header.PageType);
        Assert.False(header.IsLeaf);
        Assert.False(header.IsTable);
    }

    [Fact]
    public void Parse_InvalidPageType_ThrowsCorruptPageException()
    {
        var data = new byte[12];
        data[0] = 0xFF; // Invalid page type
        Assert.Throws<CorruptPageException>(() => BTreePageHeader.Parse(data));
    }

    [Fact]
    public void Parse_CellContentOffset_Zero_Means65536()
    {
        // Per SQLite docs, a zero cell content offset means 65536
        var data = CreateLeafTableHeader(contentOffset: 0);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal((ushort)0, header.CellContentOffset); // Store raw; caller interprets
    }

    [Fact]
    public void ReadCellPointers_ThreeCells_ReturnsThreeOffsets()
    {
        // Build a page with header + 3 cell pointers
        var data = new byte[100];
        data[0] = 0x0D; // leaf table
        data[3] = 0; data[4] = 3; // 3 cells
        data[5] = 0; data[6] = 50; // content offset
        // Cell pointers start at offset 8 (leaf header size)
        // Pointer 1: offset 50
        data[8] = 0; data[9] = 50;
        // Pointer 2: offset 60
        data[10] = 0; data[11] = 60;
        // Pointer 3: offset 70
        data[12] = 0; data[13] = 70;

        var header = BTreePageHeader.Parse(data);
        var pointers = header.ReadCellPointers(data);

        Assert.Equal(3, pointers.Length);
        Assert.Equal((ushort)50, pointers[0]);
        Assert.Equal((ushort)60, pointers[1]);
        Assert.Equal((ushort)70, pointers[2]);
    }

    // --- Write → Parse round-trip ---

    [Fact]
    public void WriteAndParse_LeafTable_RoundTrips()
    {
        var original = BTreePageHeader.Parse(CreateLeafTableHeader(cellCount: 7, contentOffset: 2048));

        var buf = new byte[12];
        BTreePageHeader.Write(buf, original);
        var roundTripped = BTreePageHeader.Parse(buf);

        Assert.Equal(original.PageType, roundTripped.PageType);
        Assert.Equal(original.CellCount, roundTripped.CellCount);
        Assert.Equal(original.CellContentOffset, roundTripped.CellContentOffset);
        Assert.Equal(original.FirstFreeblockOffset, roundTripped.FirstFreeblockOffset);
        Assert.Equal(original.FragmentedFreeBytes, roundTripped.FragmentedFreeBytes);
        Assert.Equal(original.RightChildPage, roundTripped.RightChildPage);
        Assert.True(roundTripped.IsLeaf);
    }

    [Fact]
    public void WriteAndParse_InteriorTable_RoundTrips()
    {
        var original = BTreePageHeader.Parse(CreateInteriorTableHeader(cellCount: 5, rightChild: 12345));

        var buf = new byte[12];
        BTreePageHeader.Write(buf, original);
        var roundTripped = BTreePageHeader.Parse(buf);

        Assert.Equal(original.PageType, roundTripped.PageType);
        Assert.Equal(original.CellCount, roundTripped.CellCount);
        Assert.Equal(original.CellContentOffset, roundTripped.CellContentOffset);
        Assert.Equal(original.RightChildPage, roundTripped.RightChildPage);
        Assert.False(roundTripped.IsLeaf);
        Assert.Equal(12345u, roundTripped.RightChildPage);
    }
}
