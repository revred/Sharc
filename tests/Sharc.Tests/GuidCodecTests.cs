// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
/// TDD tests for GuidCodec — RFC 4122 big-endian GUID ↔ 16-byte BLOB conversion.
/// </summary>
public class GuidCodecTests
{
    // --- Roundtrip ---

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        var original = Guid.NewGuid();
        Span<byte> buffer = stackalloc byte[16];
        GuidCodec.Encode(original, buffer);
        var decoded = GuidCodec.Decode(buffer);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encode_ThenDecode_EmptyGuid_RoundTrips()
    {
        Span<byte> buffer = stackalloc byte[16];
        GuidCodec.Encode(Guid.Empty, buffer);
        var decoded = GuidCodec.Decode(buffer);
        Assert.Equal(Guid.Empty, decoded);
    }

    [Fact]
    public void Encode_ThenDecode_MultipleRandomGuids_AllRoundTrip()
    {
        Span<byte> buffer = stackalloc byte[16];
        for (int i = 0; i < 100; i++)
        {
            var original = Guid.NewGuid();
            GuidCodec.Encode(original, buffer);
            var decoded = GuidCodec.Decode(buffer);
            Assert.Equal(original, decoded);
        }
    }

    // --- Big-endian byte ordering (RFC 4122) ---

    [Fact]
    public void Encode_KnownGuid_ProducesBigEndianBytes()
    {
        // RFC 4122 UUID: 01020304-0506-0708-090a-0b0c0d0e0f10
        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        Span<byte> buffer = stackalloc byte[16];
        GuidCodec.Encode(guid, buffer);

        // Big-endian (RFC 4122) layout:
        // time_low (4 bytes): 01 02 03 04
        // time_mid (2 bytes): 05 06
        // time_hi_and_version (2 bytes): 07 08
        // clock_seq_hi_and_reserved (1 byte): 09
        // clock_seq_low (1 byte): 0a
        // node (6 bytes): 0b 0c 0d 0e 0f 10
        byte[] expected = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                           0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10];
        Assert.Equal(expected, buffer.ToArray());
    }

    [Fact]
    public void Decode_BigEndianBytes_ReturnsCorrectGuid()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10];
        var guid = GuidCodec.Decode(bytes);
        Assert.Equal(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10"), guid);
    }

    [Fact]
    public void Encode_EmptyGuid_AllZeros()
    {
        Span<byte> buffer = stackalloc byte[16];
        GuidCodec.Encode(Guid.Empty, buffer);

        for (int i = 0; i < 16; i++)
            Assert.Equal(0, buffer[i]);
    }

    // --- IsGuidShaped ---

    [Fact]
    public void IsGuidShaped_SerialType44_ReturnsTrue()
    {
        // Serial type 44 = BLOB of 16 bytes: (44 - 12) / 2 = 16
        Assert.True(GuidCodec.IsGuidShaped(44));
    }

    [Fact]
    public void IsGuidShaped_OtherBlobSerialTypes_ReturnsFalse()
    {
        Assert.False(GuidCodec.IsGuidShaped(12)); // 0-byte blob
        Assert.False(GuidCodec.IsGuidShaped(14)); // 1-byte blob
        Assert.False(GuidCodec.IsGuidShaped(42)); // 15-byte blob
        Assert.False(GuidCodec.IsGuidShaped(46)); // 17-byte blob
    }

    [Fact]
    public void IsGuidShaped_NonBlobSerialTypes_ReturnsFalse()
    {
        Assert.False(GuidCodec.IsGuidShaped(0));  // NULL
        Assert.False(GuidCodec.IsGuidShaped(1));  // 8-bit int
        Assert.False(GuidCodec.IsGuidShaped(7));  // float
        Assert.False(GuidCodec.IsGuidShaped(45)); // TEXT (odd serial type)
    }

    // --- ReadOnlyMemory overload ---

    [Fact]
    public void Decode_ReadOnlyMemory_RoundTrips()
    {
        var original = Guid.NewGuid();
        byte[] buffer = new byte[16];
        GuidCodec.Encode(original, buffer);
        var decoded = GuidCodec.Decode((ReadOnlyMemory<byte>)buffer);
        Assert.Equal(original, decoded);
    }

    // --- Edge cases ---

    [Fact]
    public void Encode_DestinationTooSmall_Throws()
    {
        var buffer = new byte[15]; // too small
        Assert.Throws<ArgumentException>(() => GuidCodec.Encode(Guid.NewGuid(), buffer));
    }

    // --- Int64 Pair Conversion (Merged Column Support) ---

    [Fact]
    public void ToInt64Pair_KnownGuid_ReturnsExpectedHiLo()
    {
        // 01020304-0506-0708-090a-0b0c0d0e0f10
        // Big-endian bytes: 01 02 03 04 05 06 07 08 | 09 0a 0b 0c 0d 0e 0f 10
        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        var (hi, lo) = GuidCodec.ToInt64Pair(guid);

        Assert.Equal(0x0102030405060708L, hi);
        Assert.Equal(0x090a0b0c0d0e0f10L, lo);
    }

    [Fact]
    public void FromInt64Pair_KnownHiLo_ReturnsExpectedGuid()
    {
        long hi = 0x0102030405060708L;
        long lo = 0x090a0b0c0d0e0f10L;
        var guid = GuidCodec.FromInt64Pair(hi, lo);

        Assert.Equal(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10"), guid);
    }

    [Fact]
    public void ToInt64Pair_ThenFromInt64Pair_RoundTrips()
    {
        for (int i = 0; i < 100; i++)
        {
            var original = Guid.NewGuid();
            var (hi, lo) = GuidCodec.ToInt64Pair(original);
            var restored = GuidCodec.FromInt64Pair(hi, lo);
            Assert.Equal(original, restored);
        }
    }

    [Fact]
    public void ToInt64Pair_EmptyGuid_ReturnsZeroZero()
    {
        var (hi, lo) = GuidCodec.ToInt64Pair(Guid.Empty);
        Assert.Equal(0L, hi);
        Assert.Equal(0L, lo);
    }

    [Fact]
    public void ToInt64Pair_MatchesBlobEncode_SameBytes()
    {
        // Verify that ToInt64Pair produces the same byte interpretation as Encode
        var guid = Guid.NewGuid();
        var (hi, lo) = GuidCodec.ToInt64Pair(guid);

        byte[] blobBytes = new byte[16];
        GuidCodec.Encode(guid, blobBytes);
        long blobHi = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(blobBytes);
        long blobLo = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(blobBytes.AsSpan(8));

        Assert.Equal(blobHi, hi);
        Assert.Equal(blobLo, lo);
    }
}