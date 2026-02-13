/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for the write-side serial type codec: ColumnValue → serial type.
/// </summary>
public class SerialTypeCodecWriteTests
{
    // ── NULL ──

    [Fact]
    public void GetSerialType_Null_Returns0()
    {
        var value = ColumnValue.Null();
        Assert.Equal(0L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Integer constants ──

    [Fact]
    public void GetSerialType_IntZero_Returns8()
    {
        var value = ColumnValue.FromInt64(0, 0);
        Assert.Equal(8L, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_IntOne_Returns9()
    {
        var value = ColumnValue.FromInt64(0, 1);
        Assert.Equal(9L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Small integers (1 byte: -128..127) ──

    [Theory]
    [InlineData(-128)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(127)]
    public void GetSerialType_Int8_Returns1(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(1L, SerialTypeCodec.GetSerialType(value));
    }

    // ── 2-byte integers (-32768..32767, excluding 1-byte range) ──

    [Theory]
    [InlineData(-32768)]
    [InlineData(-129)]
    [InlineData(128)]
    [InlineData(32767)]
    public void GetSerialType_Int16_Returns2(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(2L, SerialTypeCodec.GetSerialType(value));
    }

    // ── 3-byte integers (-8388608..8388607) ──

    [Theory]
    [InlineData(-8388608)]
    [InlineData(-32769)]
    [InlineData(32768)]
    [InlineData(8388607)]
    public void GetSerialType_Int24_Returns3(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(3L, SerialTypeCodec.GetSerialType(value));
    }

    // ── 4-byte integers (-2147483648..2147483647) ──

    [Theory]
    [InlineData(-2147483648L)]
    [InlineData(-8388609)]
    [InlineData(8388608)]
    [InlineData(2147483647)]
    public void GetSerialType_Int32_Returns4(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(4L, SerialTypeCodec.GetSerialType(value));
    }

    // ── 6-byte integers (-140737488355328..140737488355327) ──

    [Theory]
    [InlineData(-140737488355328L)]
    [InlineData(-2147483649L)]
    [InlineData(2147483648L)]
    [InlineData(140737488355327L)]
    public void GetSerialType_Int48_Returns5(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(5L, SerialTypeCodec.GetSerialType(value));
    }

    // ── 8-byte integers (outside 6-byte range) ──

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-140737488355329L)]
    [InlineData(140737488355328L)]
    [InlineData(long.MaxValue)]
    public void GetSerialType_Int64_Returns6(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        Assert.Equal(6L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Double ──

    [Fact]
    public void GetSerialType_Double_Returns7()
    {
        var value = ColumnValue.FromDouble(3.14159);
        Assert.Equal(7L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Text ──

    [Fact]
    public void GetSerialType_EmptyText_Returns13()
    {
        var value = ColumnValue.Text(13, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(13L, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_Text1Byte_Returns15()
    {
        var value = ColumnValue.Text(15, new byte[] { 0x41 }); // "A"
        Assert.Equal(15L, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_Text100Bytes_Returns213()
    {
        var value = ColumnValue.Text(213, new byte[100]);
        Assert.Equal(213L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Blob ──

    [Fact]
    public void GetSerialType_EmptyBlob_Returns12()
    {
        var value = ColumnValue.Blob(12, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(12L, SerialTypeCodec.GetSerialType(value));
    }

    [Fact]
    public void GetSerialType_Blob1Byte_Returns14()
    {
        var value = ColumnValue.Blob(14, new byte[] { 0xFF });
        Assert.Equal(14L, SerialTypeCodec.GetSerialType(value));
    }

    // ── Round-trip: GetSerialType → GetContentSize ──

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-128)]
    [InlineData(127)]
    [InlineData(-32768)]
    [InlineData(32767)]
    [InlineData(2147483647)]
    [InlineData(long.MaxValue)]
    public void GetSerialType_Integer_RoundTripsWithGetContentSize(long intValue)
    {
        var value = ColumnValue.FromInt64(0, intValue);
        long serialType = SerialTypeCodec.GetSerialType(value);
        int contentSize = SerialTypeCodec.GetContentSize(serialType);

        // For constants 0 and 1, content size is 0 (they store no bytes)
        if (intValue == 0 || intValue == 1)
        {
            Assert.Equal(0, contentSize);
        }
        else
        {
            // Content size must be enough to hold the value
            Assert.True(contentSize > 0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(500)]
    public void GetSerialType_Text_RoundTripsWithGetContentSize(int length)
    {
        var value = ColumnValue.Text(0, new byte[length]);
        long serialType = SerialTypeCodec.GetSerialType(value);
        Assert.Equal(length, SerialTypeCodec.GetContentSize(serialType));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(256)]
    public void GetSerialType_Blob_RoundTripsWithGetContentSize(int length)
    {
        var value = ColumnValue.Blob(0, new byte[length]);
        long serialType = SerialTypeCodec.GetSerialType(value);
        Assert.Equal(length, SerialTypeCodec.GetContentSize(serialType));
    }
}
