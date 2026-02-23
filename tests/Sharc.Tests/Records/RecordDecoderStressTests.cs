// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.Records;

/// <summary>
/// Stress tests for <see cref="RecordDecoder"/> and <see cref="RecordEncoder"/> —
/// roundtrip encoding/decoding with varied types, widths, and column counts.
/// </summary>
public sealed class RecordDecoderStressTests
{
    [Fact]
    public void Roundtrip_AllTypes_PreservesValues()
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.Null(),
            ColumnValue.FromInt64(1, 0),      // 8-bit int
            ColumnValue.FromInt64(2, 255),     // 16-bit int
            ColumnValue.FromInt64(4, 70000),   // 32-bit int
            ColumnValue.FromInt64(6, long.MaxValue), // 64-bit int
            ColumnValue.FromDouble(3.14159),
            ColumnValue.Text(2 * 5 + 13, "hello"u8.ToArray()),
            ColumnValue.Blob(2 * 3 + 12, new byte[] { 1, 2, 3 }),
            ColumnValue.FromInt64(8, 0),       // constant 0
            ColumnValue.FromInt64(9, 1),       // constant 1
        };

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[cols.Length];
        decoder.DecodeRecord(buf, decoded);

        Assert.True(decoded[0].IsNull);
        Assert.Equal(0L, decoded[1].AsInt64());
        Assert.Equal(255L, decoded[2].AsInt64());
        Assert.Equal(70000L, decoded[3].AsInt64());
        Assert.Equal(long.MaxValue, decoded[4].AsInt64());
        Assert.Equal(3.14159, decoded[5].AsDouble(), 5);
        Assert.Equal("hello", decoded[6].AsString());
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded[7].AsBytes().ToArray());
        Assert.Equal(0L, decoded[8].AsInt64());
        Assert.Equal(1L, decoded[9].AsInt64());
    }

    [Fact]
    public void Roundtrip_WideRecord_20Columns()
    {
        var cols = new ColumnValue[20];
        for (int i = 0; i < 20; i++)
        {
            if (i % 3 == 0) cols[i] = ColumnValue.FromInt64(1, i);
            else if (i % 3 == 1) cols[i] = ColumnValue.FromDouble(i * 1.1);
            else cols[i] = ColumnValue.Text(2 * 6 + 13, Encoding.UTF8.GetBytes($"col_{i:D2}"));
        }

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[20];
        decoder.DecodeRecord(buf, decoded);

        for (int i = 0; i < 20; i++)
        {
            if (i % 3 == 0) Assert.Equal(i, decoded[i].AsInt64());
            else if (i % 3 == 1) Assert.Equal(i * 1.1, decoded[i].AsDouble(), 5);
            else Assert.Equal($"col_{i:D2}", decoded[i].AsString());
        }
    }

    [Fact]
    public void ReadSerialTypes_BatchFastPath_MatchesGeneral()
    {
        // All serial types < 0x80 — should hit batch single-byte fast path
        var cols = new ColumnValue[]
        {
            ColumnValue.Null(),                             // serial type 0
            ColumnValue.FromInt64(1, 42),                   // serial type 1 (8-bit)
            ColumnValue.FromInt64(2, 1000),                 // serial type 2 (16-bit)
            ColumnValue.FromInt64(4, 100000),               // serial type 3 (24-bit — encoder optimizes)
            ColumnValue.FromInt64(6, 5000000000L),          // serial type 5 (48-bit: > 2^32)
            ColumnValue.FromDouble(2.718),                  // serial type 7
            ColumnValue.FromInt64(8, 0),                    // serial type 8
            ColumnValue.FromInt64(9, 1),                    // serial type 9
            ColumnValue.Text(2 * 3 + 13, "abc"u8.ToArray()), // serial type 19
        };

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        Span<long> serialTypes = stackalloc long[cols.Length];
        int colCount = decoder.ReadSerialTypes(buf, serialTypes, out int bodyOffset);

        Assert.Equal(cols.Length, colCount);
        Assert.True(bodyOffset > 0);

        // Verify serial types match expected values
        Assert.Equal(0L, serialTypes[0]);  // NULL
        Assert.Equal(1L, serialTypes[1]);  // 8-bit int (42)
        Assert.Equal(2L, serialTypes[2]);  // 16-bit int (1000)
        Assert.Equal(3L, serialTypes[3]);  // 24-bit int (100000 — encoder optimizes from 4 to 3)
        Assert.Equal(5L, serialTypes[4]);  // 48-bit int (5000000000 > 2^32)
        Assert.Equal(7L, serialTypes[5]);  // float64
        Assert.Equal(8L, serialTypes[6]);  // constant 0
        Assert.Equal(9L, serialTypes[7]);  // constant 1
        Assert.Equal(19L, serialTypes[8]); // text 3 bytes
    }

    [Fact]
    public void Roundtrip_EmptyStrings_PreservedCorrectly()
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.Text(13, Array.Empty<byte>()), // empty string: serial type = 13
            ColumnValue.FromInt64(1, 42),
        };

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[2];
        decoder.DecodeRecord(buf, decoded);

        Assert.Equal("", decoded[0].AsString());
        Assert.Equal(42L, decoded[1].AsInt64());
    }

    [Fact]
    public void Roundtrip_LargeText_4KBValue()
    {
        string largeText = new('Z', 4096);
        var textBytes = Encoding.UTF8.GetBytes(largeText);
        var cols = new ColumnValue[]
        {
            ColumnValue.Text(2 * textBytes.Length + 13, textBytes),
        };

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[1];
        decoder.DecodeRecord(buf, decoded);

        Assert.Equal(4096, decoded[0].AsString().Length);
        Assert.Equal(largeText, decoded[0].AsString());
    }

    [Fact]
    public void Roundtrip_ManyNulls_Handled()
    {
        var cols = new ColumnValue[50];
        for (int i = 0; i < 50; i++)
            cols[i] = ColumnValue.Null();

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[50];
        decoder.DecodeRecord(buf, decoded);

        for (int i = 0; i < 50; i++)
            Assert.True(decoded[i].IsNull);
    }

    [Fact]
    public void DecodeInt64At_AllSerialTypes_Correct()
    {
        var decoder = new RecordDecoder();

        // Test each integer serial type via encode/decode roundtrip
        var testCases = new (long serialType, long value)[]
        {
            (1, 42),           // 8-bit
            (2, 1000),         // 16-bit
            (3, 100000),       // 24-bit
            (4, 70000),        // 32-bit
            (5, 8000000000L),  // 48-bit
            (6, long.MaxValue),// 64-bit
            (8, 0),            // constant 0
            (9, 1),            // constant 1
        };

        foreach (var (st, val) in testCases)
        {
            var cols = new[] { ColumnValue.FromInt64(st, val) };
            int size = RecordEncoder.ComputeEncodedSize(cols);
            var buf = new byte[size];
            RecordEncoder.EncodeRecord(cols, buf);

            var decoded = new ColumnValue[1];
            decoder.DecodeRecord(buf, decoded);
            Assert.Equal(val, decoded[0].AsInt64());
        }
    }

    [Fact]
    public void Roundtrip_Utf8Multibyte_PreservedCorrectly()
    {
        string text = "Hello 世界 🌍";
        var textBytes = Encoding.UTF8.GetBytes(text);
        var cols = new ColumnValue[]
        {
            ColumnValue.Text(2 * textBytes.Length + 13, textBytes),
        };

        int size = RecordEncoder.ComputeEncodedSize(cols);
        var buf = new byte[size];
        RecordEncoder.EncodeRecord(cols, buf);

        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[1];
        decoder.DecodeRecord(buf, decoded);
        Assert.Equal(text, decoded[0].AsString());
    }
}
