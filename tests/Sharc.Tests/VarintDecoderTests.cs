using FluentAssertions;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for SQLite varint decoding.
/// SQLite varints: 1–9 bytes, MSB continuation, big-endian payload.
/// Bytes 1-8: high bit = more bytes follow, low 7 bits = data.
/// Byte 9 (if reached): all 8 bits are data.
/// </summary>
public class VarintDecoderTests
{
    // --- Single-byte values (0x00–0x7F) ---

    [Fact]
    public void Read_SingleByteZero_ReturnsZeroAndConsumesOneByte()
    {
        ReadOnlySpan<byte> data = [0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(0);
        consumed.Should().Be(1);
    }

    [Fact]
    public void Read_SingleByteOne_ReturnsOne()
    {
        ReadOnlySpan<byte> data = [0x01];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(1);
        consumed.Should().Be(1);
    }

    [Fact]
    public void Read_SingleByteMax_Returns127()
    {
        ReadOnlySpan<byte> data = [0x7F];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(127);
        consumed.Should().Be(1);
    }

    // --- Two-byte values ---

    [Fact]
    public void Read_TwoBytes_128_ReturnsCorrectValue()
    {
        // 128 = 0x80 in varint: first byte 0x81 (1 with continuation), second byte 0x00
        // Actually: 128 → high 7 bits of first byte = 1, low 7 bits of second = 0
        // 0x81, 0x00 → (1 << 7) | 0 = 128
        ReadOnlySpan<byte> data = [0x81, 0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(128);
        consumed.Should().Be(2);
    }

    [Fact]
    public void Read_TwoBytes_16383_ReturnsCorrectValue()
    {
        // Max 2-byte varint: 0xFF, 0x7F → ((0x7F) << 7) | 0x7F = 16383
        ReadOnlySpan<byte> data = [0xFF, 0x7F];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(16383);
        consumed.Should().Be(2);
    }

    // --- Three-byte values ---

    [Fact]
    public void Read_ThreeBytes_16384_ReturnsCorrectValue()
    {
        // 16384 = 0x81, 0x80, 0x00
        ReadOnlySpan<byte> data = [0x81, 0x80, 0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(16384);
        consumed.Should().Be(3);
    }

    // --- Known SQLite values ---

    [Fact]
    public void Read_Value500_CorrectlyDecoded()
    {
        // 500 = (3 << 7) | 116 = 500; encoded as 0x83, 0x74
        ReadOnlySpan<byte> data = [0x83, 0x74];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(500);
        consumed.Should().Be(2);
    }

    // --- Nine-byte (maximum) varint ---

    [Fact]
    public void Read_NineByteMaxPositive_ReturnsMaxValue()
    {
        // 9-byte varint encoding of max value
        // Bytes 1-8: all 0xFF (continuation + 7 bits each = 56 bits)
        // Byte 9: 0xFF (all 8 bits = data)
        // Total: 56 + 8 = 64 bits, all 1s = -1 as signed long
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(-1); // All bits set = -1 in two's complement
        consumed.Should().Be(9);
    }

    // --- Edge cases ---

    [Fact]
    public void Read_EmptySpan_ThrowsArgumentException()
    {
        var act = () => VarintDecoder.Read(ReadOnlySpan<byte>.Empty, out _);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Read_ExtraTrailingBytes_OnlyConsumesVarint()
    {
        // Single-byte varint followed by garbage
        ReadOnlySpan<byte> data = [0x05, 0xFF, 0xFF];
        var consumed = VarintDecoder.Read(data, out var value);
        value.Should().Be(5);
        consumed.Should().Be(1);
    }

    // --- GetEncodedLength ---

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    public void GetEncodedLength_KnownValues_ReturnsCorrectLength(long value, int expectedLength)
    {
        VarintDecoder.GetEncodedLength(value).Should().Be(expectedLength);
    }

    // --- Round-trip ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(1_000_000)]
    [InlineData(long.MaxValue)]
    public void WriteRead_Roundtrip_ReturnsOriginalValue(long original)
    {
        Span<byte> buffer = stackalloc byte[9];
        var written = VarintDecoder.Write(buffer, original);
        var consumed = VarintDecoder.Read(buffer, out var decoded);

        decoded.Should().Be(original);
        consumed.Should().Be(written);
    }
}
