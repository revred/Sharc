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

using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// TDD tests for SQLite varint decoding.
/// SQLite varints: 1â€“9 bytes, MSB continuation, big-endian payload.
/// Bytes 1-8: high bit = more bytes follow, low 7 bits = data.
/// Byte 9 (if reached): all 8 bits are data.
/// </summary>
public class VarintDecoderTests
{
    // --- Single-byte values (0x00â€“0x7F) ---

    [Fact]
    public void Read_SingleByteZero_ReturnsZeroAndConsumesOneByte()
    {
        ReadOnlySpan<byte> data = [0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(0L, value);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void Read_SingleByteOne_ReturnsOne()
    {
        ReadOnlySpan<byte> data = [0x01];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(1L, value);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void Read_SingleByteMax_Returns127()
    {
        ReadOnlySpan<byte> data = [0x7F];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(127L, value);
        Assert.Equal(1, consumed);
    }

    // --- Two-byte values ---

    [Fact]
    public void Read_TwoBytes_128_ReturnsCorrectValue()
    {
        // 128 = 0x80 in varint: first byte 0x81 (1 with continuation), second byte 0x00
        // Actually: 128 â†’ high 7 bits of first byte = 1, low 7 bits of second = 0
        // 0x81, 0x00 â†’ (1 << 7) | 0 = 128
        ReadOnlySpan<byte> data = [0x81, 0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(128L, value);
        Assert.Equal(2, consumed);
    }

    [Fact]
    public void Read_TwoBytes_16383_ReturnsCorrectValue()
    {
        // Max 2-byte varint: 0xFF, 0x7F â†’ ((0x7F) << 7) | 0x7F = 16383
        ReadOnlySpan<byte> data = [0xFF, 0x7F];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(16383L, value);
        Assert.Equal(2, consumed);
    }

    // --- Three-byte values ---

    [Fact]
    public void Read_ThreeBytes_16384_ReturnsCorrectValue()
    {
        // 16384 = 0x81, 0x80, 0x00
        ReadOnlySpan<byte> data = [0x81, 0x80, 0x00];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(16384L, value);
        Assert.Equal(3, consumed);
    }

    // --- Known SQLite values ---

    [Fact]
    public void Read_Value500_CorrectlyDecoded()
    {
        // 500 = (3 << 7) | 116 = 500; encoded as 0x83, 0x74
        ReadOnlySpan<byte> data = [0x83, 0x74];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(500L, value);
        Assert.Equal(2, consumed);
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
        Assert.Equal(-1L, value); // All bits set = -1 in two's complement
        Assert.Equal(9, consumed);
    }

    // --- Edge cases ---

    [Fact]
    public void Read_EmptySpan_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VarintDecoder.Read(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void Read_ExtraTrailingBytes_OnlyConsumesVarint()
    {
        // Single-byte varint followed by garbage
        ReadOnlySpan<byte> data = [0x05, 0xFF, 0xFF];
        var consumed = VarintDecoder.Read(data, out var value);
        Assert.Equal(5L, value);
        Assert.Equal(1, consumed);
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
        Assert.Equal(expectedLength, VarintDecoder.GetEncodedLength(value));
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
    // Negative values (use full 9-byte encoding)
    [InlineData(-1)]
    [InlineData(-128)]
    [InlineData(-32768)]
    [InlineData(long.MinValue)]
    // Intermediate byte-width boundary values
    [InlineData(2_097_151)]             // 3-byte max
    [InlineData(268_435_455)]           // 4-byte max
    [InlineData(34_359_738_367)]        // 5-byte max
    [InlineData(4_398_046_511_103)]     // 6-byte max
    public void WriteRead_Roundtrip_ReturnsOriginalValue(long original)
    {
        Span<byte> buffer = stackalloc byte[9];
        var written = VarintDecoder.Write(buffer, original);
        var consumed = VarintDecoder.Read(buffer, out var decoded);

        Assert.Equal(original, decoded);
        Assert.Equal(written, consumed);
    }
}
