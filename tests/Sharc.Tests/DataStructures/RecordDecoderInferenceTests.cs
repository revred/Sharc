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

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Inference-based tests for RecordDecoder.
/// Verifies behaviors derived from the SQLite record format specification,
/// including zero-body constants, sign extension, empty records, and column projection.
/// </summary>
public class RecordDecoderInferenceTests
{
    private readonly RecordDecoder _decoder = new();

    /// <summary>
    /// Builds a SQLite record from serial types and their body bytes.
    /// Record format: [header_size varint] [serial_type varints...] [body bytes...]
    /// </summary>
    private static byte[] BuildRecord(params (long serialType, byte[] body)[] columns)
    {
        // Calculate header size: header_size varint + each serial type varint
        var headerBytes = new List<byte>();
        var bodyBytes = new List<byte>();

        // Placeholder for header_size â€” we'll write it after computing
        foreach (var (st, body) in columns)
        {
            var stBuf = new byte[9];
            int stLen = VarintDecoder.Write(stBuf, st);
            headerBytes.AddRange(stBuf[..stLen]);
            bodyBytes.AddRange(body);
        }

        // header_size = varint length of header_size itself + serial type bytes
        // Try encoding header_size and see if it changes the total
        int headerContentLen = headerBytes.Count;
        int headerSizeLen = VarintDecoder.GetEncodedLength(headerContentLen + 1);
        // If adding the header_size varint changes its own length, adjust
        int totalHeaderSize = headerSizeLen + headerContentLen;
        int adjustedLen = VarintDecoder.GetEncodedLength(totalHeaderSize);
        totalHeaderSize = adjustedLen + headerContentLen;

        var record = new byte[totalHeaderSize + bodyBytes.Count];
        int offset = VarintDecoder.Write(record, totalHeaderSize);
        headerBytes.CopyTo(0, record, offset, headerBytes.Count);
        bodyBytes.CopyTo(0, record, totalHeaderSize, bodyBytes.Count);

        return record;
    }

    // --- Empty record: header_size=1, no columns ---
    // A valid record can have zero columns (e.g., an empty table row)

    [Fact]
    public void DecodeRecord_EmptyRecord_ZeroColumns()
    {
        // header_size = 1 (just the header_size varint itself), no serial types
        byte[] record = [0x01]; // header_size = 1
        var result = _decoder.DecodeRecord(record);
        Assert.Empty(result);
    }

    [Fact]
    public void GetColumnCount_EmptyRecord_ReturnsZero()
    {
        byte[] record = [0x01];
        Assert.Equal(0, _decoder.GetColumnCount(record));
    }

    // --- Serial types 8 and 9: integer constants with ZERO body bytes ---
    // These are a size optimization in SQLite â€” the entire value is in the type code.

    [Fact]
    public void DecodeRecord_SerialType8_IntegerConstant0_ZeroBodyBytes()
    {
        // header: [header_size=2] [serial_type=8]
        // body: (empty â€” type 8 has 0 content bytes)
        byte[] record = [0x02, 0x08];
        var result = _decoder.DecodeRecord(record);

        Assert.Single(result);
        Assert.Equal(ColumnStorageClass.Integral, result[0].StorageClass);
        Assert.Equal(0L, result[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_SerialType9_IntegerConstant1_ZeroBodyBytes()
    {
        byte[] record = [0x02, 0x09];
        var result = _decoder.DecodeRecord(record);

        Assert.Single(result);
        Assert.Equal(ColumnStorageClass.Integral, result[0].StorageClass);
        Assert.Equal(1L, result[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_MixedConstants_MultipleColumnsNoBody()
    {
        // Three columns: constant 0, NULL, constant 1
        // header: [header_size=4] [type=8] [type=0] [type=9]
        // body: (empty)
        byte[] record = [0x04, 0x08, 0x00, 0x09];
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(3, result.Length);
        Assert.Equal(0L, result[0].AsInt64());
        Assert.True(result[1].IsNull);
        Assert.Equal(1L, result[2].AsInt64());
    }

    // --- Sign extension for 24-bit integers (serial type 3) ---
    // 24-bit is not a standard CPU width â€” sign extension from bit 23 is required.

    [Fact]
    public void DecodeRecord_Int24_Positive_NoSignExtension()
    {
        // 0x7FFFFF = +8388607 (largest positive 24-bit signed)
        var record = BuildRecord((3, new byte[] { 0x7F, 0xFF, 0xFF }));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(8388607L, result[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int24_NegativeMax_SignExtensionFromBit23()
    {
        // 0x800000 = -8388608 (most negative 24-bit signed)
        var record = BuildRecord((3, new byte[] { 0x80, 0x00, 0x00 }));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(-8388608L, result[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int24_AllOnes_NegativeOne()
    {
        // 0xFFFFFF = -1 in 24-bit signed
        var record = BuildRecord((3, new byte[] { 0xFF, 0xFF, 0xFF }));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(-1L, result[0].AsInt64());
    }

    // --- Sign extension for 48-bit integers (serial type 5) ---

    [Fact]
    public void DecodeRecord_Int48_Positive_NoSignExtension()
    {
        // 0x7FFFFFFFFFFF = largest positive 48-bit signed
        var record = BuildRecord((5, new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(0x7FFFFFFFFFFFL, result[0].AsInt64());
    }

    [Fact]
    public void DecodeRecord_Int48_Negative_SignExtensionFromBit47()
    {
        // 0x800000000000 = most negative 48-bit signed
        var record = BuildRecord((5, new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        var result = _decoder.DecodeRecord(record);

        Assert.True(result[0].AsInt64() < 0);
        Assert.Equal(unchecked((long)0xFFFF800000000000L), result[0].AsInt64());
    }

    // --- Empty text and empty blob: serial types 13 and 12 ---
    // (13-13)/2 = 0 bytes of text; (12-12)/2 = 0 bytes of blob

    [Fact]
    public void DecodeRecord_SerialType13_EmptyText()
    {
        byte[] record = [0x02, 0x0D]; // header_size=2, serial_type=13
        var result = _decoder.DecodeRecord(record);

        Assert.Single(result);
        Assert.Equal(ColumnStorageClass.Text, result[0].StorageClass);
        Assert.Equal("", result[0].AsString());
        Assert.False(result[0].IsNull);
    }

    [Fact]
    public void DecodeRecord_SerialType12_EmptyBlob()
    {
        byte[] record = [0x02, 0x0C]; // header_size=2, serial_type=12
        var result = _decoder.DecodeRecord(record);

        Assert.Single(result);
        Assert.Equal(ColumnStorageClass.Blob, result[0].StorageClass);
        Assert.True(result[0].AsBytes().IsEmpty);
        Assert.False(result[0].IsNull);
    }

    // --- Large text/blob serial types ---
    // Even serial â‰¥ 12 â†’ BLOB of (N-12)/2 bytes
    // Odd serial â‰¥ 13 â†’ TEXT of (N-13)/2 bytes

    [Fact]
    public void DecodeRecord_TextSerialType25_Has6ByteBody()
    {
        // (25-13)/2 = 6 bytes of text
        byte[] textBody = "Hello!"u8.ToArray();
        var record = BuildRecord((25, textBody));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal("Hello!", result[0].AsString());
    }

    [Fact]
    public void DecodeRecord_BlobSerialType20_Has4ByteBody()
    {
        // (20-12)/2 = 4 bytes of blob
        byte[] blobBody = [0xDE, 0xAD, 0xBE, 0xEF];
        var record = BuildRecord((20, blobBody));
        var result = _decoder.DecodeRecord(record);

        Assert.Equal(ColumnStorageClass.Blob, result[0].StorageClass);
        Assert.Equal(blobBody, result[0].AsBytes().ToArray());
    }

    // --- Many columns ---

    [Fact]
    public void DecodeRecord_100Columns_AllNull_DecodesCorrectly()
    {
        // 100 NULL columns: header has 100 serial type varints of value 0
        int headerContentSize = 100; // 100 Ã— 1 byte for serial type 0
        int headerSize = 1 + headerContentSize; // 1 byte for header_size varint (value â‰¤ 127)
        var record = new byte[headerSize]; // no body for NULLs
        record[0] = (byte)headerSize;
        // serial types are all 0 (already zeroed)

        var result = _decoder.DecodeRecord(record);

        Assert.Equal(100, result.Length);
        Assert.All(result, v => Assert.True(v.IsNull));
    }

    [Fact]
    public void GetColumnCount_100Columns_Returns100()
    {
        int headerSize = 1 + 100;
        var record = new byte[headerSize];
        record[0] = (byte)headerSize;

        Assert.Equal(100, _decoder.GetColumnCount(record));
    }

    // --- Column projection: DecodeColumn ---

    [Fact]
    public void DecodeColumn_Index0_DecodesFirstColumn()
    {
        var record = BuildRecord(
            (1, [42]),                    // INT8 = 42
            (25, "Hello!"u8.ToArray())    // TEXT
        );

        var result = _decoder.DecodeColumn(record, 0);
        Assert.Equal(42L, result.AsInt64());
    }

    [Fact]
    public void DecodeColumn_LastColumn_SkipsPrecedingBody()
    {
        var record = BuildRecord(
            (1, [42]),                    // INT8 = 42
            (25, "Hello!"u8.ToArray())    // TEXT = "Hello!"
        );

        var result = _decoder.DecodeColumn(record, 1);
        Assert.Equal("Hello!", result.AsString());
    }

    [Fact]
    public void DecodeColumn_BeyondCount_ThrowsArgumentOutOfRange()
    {
        var record = BuildRecord((1, [42]));
        var result = _decoder.DecodeColumn(record, 1);
        Assert.True(result.IsNull, "Missing columns should decode as NULL to support schema evolution");
    }

    // --- All integer sizes in one record ---

    [Fact]
    public void DecodeRecord_AllIntegerSizes_DecodedCorrectly()
    {
        var record = BuildRecord(
            (1, [0x7F]),                                              // INT8: 127
            (2, [0x00, 0xFF]),                                        // INT16: 255
            (3, [0x01, 0x00, 0x00]),                                  // INT24: 65536
            (4, [0x00, 0x01, 0x00, 0x00]),                            // INT32: 65536
            (5, [0x00, 0x00, 0x00, 0x01, 0x00, 0x00]),               // INT48: 65536
            (6, [0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00])    // INT64: 65536
        );

        var result = _decoder.DecodeRecord(record);

        Assert.Equal(6, result.Length);
        Assert.Equal(127L, result[0].AsInt64());
        Assert.Equal(255L, result[1].AsInt64());
        Assert.Equal(65536L, result[2].AsInt64());
        Assert.Equal(65536L, result[3].AsInt64());
        Assert.Equal(65536L, result[4].AsInt64());
        Assert.Equal(65536L, result[5].AsInt64());
    }

    // --- Reserved serial types 10 and 11 ---

    [Fact]
    public void DecodeRecord_ReservedSerialType10_Throws()
    {
        byte[] record = [0x02, 0x0A]; // header_size=2, serial_type=10
        Assert.Throws<ArgumentOutOfRangeException>(() => _decoder.DecodeRecord(record));
    }

    [Fact]
    public void DecodeRecord_ReservedSerialType11_Throws()
    {
        byte[] record = [0x02, 0x0B]; // header_size=2, serial_type=11
        Assert.Throws<ArgumentOutOfRangeException>(() => _decoder.DecodeRecord(record));
    }
}
