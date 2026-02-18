// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Primitives;
using System.Text;
using Xunit;

namespace Sharc.Tests.Records;

public sealed class RecordCodecRigorTests
{
    private readonly RecordDecoder _decoder = new();

    [Theory]
    [InlineData(0, 8)]          // Constant 0
    [InlineData(1, 9)]          // Constant 1
    [InlineData(127, 1)]        // 1-byte signed (max)
    [InlineData(-128, 1)]       // 1-byte signed (min)
    [InlineData(128, 2)]        // 2-byte signed
    [InlineData(32767, 2)]      // 2-byte signed (max)
    [InlineData(-32768, 2)]     // 2-byte signed (min)
    [InlineData(32768, 3)]      // 3-byte signed
    [InlineData(8388607, 3)]    // 3-byte signed (max)
    [InlineData(-8388608, 3)]   // 3-byte signed (min)
    [InlineData(8388608, 4)]    // 4-byte signed
    [InlineData(2147483647, 4)] // 4-byte signed (max)
    [InlineData(-2147483648, 4)]// 4-byte signed (min)
    [InlineData(2147483648L, 5)] // 6-byte signed (SQLite doesn't have 5, it uses 6)
    [InlineData(140737488355327L, 5)] // 6-byte signed (max)
    [InlineData(-140737488355328L, 5)] // 6-byte signed (min)
    [InlineData(140737488355328L, 6)] // 8-byte signed
    [InlineData(long.MaxValue, 6)]    // 8-byte signed (max)
    [InlineData(long.MinValue, 6)]    // 8-byte signed (min)
    public void IntegerBoundaries_RoundTrip(long value, int expectedSerialType)
    {
        var columns = new[] { ColumnValue.FromInt64(0, value) };
        int size = RecordEncoder.ComputeEncodedSize(columns);
        var buffer = new byte[size];
        RecordEncoder.EncodeRecord(columns, buffer);

        // Verify serial type in encoded buffer
        // Record format: [header_size (varint)] [serial_type (varint)] ...
        // For 1 column, header_size is HeaderVarint + 1 SerialTypeVarint = 2
        Assert.Equal(2, buffer[0]);
        Assert.Equal(expectedSerialType, buffer[1]);

        var decoded = _decoder.DecodeRecord(buffer);
        Assert.Single(decoded);
        Assert.Equal(value, decoded[0].AsInt64());
    }

    [Fact]
    public void LargePayloads_RoundTrip()
    {
        var rng = new Random(1337);
        // Test records with 100kb+ of data (should remain in-page or handled by RecordEncoder)
        // Sharc handles larger payloads via overflow, but RecordEncoder just produces the bytes.
        
        var blob = new byte[100_000];
        rng.NextBytes(blob);
        var text = new string('A', 50_000);

        var columns = new[]
        {
            ColumnValue.Blob(0, blob),
            ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes(text))
        };

        int size = RecordEncoder.ComputeEncodedSize(columns);
        var buffer = new byte[size];
        RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer);
        Assert.Equal(2, decoded.Length);
        Assert.Equal(blob, decoded[0].AsBytes().ToArray());
        Assert.Equal(text, decoded[1].AsString());
    }

    [Fact]
    public void Varint_Boundaries()
    {
        // SQLite varints are 1-9 bytes.
        // Test encoder/decoder against extreme varint values if applicable.
        // Record format uses varints for serial types and header size.
        
        // 100 columns forces a larger header_size varint.
        var columns = new ColumnValue[200];
        for (int i = 0; i < 200; i++) columns[i] = ColumnValue.Null();

        int size = RecordEncoder.ComputeEncodedSize(columns);
        var buffer = new byte[size];
        RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer);
        Assert.Equal(200, decoded.Length);
        foreach (var col in decoded) Assert.True(col.IsNull);
    }

    [Fact]
    public void Mixed_Stress_1000Trials()
    {
        var rng = new Random(888);
        var decoder = new RecordDecoder();

        for (int i = 0; i < 1000; i++)
        {
            int colCount = rng.Next(1, 50);
            var columns = new ColumnValue[colCount];
            for (int c = 0; c < colCount; c++)
            {
                columns[c] = rng.Next(6) switch
                {
                    0 => ColumnValue.Null(),
                    1 => ColumnValue.FromInt64(0, rng.NextInt64()),
                    2 => ColumnValue.FromDouble(rng.NextDouble()),
                    3 => ColumnValue.Text(0, Encoding.UTF8.GetBytes(new string('x', rng.Next(0, 100)))),
                    4 => ColumnValue.Blob(0, new byte[rng.Next(0, 100)]),
                    _ => ColumnValue.FromGuid(Guid.NewGuid())
                };
            }

            int size = RecordEncoder.ComputeEncodedSize(columns);
            var buffer = new byte[size];
            RecordEncoder.EncodeRecord(columns, buffer);

            var decoded = decoder.DecodeRecord(buffer);
            Assert.Equal(colCount, decoded.Length);
            // Storage parity check (Guid becomes Blob(16))
            for(int c=0; c<colCount; c++) {
                if (columns[c].StorageClass == ColumnStorageClass.UniqueId) {
                    Assert.Equal(ColumnStorageClass.Blob, decoded[c].StorageClass);
                    Assert.Equal(16, decoded[c].AsBytes().Length);
                } else {
                    Assert.Equal(columns[c].StorageClass, decoded[c].StorageClass);
                }
            }
        }
    }
}
