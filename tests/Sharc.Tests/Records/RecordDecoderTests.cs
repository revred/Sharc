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
using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.Records;

public class RecordDecoderTests
{
    private readonly RecordDecoder _decoder = new();

    /// <summary>
    /// Builds a valid SQLite record from serial types and their corresponding body data.
    /// </summary>
    private static byte[] BuildRecord(params (long serialType, byte[] data)[] columns)
    {
        // Write serial type varints to temporary buffer
        var stBuffer = new byte[columns.Length * 9];
        int stLen = 0;
        foreach (var (st, _) in columns)
            stLen += VarintDecoder.Write(stBuffer.AsSpan(stLen), st);

        // Header size = varint length of header_size itself + serial types
        // We need to account for the fact that header_size includes its own varint length
        int headerSizeVarintLen = VarintDecoder.GetEncodedLength(stLen + 1);
        // Check if including the varint length changes the varint length
        int totalHeaderSize = headerSizeVarintLen + stLen;
        if (VarintDecoder.GetEncodedLength(totalHeaderSize) != headerSizeVarintLen)
        {
            headerSizeVarintLen = VarintDecoder.GetEncodedLength(totalHeaderSize + 1);
            totalHeaderSize = headerSizeVarintLen + stLen;
        }

        // Calculate total body size
        int bodySize = 0;
        foreach (var (_, data) in columns)
            bodySize += data.Length;

        var result = new byte[totalHeaderSize + bodySize];

        // Write header size varint
        int offset = VarintDecoder.Write(result, totalHeaderSize);

        // Write serial types
        foreach (var (st, _) in columns)
            offset += VarintDecoder.Write(result.AsSpan(offset), st);

        // Write body data
        foreach (var (_, data) in columns)
        {
            data.CopyTo(result, offset);
            offset += data.Length;
        }

        return result;
    }

    [Fact]
    public void DecodeRecord_NullColumn_ReturnsNull()
    {
        var record = BuildRecord((0, []));

        var columns = _decoder.DecodeRecord(record);

        Assert.Single(columns);
        Assert.True(columns[0].IsNull);
        Assert.Equal(ColumnStorageClass.Null, columns[0].StorageClass);
    }

    [Fact]
    public void DecodeRecord_Int8_CorrectValue()
    {
        var record = BuildRecord((1, [42]));

        var columns = _decoder.DecodeRecord(record);

        Assert.Single(columns);
        Assert.Equal(ColumnStorageClass.Integral, columns[0].StorageClass);
        Assert.Equal(42L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int8_Negative_CorrectValue()
    {
        var record = BuildRecord((1, [0xFE])); // -2 as signed byte

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(-2L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int16_CorrectValue()
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, 1000);
        var record = BuildRecord((2, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(1000L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int24_Positive_CorrectValue()
    {
        // 100000 in 24-bit big-endian: 0x01, 0x86, 0xA0
        byte[] bytes = [0x01, 0x86, 0xA0];
        var record = BuildRecord((3, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(100000L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int24_Negative_SignExtends()
    {
        // -1 in 24-bit: 0xFF, 0xFF, 0xFF
        byte[] bytes = [0xFF, 0xFF, 0xFF];
        var record = BuildRecord((3, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(-1L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int24_NegativeSmall_SignExtends()
    {
        // -100 in 24-bit: 0xFF, 0xFF, 0x9C
        byte[] bytes = [0xFF, 0xFF, 0x9C];
        var record = BuildRecord((3, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(-100L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int32_CorrectValue()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 123456789);
        var record = BuildRecord((4, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(123456789L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int48_CorrectValue()
    {
        // 1099511627775 = 0x000000FFFFFFFFFF (fits in 48 bits, positive)
        long value = 1099511627775L;
        byte[] bytes =
        [
            (byte)(value >> 40), (byte)(value >> 32),
            (byte)(value >> 24), (byte)(value >> 16),
            (byte)(value >> 8), (byte)value
        ];
        var record = BuildRecord((5, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(value, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int48_Negative_SignExtends()
    {
        // -1 in 48-bit: all 0xFF
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        var record = BuildRecord((5, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(-1L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int64_CorrectValue()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, long.MaxValue);
        var record = BuildRecord((6, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(long.MaxValue, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Float64_CorrectValue()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, 3.14159);
        var record = BuildRecord((7, bytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Real, columns[0].StorageClass);
        Assert.Equal(3.14159, columns[0].AsDouble(), 5);
    }

    [Fact]
    public void DecodeRecord_ConstantZero_ReturnsZero()
    {
        var record = BuildRecord((8, []));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Integral, columns[0].StorageClass);
        Assert.Equal(0L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_ConstantOne_ReturnsOne()
    {
        var record = BuildRecord((9, []));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Integral, columns[0].StorageClass);
        Assert.Equal(1L, columns[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_TextColumn_ReturnsUtf8()
    {
        var textBytes = "Hello"u8.ToArray();
        // TEXT serial type for 5 chars: (5*2)+13 = 23
        var record = BuildRecord((23, textBytes));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Text, columns[0].StorageClass);
        Assert.Equal("Hello", columns[0].AsString());
    }

    [Fact]
    public void DecodeRecord_EmptyText_ReturnsEmptyString()
    {
        // Serial type 13 = TEXT of 0 bytes
        var record = BuildRecord((13, []));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Text, columns[0].StorageClass);
        Assert.Equal("", columns[0].AsString());
    }

    [Fact]
    public void DecodeRecord_BlobColumn_ReturnsBytes()
    {
        byte[] blobData = [0xDE, 0xAD, 0xBE, 0xEF];
        // BLOB serial type for 4 bytes: (4*2)+12 = 20
        var record = BuildRecord((20, blobData));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Blob, columns[0].StorageClass);
        Assert.True(columns[0].AsBytes().Span.SequenceEqual(blobData));
    }

    [Fact]
    public void DecodeRecord_EmptyBlob_ReturnsEmptyBytes()
    {
        // Serial type 12 = BLOB of 0 bytes
        var record = BuildRecord((12, []));

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Blob, columns[0].StorageClass);
        Assert.Equal(0, columns[0].AsBytes().Length);
    }

    [Fact]
    public void DecodeRecord_MultipleColumns_AllTypesCorrect()
    {
        var intBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(intBytes, 42);
        var textBytes = "Sharc"u8.ToArray();
        var floatBytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(floatBytes, 2.718);

        // Columns: NULL, INT32(42), TEXT("Sharc"=5 chars, st=23), FLOAT(2.718)
        var record = BuildRecord(
            (0, []),           // NULL
            (4, intBytes),     // INT32
            (23, textBytes),   // TEXT (5 chars â†’ serial type 23)
            (7, floatBytes)    // FLOAT
        );

        var columns = _decoder.DecodeRecord(record);

        Assert.Equal(4, columns.Length);
        Assert.True(columns[0].IsNull);
        Assert.Equal(42L, columns[1].AsInt64());
        Assert.Equal("Sharc", columns[2].AsString());
        Assert.Equal(2.718, columns[3].AsDouble(), 3);
    }

    [Fact]
    public void GetColumnCount_ReturnsCorrect()
    {
        var record = BuildRecord((0, []), (9, []), (13, []));

        var count = _decoder.GetColumnCount(record);

        Assert.Equal(3, count);
    }

    [Fact]
    public void DecodeColumn_SpecificIndex_SkipsOthers()
    {
        var intBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(intBytes, 99);
        var textBytes = "Test"u8.ToArray();

        // Columns: NULL, INT32(99), TEXT("Test")
        var record = BuildRecord(
            (0, []),
            (4, intBytes),
            (21, textBytes) // 4-char text â†’ (4*2)+13 = 21
        );

        var col1 = _decoder.DecodeColumn(record, 1);
        Assert.Equal(99L, col1.AsInt64());

        var col2 = _decoder.DecodeColumn(record, 2);
        Assert.Equal("Test", col2.AsString());
    }

    [Fact]
    public void DecodeColumn_IndexOutOfRange_ThrowsArgumentOutOfRange()
    {
        var record = BuildRecord((0, []), (9, []));

        Assert.Throws<ArgumentOutOfRangeException>(() => _decoder.DecodeColumn(record, 5));
    }

    // --- ReadSerialTypes ---

    [Fact]
    public void ReadSerialTypes_ThreeColumns_ReturnsAllTypes()
    {
        var intBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(intBytes, 42);
        var record = BuildRecord((0, []), (4, intBytes), (9, []));

        var serialTypes = new long[3];
        int count = _decoder.ReadSerialTypes(record, serialTypes);

        Assert.Equal(3, count);
        Assert.Equal(0L, serialTypes[0]);  // NULL
        Assert.Equal(4L, serialTypes[1]);  // INT32
        Assert.Equal(9L, serialTypes[2]);  // Constant 1
    }

    [Fact]
    public void ReadSerialTypes_ShortDestination_ReturnsFullCountButFillsPartially()
    {
        var intBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(intBytes, 42);
        var textBytes = "Hi"u8.ToArray();
        var record = BuildRecord((0, []), (4, intBytes), (17, textBytes));

        // Destination smaller than column count
        var serialTypes = new long[2];
        int count = _decoder.ReadSerialTypes(record, serialTypes);

        Assert.Equal(3, count);          // Reports full count
        Assert.Equal(0L, serialTypes[0]); // Filled
        Assert.Equal(4L, serialTypes[1]); // Filled
        // serialTypes[2] doesn't exist — that's the edge case
    }
}
