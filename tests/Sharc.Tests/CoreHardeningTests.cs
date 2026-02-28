// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Sharc.Core.Records;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// Defensive tests for Sharc.Core hardening: bounds checks, overflow guards,
/// and corruption detection in the low-level page/record/B-tree layer.
/// </summary>
public sealed class CoreHardeningTests
{
    // ══════════════════════════════════════════════════════════════════
    //  MemoryPageSource — checked page arithmetic
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoryPageSource_GetPage_ValidPage_Succeeds()
    {
        var data = CreateMinimalDb(pageSize: 4096, pageCount: 2);
        var source = new MemoryPageSource(data);
        var page = source.GetPage(1);
        Assert.Equal(4096, page.Length);
    }

    [Fact]
    public void MemoryPageSource_GetPage_PageZero_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDb(pageSize: 4096, pageCount: 2);
        var source = new MemoryPageSource(data);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPage(0));
    }

    [Fact]
    public void MemoryPageSource_GetPage_PageBeyondCount_ThrowsArgumentOutOfRange()
    {
        var data = CreateMinimalDb(pageSize: 4096, pageCount: 2);
        var source = new MemoryPageSource(data);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPage(3));
    }

    [Fact]
    public void MemoryPageSource_WritePage_GrowsBuffer()
    {
        var data = CreateMinimalDb(pageSize: 4096, pageCount: 1);
        var source = new MemoryPageSource(data);
        Assert.Equal(1, source.PageCount);

        var newPage = new byte[4096];
        newPage[0] = 0xAB;
        source.WritePage(2, newPage);

        Assert.Equal(2, source.PageCount);
        var read = source.GetPage(2);
        Assert.Equal(0xAB, read[0]);
    }

    // ══════════════════════════════════════════════════════════════════
    //  BTreePageHeader — Parse hardening
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BTreePageHeader_Parse_TooShortData_ThrowsCorruptPage()
    {
        byte[] tooShort = new byte[4]; // Less than 12 bytes
        Assert.Throws<CorruptPageException>(() => BTreePageHeader.Parse(tooShort));
    }

    [Fact]
    public void BTreePageHeader_Parse_InvalidTypeFlag_ThrowsCorruptPage()
    {
        byte[] data = new byte[12];
        data[0] = 0xFF; // Invalid page type
        Assert.Throws<CorruptPageException>(() => BTreePageHeader.Parse(data));
    }

    [Fact]
    public void BTreePageHeader_Parse_ValidLeaf_Succeeds()
    {
        byte[] data = new byte[12];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 5); // 5 cells
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(BTreePageType.LeafTable, header.PageType);
        Assert.Equal(5, header.CellCount);
    }

    [Fact]
    public void BTreePageHeader_Parse_ValidInterior_ReadsRightChild()
    {
        byte[] data = new byte[12];
        data[0] = 0x05; // InteriorTable
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 42); // Right child = 42
        var header = BTreePageHeader.Parse(data);
        Assert.Equal(42u, header.RightChildPage);
    }

    // ══════════════════════════════════════════════════════════════════
    //  BTreePageHeader.GetCellPointer — cellIndex bounds
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCellPointer_NegativeIndex_ThrowsArgumentOutOfRange()
    {
        byte[] data = new byte[12];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 2); // 2 cells
        var header = BTreePageHeader.Parse(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => header.GetCellPointer(data, -1));
    }

    [Fact]
    public void GetCellPointer_IndexEqualToCellCount_ThrowsArgumentOutOfRange()
    {
        byte[] data = new byte[20];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 2); // 2 cells
        var header = BTreePageHeader.Parse(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => header.GetCellPointer(data, 2));
    }

    [Fact]
    public void GetCellPointer_IndexBeyondCellCount_ThrowsArgumentOutOfRange()
    {
        byte[] data = new byte[20];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 2); // 2 cells
        var header = BTreePageHeader.Parse(data);

        Assert.Throws<ArgumentOutOfRangeException>(() => header.GetCellPointer(data, 100));
    }

    [Fact]
    public void GetCellPointer_ValidIndex_ReturnsCellOffset()
    {
        byte[] data = new byte[20];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 1); // 1 cell
        // Cell pointer array starts at offset 8 (leaf header size)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 0x0100); // Cell at offset 256
        var header = BTreePageHeader.Parse(data);

        ushort cellOff = header.GetCellPointer(data, 0);
        Assert.Equal(0x0100, cellOff);
    }

    // ══════════════════════════════════════════════════════════════════
    //  VarintDecoder — truncated span guards
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void VarintDecoder_Read_EmptySpan_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            VarintDecoder.Read(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void VarintDecoder_Read_SingleByte_Succeeds()
    {
        ReadOnlySpan<byte> data = [0x42];
        int consumed = VarintDecoder.Read(data, out long value);
        Assert.Equal(1, consumed);
        Assert.Equal(0x42, value);
    }

    [Fact]
    public void VarintDecoder_Read_TruncatedMultibyte_DoesNotThrow()
    {
        // 2 bytes with continuation bit set — but span is only 2 bytes
        // This should not throw IndexOutOfRangeException
        ReadOnlySpan<byte> data = [0x81, 0x82]; // Both have continuation
        int consumed = VarintDecoder.Read(data, out long _);
        // Should consume what's available without throwing
        Assert.True(consumed >= 1 && consumed <= 2);
    }

    [Fact]
    public void VarintDecoder_Read_SingleContinuationByte_HandlesGracefully()
    {
        // One byte with continuation bit, nothing follows
        ReadOnlySpan<byte> data = [0x80];
        int consumed = VarintDecoder.Read(data, out long value);
        Assert.Equal(1, consumed);
        Assert.Equal(0L, value);
    }

    [Fact]
    public void VarintDecoder_ReadFromRef_ZeroAvailable_ReturnsZero()
    {
        byte dummy = 0;
        int consumed = VarintDecoder.ReadFromRef(ref dummy, 0, out long value);
        Assert.Equal(0, consumed);
        Assert.Equal(0L, value);
    }

    [Fact]
    public void VarintDecoder_ReadFromRef_NegativeAvailable_ReturnsZero()
    {
        byte dummy = 0;
        int consumed = VarintDecoder.ReadFromRef(ref dummy, -1, out long value);
        Assert.Equal(0, consumed);
        Assert.Equal(0L, value);
    }

    // ══════════════════════════════════════════════════════════════════
    //  SerialTypeCodec — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetContentSize_ReservedType10_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SerialTypeCodec.GetContentSize(10));
    }

    [Fact]
    public void GetContentSize_ReservedType11_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SerialTypeCodec.GetContentSize(11));
    }

    [Fact]
    public void GetContentSize_HugeSerialType_ThrowsArgumentOutOfRange()
    {
        // A serial type so large that (st - 12) / 2 overflows int.MaxValue
        long hugeSerialType = (long)int.MaxValue * 2 + 14; // > int.MaxValue content size
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SerialTypeCodec.GetContentSize(hugeSerialType));
    }

    [Fact]
    public void GetContentSize_NullSerialType_ReturnsZero()
    {
        Assert.Equal(0, SerialTypeCodec.GetContentSize(0));
    }

    [Fact]
    public void GetContentSize_Int8_Returns1()
    {
        Assert.Equal(1, SerialTypeCodec.GetContentSize(1));
    }

    [Fact]
    public void GetContentSize_BlobSerialType12_ReturnsZero()
    {
        Assert.Equal(0, SerialTypeCodec.GetContentSize(12)); // 0-byte blob
    }

    [Fact]
    public void GetContentSize_TextSerialType13_ReturnsZero()
    {
        Assert.Equal(0, SerialTypeCodec.GetContentSize(13)); // 0-byte text (empty string)
    }

    [Fact]
    public void GetContentSize_BlobSerialType14_Returns1()
    {
        Assert.Equal(1, SerialTypeCodec.GetContentSize(14)); // 1-byte blob
    }

    // ══════════════════════════════════════════════════════════════════
    //  RecordDecoder — headerEnd validation
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void RecordDecoder_DecodeRecord_ValidRecord_Succeeds()
    {
        // Minimal record: headerSize=2, one NULL column (serial type 0)
        byte[] payload = [0x02, 0x00];
        var decoder = new RecordDecoder();
        var result = decoder.DecodeRecord(payload);
        Assert.Single(result);
        Assert.Equal(ColumnStorageClass.Null, result[0].StorageClass);
    }

    [Fact]
    public void RecordDecoder_DecodeRecord_HeaderSizeLargerThanPayload_ThrowsArgument()
    {
        // headerSize varint = 100, but payload is only 2 bytes
        byte[] payload = [0x64, 0x00]; // varint 100
        var decoder = new RecordDecoder();
        Assert.Throws<ArgumentException>(() => decoder.DecodeRecord(payload));
    }

    [Fact]
    public void RecordDecoder_GetColumnCount_HeaderSizeLargerThanPayload_ThrowsArgument()
    {
        byte[] payload = [0x64, 0x00]; // varint 100 for headerSize
        var decoder = new RecordDecoder();
        Assert.Throws<ArgumentException>(() => decoder.GetColumnCount(payload));
    }

    [Fact]
    public void RecordDecoder_DecodeBody_OverflowsPayload_ThrowsArgument()
    {
        // Build a record where serial types claim more body bytes than available:
        // headerSize = 3 (header is bytes 0..2), serial type = 6 (8-byte int64)
        // But payload is only 5 bytes total — body starts at byte 3, only 2 bytes available
        byte[] payload = [0x03, 0x06, 0x00, 0xAB, 0xCD];
        var decoder = new RecordDecoder();
        var destination = new ColumnValue[1];
        Assert.Throws<ArgumentException>(() => decoder.DecodeRecord(payload, destination));
    }

    [Fact]
    public void RecordDecoder_DecodeColumn_HeaderSizeTooLarge_ThrowsArgument()
    {
        byte[] payload = [0x64, 0x00]; // headerSize = 100
        var decoder = new RecordDecoder();
        Assert.Throws<ArgumentException>(() => decoder.DecodeColumn(payload, 0));
    }

    [Fact]
    public void RecordDecoder_ReadSerialTypes_HeaderSizeTooLarge_ThrowsArgument()
    {
        byte[] payload = [0x64, 0x00]; // headerSize = 100
        var decoder = new RecordDecoder();
        var serialTypes = new long[16];
        Assert.Throws<ArgumentException>(() =>
            decoder.ReadSerialTypes(payload, serialTypes, out _));
    }

    // ══════════════════════════════════════════════════════════════════
    //  CellParser — payloadSize validation
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CellParser_ParseTableLeafCell_ValidCell_Succeeds()
    {
        // payload size = 5 (varint 0x05), rowid = 1 (varint 0x01)
        byte[] cellData = [0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00];
        int headerSize = CellParser.ParseTableLeafCell(cellData, out int payloadSize, out long rowId);
        Assert.Equal(5, payloadSize);
        Assert.Equal(1, rowId);
        Assert.Equal(2, headerSize); // 1 byte payloadSize varint + 1 byte rowId varint
    }

    [Fact]
    public void CellParser_CalculateInlinePayloadSize_SmallPayload_ReturnsFullPayload()
    {
        int inline = CellParser.CalculateInlinePayloadSize(100, 4096);
        Assert.Equal(100, inline);
    }

    [Fact]
    public void CellParser_CalculateInlinePayloadSize_LargePayload_ReturnsPartial()
    {
        int usable = 4096;
        int inline = CellParser.CalculateInlinePayloadSize(10000, usable);
        Assert.True(inline < 10000);
        Assert.True(inline > 0);
    }

    // ══════════════════════════════════════════════════════════════════
    //  BTreePageHeader.ReadCellPointers
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadCellPointers_ReturnsCorrectPointers()
    {
        byte[] data = new byte[20];
        data[0] = 0x0D; // LeafTable
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), 2); // 2 cells
        // Cell pointer array at offset 8: two 2-byte pointers
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), 0x0200);
        var header = BTreePageHeader.Parse(data);

        var pointers = header.ReadCellPointers(data);
        Assert.Equal(2, pointers.Length);
        Assert.Equal(0x0100, pointers[0]);
        Assert.Equal(0x0200, pointers[1]);
    }

    [Fact]
    public void ReadCellPointers_ZeroCells_ReturnsEmptyArray()
    {
        byte[] data = new byte[12];
        data[0] = 0x0D; // LeafTable, 0 cells
        var header = BTreePageHeader.Parse(data);

        var pointers = header.ReadCellPointers(data);
        Assert.Empty(pointers);
    }

    // ══════════════════════════════════════════════════════════════════
    //  BTreePageHeader.Write roundtrip
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BTreePageHeader_Write_Roundtrip_Leaf()
    {
        var original = new BTreePageHeader(BTreePageType.LeafTable, 0, 10, 1024, 3, 0);
        byte[] buf = new byte[12];
        int written = BTreePageHeader.Write(buf, original);
        Assert.Equal(8, written);

        var parsed = BTreePageHeader.Parse(buf);
        Assert.Equal(BTreePageType.LeafTable, parsed.PageType);
        Assert.Equal(10, parsed.CellCount);
        Assert.Equal(1024, parsed.CellContentOffset);
        Assert.Equal(3, parsed.FragmentedFreeBytes);
    }

    [Fact]
    public void BTreePageHeader_Write_Roundtrip_Interior()
    {
        var original = new BTreePageHeader(BTreePageType.InteriorTable, 0, 5, 2048, 0, 99);
        byte[] buf = new byte[12];
        int written = BTreePageHeader.Write(buf, original);
        Assert.Equal(12, written);

        var parsed = BTreePageHeader.Parse(buf);
        Assert.Equal(BTreePageType.InteriorTable, parsed.PageType);
        Assert.Equal(99u, parsed.RightChildPage);
    }

    // ══════════════════════════════════════════════════════════════════
    //  VarintDecoder.Write roundtrip
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(int.MaxValue)]
    [InlineData(long.MaxValue)]
    public void VarintDecoder_Write_Read_Roundtrip(long value)
    {
        Span<byte> buf = stackalloc byte[9];
        int written = VarintDecoder.Write(buf, value);
        int consumed = VarintDecoder.Read(buf, out long decoded);
        Assert.Equal(written, consumed);
        Assert.Equal(value, decoded);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a minimal valid SQLite database header for MemoryPageSource.
    /// </summary>
    private static byte[] CreateMinimalDb(int pageSize, int pageCount)
    {
        var data = new byte[pageSize * pageCount];

        // Magic string
        "SQLite format 3\0"u8.CopyTo(data);

        // Page size at offset 16 (2 bytes, big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), (ushort)pageSize);

        // File format versions at offset 18-19
        data[18] = 1;
        data[19] = 1;

        // Page count at offset 28 (4 bytes, big-endian)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pageCount);

        // Schema format at offset 44
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);

        // Text encoding at offset 56 (1 = UTF-8)
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(56), 1);

        return data;
    }
}
