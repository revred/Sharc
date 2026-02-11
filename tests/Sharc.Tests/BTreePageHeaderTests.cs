using FluentAssertions;
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
        header.PageType.Should().Be(BTreePageType.LeafTable);
    }

    [Fact]
    public void Parse_LeafTablePage_IsLeafTrue()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.IsLeaf.Should().BeTrue();
    }

    [Fact]
    public void Parse_LeafTablePage_IsTableTrue()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.IsTable.Should().BeTrue();
    }

    [Fact]
    public void Parse_LeafTablePage_CorrectCellCount()
    {
        var data = CreateLeafTableHeader(cellCount: 17);
        var header = BTreePageHeader.Parse(data);
        header.CellCount.Should().Be(17);
    }

    [Fact]
    public void Parse_LeafTablePage_HeaderSize8()
    {
        var data = CreateLeafTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.HeaderSize.Should().Be(8);
    }

    [Fact]
    public void Parse_InteriorTablePage_ReturnsCorrectType()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.PageType.Should().Be(BTreePageType.InteriorTable);
    }

    [Fact]
    public void Parse_InteriorTablePage_IsLeafFalse()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.IsLeaf.Should().BeFalse();
    }

    [Fact]
    public void Parse_InteriorTablePage_HeaderSize12()
    {
        var data = CreateInteriorTableHeader();
        var header = BTreePageHeader.Parse(data);
        header.HeaderSize.Should().Be(12);
    }

    [Fact]
    public void Parse_InteriorTablePage_CorrectRightChild()
    {
        var data = CreateInteriorTableHeader(rightChild: 99);
        var header = BTreePageHeader.Parse(data);
        header.RightChildPage.Should().Be(99);
    }

    [Fact]
    public void Parse_LeafIndexPage_CorrectType()
    {
        var data = CreateLeafTableHeader();
        data[0] = 0x0A; // Leaf index
        var header = BTreePageHeader.Parse(data);
        header.PageType.Should().Be(BTreePageType.LeafIndex);
        header.IsLeaf.Should().BeTrue();
        header.IsTable.Should().BeFalse();
    }

    [Fact]
    public void Parse_InteriorIndexPage_CorrectType()
    {
        var data = CreateInteriorTableHeader();
        data[0] = 0x02; // Interior index
        var header = BTreePageHeader.Parse(data);
        header.PageType.Should().Be(BTreePageType.InteriorIndex);
        header.IsLeaf.Should().BeFalse();
        header.IsTable.Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidPageType_ThrowsCorruptPageException()
    {
        var data = new byte[12];
        data[0] = 0xFF; // Invalid page type
        var act = () => BTreePageHeader.Parse(data);
        act.Should().Throw<CorruptPageException>();
    }

    [Fact]
    public void Parse_CellContentOffset_Zero_Means65536()
    {
        // Per SQLite docs, a zero cell content offset means 65536
        var data = CreateLeafTableHeader(contentOffset: 0);
        var header = BTreePageHeader.Parse(data);
        header.CellContentOffset.Should().Be(0); // Store raw; caller interprets
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

        pointers.Should().HaveCount(3);
        pointers[0].Should().Be(50);
        pointers[1].Should().Be(60);
        pointers[2].Should().Be(70);
    }
}
