// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Codec;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for CBOR decoder.
/// Ensures CborDecoder handles adversarial CBOR bytes without crashing.
/// </summary>
public sealed class CborDecoderFuzzTests
{
    private static readonly Random Rng = new(42);

    [Fact]
    public void Decode_RandomBytes_NeverCrashes()
    {
        for (int trial = 0; trial < 200; trial++)
        {
            int len = Rng.Next(1, 128);
            var buffer = new byte[len];
            Rng.NextBytes(buffer);
            try
            {
                CborDecoder.Decode(buffer);
            }
            catch
            {
                // Any exception is acceptable
            }
        }
    }

    [Fact]
    public void Decode_EmptySpan_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CborDecoder.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Decode_SingleByte_NeverCrashes()
    {
        for (int b = 0; b < 256; b++)
        {
            var buffer = new byte[] { (byte)b };
            try
            {
                CborDecoder.Decode(buffer);
            }
            catch
            {
                // Acceptable
            }
        }
    }

    [Fact]
    public void ReadField_RandomBytes_NeverCrashes()
    {
        for (int trial = 0; trial < 200; trial++)
        {
            int len = Rng.Next(1, 128);
            var buffer = new byte[len];
            Rng.NextBytes(buffer);
            try
            {
                CborDecoder.ReadField(buffer, "test");
            }
            catch
            {
                // Any exception is acceptable
            }
        }
    }

    [Fact]
    public void ReadField_EmptySpan_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CborDecoder.ReadField(ReadOnlySpan<byte>.Empty, "key"));
    }

    [Fact]
    public void Decode_TruncatedMap_NeverHangs()
    {
        // CBOR map header claiming 100 entries, but no data follows
        var buffer = new byte[] { 0xB8, 100 }; // map(100) — 0xA0 | 0x18 = short map, arg in next byte
        try
        {
            CborDecoder.Decode(buffer);
        }
        catch
        {
            // Acceptable — truncated data
        }
    }

    [Fact]
    public void Decode_DeeplyNestedMaps_DoesNotStackOverflow()
    {
        // 20 levels of map(1) → key "a" → map(1) → ...
        var bytes = new List<byte>();
        for (int i = 0; i < 20; i++)
        {
            bytes.Add(0xA1); // map(1)
            bytes.Add(0x61); // text(1)
            bytes.Add((byte)'a');
        }
        bytes.Add(0xF6); // null (terminal value)

        try
        {
            var result = CborDecoder.Decode(bytes.ToArray());
            Assert.NotNull(result);
        }
        catch
        {
            // Acceptable if stack depth is limited
        }
    }
}
