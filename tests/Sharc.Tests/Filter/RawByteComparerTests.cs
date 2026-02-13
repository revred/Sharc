using System.Buffers.Binary;
using Sharc;
using Xunit;

namespace Sharc.Tests.Filter;

public sealed class RawByteComparerTests
{
    // ── Integer comparisons ──

    [Fact]
    public void CompareInt64_SerialType1_SingleByte_ReturnsCorrectComparison()
    {
        Span<byte> data = stackalloc byte[1];
        data[0] = 42;
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 1, 42));
        Assert.True(RawByteComparer.CompareInt64(data, 1, 100) < 0);
        Assert.True(RawByteComparer.CompareInt64(data, 1, 10) > 0);
    }

    [Fact]
    public void CompareInt64_SerialType1_NegativeValue()
    {
        Span<byte> data = stackalloc byte[1];
        data[0] = unchecked((byte)-5);  // 0xFB → -5 as signed byte
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 1, -5));
        Assert.True(RawByteComparer.CompareInt64(data, 1, 0) < 0);
    }

    [Fact]
    public void CompareInt64_SerialType2_TwoBytes()
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(data, 1000);
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 2, 1000));
    }

    [Fact]
    public void CompareInt64_SerialType4_FourBytes()
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(data, int.MaxValue);
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 4, int.MaxValue));
    }

    [Fact]
    public void CompareInt64_SerialType6_EightBytes_MaxValue()
    {
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(data, long.MaxValue);
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 6, long.MaxValue));
    }

    [Fact]
    public void CompareInt64_SerialType6_EightBytes_MinValue()
    {
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(data, long.MinValue);
        Assert.Equal(0, RawByteComparer.CompareInt64(data, 6, long.MinValue));
    }

    [Fact]
    public void CompareInt64_SerialType8_Constant0()
    {
        Assert.Equal(0, RawByteComparer.CompareInt64(ReadOnlySpan<byte>.Empty, 8, 0));
        Assert.True(RawByteComparer.CompareInt64(ReadOnlySpan<byte>.Empty, 8, 1) < 0);
    }

    [Fact]
    public void CompareInt64_SerialType9_Constant1()
    {
        Assert.Equal(0, RawByteComparer.CompareInt64(ReadOnlySpan<byte>.Empty, 9, 1));
        Assert.True(RawByteComparer.CompareInt64(ReadOnlySpan<byte>.Empty, 9, 0) > 0);
    }

    // ── Double comparisons ──

    [Fact]
    public void CompareDouble_PositiveValue()
    {
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(data, 3.14);
        Assert.Equal(0, RawByteComparer.CompareDouble(data, 3.14));
        Assert.True(RawByteComparer.CompareDouble(data, 4.0) < 0);
    }

    [Fact]
    public void CompareDouble_NegativeValue()
    {
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(data, -2.5);
        Assert.Equal(0, RawByteComparer.CompareDouble(data, -2.5));
    }

    // ── UTF-8 comparisons ──

    [Fact]
    public void Utf8Compare_EqualStrings_ReturnsZero()
    {
        byte[] a = "hello"u8.ToArray();
        byte[] b = "hello"u8.ToArray();
        Assert.Equal(0, RawByteComparer.Utf8Compare(a, b));
    }

    [Fact]
    public void Utf8Compare_LessThan_ReturnsNegative()
    {
        byte[] a = "abc"u8.ToArray();
        byte[] b = "abd"u8.ToArray();
        Assert.True(RawByteComparer.Utf8Compare(a, b) < 0);
    }

    [Fact]
    public void Utf8StartsWith_Match_ReturnsTrue()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] prefix = "hello"u8.ToArray();
        Assert.True(RawByteComparer.Utf8StartsWith(col, prefix));
    }

    [Fact]
    public void Utf8StartsWith_NoMatch_ReturnsFalse()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] prefix = "world"u8.ToArray();
        Assert.False(RawByteComparer.Utf8StartsWith(col, prefix));
    }

    [Fact]
    public void Utf8StartsWith_EmptyPrefix_ReturnsTrue()
    {
        byte[] col = "hello"u8.ToArray();
        Assert.True(RawByteComparer.Utf8StartsWith(col, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Utf8EndsWith_Match_ReturnsTrue()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] suffix = "world"u8.ToArray();
        Assert.True(RawByteComparer.Utf8EndsWith(col, suffix));
    }

    [Fact]
    public void Utf8EndsWith_NoMatch_ReturnsFalse()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] suffix = "hello"u8.ToArray();
        Assert.False(RawByteComparer.Utf8EndsWith(col, suffix));
    }

    [Fact]
    public void Utf8Contains_SubstringPresent_ReturnsTrue()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] pattern = "lo wo"u8.ToArray();
        Assert.True(RawByteComparer.Utf8Contains(col, pattern));
    }

    [Fact]
    public void Utf8Contains_SubstringAbsent_ReturnsFalse()
    {
        byte[] col = "hello world"u8.ToArray();
        byte[] pattern = "xyz"u8.ToArray();
        Assert.False(RawByteComparer.Utf8Contains(col, pattern));
    }

    [Fact]
    public void Utf8Contains_EmptyPattern_ReturnsTrue()
    {
        byte[] col = "hello"u8.ToArray();
        Assert.True(RawByteComparer.Utf8Contains(col, ReadOnlySpan<byte>.Empty));
    }
}
