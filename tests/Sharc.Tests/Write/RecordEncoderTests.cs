/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for RecordEncoder — the write-side inverse of RecordDecoder.
/// </summary>
public class RecordEncoderTests
{
    private readonly RecordDecoder _decoder = new();

    // ── Single column: NULL ──

    [Fact]
    public void EncodeRecord_SingleNull_ProducesValidRecord()
    {
        var columns = new[] { ColumnValue.Null() };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        // Header: header-size varint (2) + serial type 0 varint (1 byte) = 2 bytes header
        // Body: 0 bytes (NULL has no body)
        Assert.Equal(2, written);

        // Round-trip through decoder
        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.True(decoded[0].IsNull);
    }

    // ── Single column: Integer ──

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(-128)]
    [InlineData(32767)]
    [InlineData(2147483647)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void EncodeRecord_SingleInt_RoundTripsCorrectly(long intValue)
    {
        var columns = new[] { ColumnValue.FromInt64(0, intValue) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Integral, decoded[0].StorageClass);
        Assert.Equal(intValue, decoded[0].AsInt64());
    }

    // ── Single column: Double ──

    [Theory]
    [InlineData(3.14159)]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    [InlineData(double.MaxValue)]
    [InlineData(double.NaN)]
    public void EncodeRecord_SingleDouble_RoundTripsCorrectly(double value)
    {
        var columns = new[] { ColumnValue.FromDouble(value) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Real, decoded[0].StorageClass);
        if (double.IsNaN(value))
            Assert.True(double.IsNaN(decoded[0].AsDouble()));
        else
            Assert.Equal(value, decoded[0].AsDouble());
    }

    // ── Single column: Text ──

    [Fact]
    public void EncodeRecord_EmptyText_RoundTripsCorrectly()
    {
        var columns = new[] { ColumnValue.Text(13, ReadOnlyMemory<byte>.Empty) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Text, decoded[0].StorageClass);
        Assert.Equal(0, decoded[0].AsBytes().Length);
    }

    [Fact]
    public void EncodeRecord_ShortText_RoundTripsCorrectly()
    {
        byte[] utf8 = "Hello"u8.ToArray();
        var columns = new[] { ColumnValue.Text(0, utf8) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Text, decoded[0].StorageClass);
        Assert.Equal("Hello", decoded[0].AsString());
    }

    // ── Single column: Blob ──

    [Fact]
    public void EncodeRecord_EmptyBlob_RoundTripsCorrectly()
    {
        var columns = new[] { ColumnValue.Blob(12, ReadOnlyMemory<byte>.Empty) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Blob, decoded[0].StorageClass);
        Assert.Equal(0, decoded[0].AsBytes().Length);
    }

    [Fact]
    public void EncodeRecord_ShortBlob_RoundTripsCorrectly()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        var columns = new[] { ColumnValue.Blob(0, data) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Blob, decoded[0].StorageClass);
        Assert.Equal(data, decoded[0].AsBytes().ToArray());
    }

    // ── Multi-column: mixed types ──

    [Fact]
    public void EncodeRecord_MixedTypes_RoundTripsCorrectly()
    {
        // int + text + NULL + double
        byte[] utf8 = "Sharc"u8.ToArray();
        var columns = new[]
        {
            ColumnValue.FromInt64(0, 42),
            ColumnValue.Text(0, utf8),
            ColumnValue.Null(),
            ColumnValue.FromDouble(3.14),
        };
        var buffer = new byte[128];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Equal(4, decoded.Length);
        Assert.Equal(42L, decoded[0].AsInt64());
        Assert.Equal("Sharc", decoded[1].AsString());
        Assert.True(decoded[2].IsNull);
        Assert.Equal(3.14, decoded[3].AsDouble());
    }

    [Fact]
    public void EncodeRecord_AllStorageClasses_RoundTripsCorrectly()
    {
        byte[] blob = [0x01, 0x02, 0x03];
        byte[] text = "test"u8.ToArray();
        var columns = new[]
        {
            ColumnValue.Null(),
            ColumnValue.FromInt64(0, -999),
            ColumnValue.FromDouble(-1.5),
            ColumnValue.Text(0, text),
            ColumnValue.Blob(0, blob),
        };
        var buffer = new byte[128];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Equal(5, decoded.Length);
        Assert.True(decoded[0].IsNull);
        Assert.Equal(-999L, decoded[1].AsInt64());
        Assert.Equal(-1.5, decoded[2].AsDouble());
        Assert.Equal("test", decoded[3].AsString());
        Assert.Equal(blob, decoded[4].AsBytes().ToArray());
    }

    // ── Integer boundary encoding ──

    [Fact]
    public void EncodeRecord_IntConstants_EncodeZeroBodyBytes()
    {
        var columns = new[]
        {
            ColumnValue.FromInt64(0, 0),
            ColumnValue.FromInt64(0, 1),
        };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        // Header: header-size (1 byte = 3) + serial type 8 (1 byte) + serial type 9 (1 byte) = 3 bytes
        // Body: 0 bytes (constants)
        Assert.Equal(3, written);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Equal(2, decoded.Length);
        Assert.Equal(0L, decoded[0].AsInt64());
        Assert.Equal(1L, decoded[1].AsInt64());
    }

    [Fact]
    public void EncodeRecord_NegativeInt_RoundTripsCorrectly()
    {
        var columns = new[] { ColumnValue.FromInt64(0, -1) };
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(-1L, decoded[0].AsInt64());
    }

    // ── Large text ──

    [Fact]
    public void EncodeRecord_LargeText_RoundTripsCorrectly()
    {
        byte[] utf8 = new byte[1024];
        Array.Fill(utf8, (byte)'X');
        var columns = new[] { ColumnValue.Text(0, utf8) };
        var buffer = new byte[2048];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Single(decoded);
        Assert.Equal(ColumnStorageClass.Text, decoded[0].StorageClass);
        Assert.Equal(1024, decoded[0].AsBytes().Length);
        Assert.Equal(utf8, decoded[0].AsBytes().ToArray());
    }

    // ── ComputeEncodedSize ──

    [Fact]
    public void ComputeEncodedSize_SingleNull_MatchesActualEncoding()
    {
        var columns = new[] { ColumnValue.Null() };
        int computed = RecordEncoder.ComputeEncodedSize(columns);

        var buffer = new byte[64];
        int actual = RecordEncoder.EncodeRecord(columns, buffer);
        Assert.Equal(actual, computed);
    }

    [Fact]
    public void ComputeEncodedSize_MixedColumns_MatchesActualEncoding()
    {
        byte[] utf8 = "Hello, Sharc!"u8.ToArray();
        var columns = new[]
        {
            ColumnValue.FromInt64(0, 42),
            ColumnValue.Text(0, utf8),
            ColumnValue.Null(),
            ColumnValue.FromDouble(9.99),
            ColumnValue.Blob(0, new byte[] { 0xAA, 0xBB }),
        };
        int computed = RecordEncoder.ComputeEncodedSize(columns);

        var buffer = new byte[256];
        int actual = RecordEncoder.EncodeRecord(columns, buffer);
        Assert.Equal(actual, computed);
    }

    [Fact]
    public void ComputeEncodedSize_LargeText_MatchesActualEncoding()
    {
        byte[] utf8 = new byte[500];
        var columns = new[] { ColumnValue.Text(0, utf8) };
        int computed = RecordEncoder.ComputeEncodedSize(columns);

        var buffer = new byte[1024];
        int actual = RecordEncoder.EncodeRecord(columns, buffer);
        Assert.Equal(actual, computed);
    }

    // ── Empty record ──

    [Fact]
    public void EncodeRecord_NoColumns_ProducesHeaderOnly()
    {
        var columns = Array.Empty<ColumnValue>();
        var buffer = new byte[64];
        int written = RecordEncoder.EncodeRecord(columns, buffer);

        // Header: header-size varint (1 byte, value = 1) only
        Assert.Equal(1, written);

        var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
        Assert.Empty(decoded);
    }

    // ── Fuzz: random round-trips ──

    [Fact]
    public void EncodeRecord_RandomValues_RoundTripFuzz()
    {
        var rng = new Random(42);
        for (int trial = 0; trial < 10; trial++)
        {
            int colCount = rng.Next(1, 8);
            var columns = new ColumnValue[colCount];
            for (int c = 0; c < colCount; c++)
            {
                columns[c] = rng.Next(5) switch
                {
                    0 => ColumnValue.Null(),
                    1 => ColumnValue.FromInt64(0, rng.NextInt64()),
                    2 => ColumnValue.FromDouble(rng.NextDouble() * 1000),
                    3 => ColumnValue.Text(0, RandomBytes(rng, rng.Next(0, 50))),
                    _ => ColumnValue.Blob(0, RandomBytes(rng, rng.Next(0, 50))),
                };
            }

            int size = RecordEncoder.ComputeEncodedSize(columns);
            var buffer = new byte[size];
            int written = RecordEncoder.EncodeRecord(columns, buffer);
            Assert.Equal(size, written);

            var decoded = _decoder.DecodeRecord(buffer.AsSpan(0, written));
            Assert.Equal(colCount, decoded.Length);

            for (int c = 0; c < colCount; c++)
            {
                Assert.Equal(columns[c].StorageClass, decoded[c].StorageClass);
                switch (columns[c].StorageClass)
                {
                    case ColumnStorageClass.Null:
                        Assert.True(decoded[c].IsNull);
                        break;
                    case ColumnStorageClass.Integral:
                        Assert.Equal(columns[c].AsInt64(), decoded[c].AsInt64());
                        break;
                    case ColumnStorageClass.Real:
                        Assert.Equal(columns[c].AsDouble(), decoded[c].AsDouble());
                        break;
                    case ColumnStorageClass.Text:
                    case ColumnStorageClass.Blob:
                        Assert.Equal(columns[c].AsBytes().ToArray(), decoded[c].AsBytes().ToArray());
                        break;
                }
            }
        }
    }

    private static byte[] RandomBytes(Random rng, int length)
    {
        var bytes = new byte[length];
        rng.NextBytes(bytes);
        return bytes;
    }
}
