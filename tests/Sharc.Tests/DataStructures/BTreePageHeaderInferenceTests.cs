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

using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Inference-based tests for BTreePageHeader parsing.
/// Verifies page type discrimination, computed properties, and cell pointer contracts.
/// </summary>
public class BTreePageHeaderInferenceTests
{
    private static byte[] MakePageHeader(byte pageType, ushort cellCount = 0,
        ushort cellContentOffset = 0, ushort firstFreeblock = 0, byte fragmented = 0,
        uint rightChild = 0, ushort[]? cellPointers = null)
    {
        bool isInterior = pageType is 0x02 or 0x05;
        int headerSize = isInterior ? 12 : 8;
        int totalSize = headerSize + (cellPointers?.Length ?? 0) * 2;
        var data = new byte[Math.Max(totalSize, 12)];

        data[0] = pageType;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), firstFreeblock);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), cellCount);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(5), cellContentOffset);
        data[7] = fragmented;

        if (isInterior)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), rightChild);

        if (cellPointers != null)
        {
            int offset = headerSize;
            foreach (var ptr in cellPointers)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), ptr);
                offset += 2;
            }
        }

        return data;
    }

    // --- Page type discrimination ---

    [Theory]
    [InlineData(0x0D, true, true)]    // Leaf table
    [InlineData(0x05, false, true)]   // Interior table
    [InlineData(0x0A, true, false)]   // Leaf index
    [InlineData(0x02, false, false)]  // Interior index
    public void Parse_AllPageTypes_CorrectFlags(byte type, bool isLeaf, bool isTable)
    {
        var data = MakePageHeader(type);
        var header = BTreePageHeader.Parse(data);

        Assert.Equal(isLeaf, header.IsLeaf);
        Assert.Equal(isTable, header.IsTable);
    }

    // --- Header sizes ---
    // Leaf pages: 8 bytes (no right-child pointer)
    // Interior pages: 12 bytes (8 base + 4-byte right-child pointer)

    [Theory]
    [InlineData(0x0D, 8)]   // Leaf table
    [InlineData(0x0A, 8)]   // Leaf index
    [InlineData(0x05, 12)]  // Interior table
    [InlineData(0x02, 12)]  // Interior index
    public void Parse_HeaderSize_CorrectForPageType(byte type, int expectedSize)
    {
        var data = MakePageHeader(type);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(expectedSize, header.HeaderSize);
    }

    // --- Cell content offset 0 means 65536 ---
    // SQLite spec: "A zero value for this integer is interpreted as 65536."

    [Fact]
    public void Parse_CellContentOffset0_StoredAsZero()
    {
        // The struct stores the raw value; interpretation of 0→65536 is the caller's job
        var data = MakePageHeader(0x0D, cellContentOffset: 0);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(0, header.CellContentOffset);
    }

    [Fact]
    public void Parse_CellContentOffset4000_StoredExactly()
    {
        var data = MakePageHeader(0x0D, cellContentOffset: 4000);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(4000, header.CellContentOffset);
    }

    // --- Empty page is valid ---

    [Fact]
    public void Parse_CellCount0_ValidEmptyPage()
    {
        var data = MakePageHeader(0x0D, cellCount: 0);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(0, header.CellCount);
    }

    // --- Freeblock offset and fragmented bytes ---

    [Fact]
    public void Parse_FirstFreeblockOffset_Preserved()
    {
        var data = MakePageHeader(0x0D, firstFreeblock: 500);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(500, header.FirstFreeblockOffset);
    }

    [Fact]
    public void Parse_FragmentedFreeBytes_Preserved()
    {
        var data = MakePageHeader(0x0D, fragmented: 23);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(23, header.FragmentedFreeBytes);
    }

    // --- Right child page (interior pages only) ---

    [Fact]
    public void Parse_InteriorTable_RightChildPreserved()
    {
        var data = MakePageHeader(0x05, rightChild: 42);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(42u, header.RightChildPage);
    }

    [Fact]
    public void Parse_LeafTable_RightChildIsZero()
    {
        var data = MakePageHeader(0x0D);
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(0u, header.RightChildPage);
    }

    // --- Invalid page type ---

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x03)]
    [InlineData(0x06)]
    [InlineData(0x0B)]
    [InlineData(0xFF)]
    public void Parse_InvalidPageType_ThrowsCorruptPage(byte badType)
    {
        var data = MakePageHeader(0x0D); // start valid, then corrupt
        data[0] = badType;
        Assert.Throws<CorruptPageException>(() => BTreePageHeader.Parse(data));
    }

    // --- Cell pointer reading ---
    // Cell pointers are 2-byte big-endian offsets from the start of the page.

    [Fact]
    public void ReadCellPointers_ReturnsCorrectOffsets()
    {
        ushort[] ptrs = [100, 200, 300];
        var data = MakePageHeader(0x0D, cellCount: 3, cellPointers: ptrs);
        var header = BTreePageHeader.Parse(data);

        var result = header.ReadCellPointers(data);

        Assert.Equal(3, result.Length);
        Assert.Equal((ushort)100, result[0]);
        Assert.Equal((ushort)200, result[1]);
        Assert.Equal((ushort)300, result[2]);
    }

    [Fact]
    public void ReadCellPointers_EmptyPage_ReturnsEmptyArray()
    {
        var data = MakePageHeader(0x0D, cellCount: 0);
        var header = BTreePageHeader.Parse(data);

        var result = header.ReadCellPointers(data);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadCellPointers_InteriorPage_OffsetsStartAfterRightChild()
    {
        // Interior header: 12 bytes. Cell pointers start at offset 12.
        ushort[] ptrs = [500, 600];
        var data = MakePageHeader(0x05, cellCount: 2, rightChild: 7, cellPointers: ptrs);
        var header = BTreePageHeader.Parse(data);

        var result = header.ReadCellPointers(data);

        Assert.Equal(2, result.Length);
        Assert.Equal((ushort)500, result[0]);
        Assert.Equal((ushort)600, result[1]);
    }
}
