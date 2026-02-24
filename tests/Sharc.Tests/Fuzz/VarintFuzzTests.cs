// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for varint decoder.
/// Ensures VarintDecoder handles adversarial inputs without crashing.
/// </summary>
public sealed class VarintFuzzTests
{
    private static readonly Random Rng = new(42); // deterministic seed

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    public void Read_RandomBytes_AtLength_NeverCrashes(int length)
    {
        var buffer = new byte[length];
        for (int trial = 0; trial < 100; trial++)
        {
            Rng.NextBytes(buffer);
            try
            {
                VarintDecoder.Read(buffer, out _);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // Acceptable for truncated inputs
            }
        }
    }

    [Fact]
    public void Read_AllZeros_ReturnsZero()
    {
        var buffer = new byte[9]; // max varint length
        int bytesRead = VarintDecoder.Read(buffer, out long value);
        Assert.Equal(0L, value);
        Assert.Equal(1, bytesRead);
    }

    [Fact]
    public void Read_AllOnes_DoesNotCrash()
    {
        var buffer = new byte[9];
        Array.Fill(buffer, (byte)0xFF);
        try
        {
            VarintDecoder.Read(buffer, out _);
        }
        catch
        {
            // Any exception is acceptable — just must not hang or crash process
        }
    }

    [Fact]
    public void Read_MaxContinuationBytes_DoesNotInfiniteLoop()
    {
        // 8 continuation bytes (high bit set) + 1 final byte
        var buffer = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        int bytesRead = VarintDecoder.Read(buffer, out _);
        Assert.True(bytesRead <= 9);
    }

    [Fact]
    public void Read_EmptySpan_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            VarintDecoder.Read(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void Read_RandomLargeBuffers_NeverHangs()
    {
        var buffer = new byte[256];
        for (int trial = 0; trial < 50; trial++)
        {
            Rng.NextBytes(buffer);
            int bytesRead = VarintDecoder.Read(buffer, out _);
            Assert.True(bytesRead >= 1 && bytesRead <= 9);
        }
    }
}
