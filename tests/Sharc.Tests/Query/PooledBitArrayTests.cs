// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Tests for <see cref="PooledBitArray"/> — bit-packed matched-row tracker.
/// Uses 1 bit per row (Col&lt;bit&gt;), backed by ArrayPool&lt;byte&gt; for Tier II
/// or stackalloc-compatible for Tier I.
/// </summary>
public sealed class PooledBitArrayTests
{
    [Fact]
    public void NewArray_AllBitsFalse()
    {
        using var bits = PooledBitArray.Create(64);
        for (int i = 0; i < 64; i++)
            Assert.False(bits.Get(i));
    }

    [Fact]
    public void Set_ThenGet_ReturnsTrue()
    {
        using var bits = PooledBitArray.Create(128);
        bits.Set(0);
        bits.Set(42);
        bits.Set(127);

        Assert.True(bits.Get(0));
        Assert.True(bits.Get(42));
        Assert.True(bits.Get(127));
    }

    [Fact]
    public void Set_DoesNotAffectOtherBits()
    {
        using var bits = PooledBitArray.Create(16);
        bits.Set(3);

        Assert.False(bits.Get(0));
        Assert.False(bits.Get(1));
        Assert.False(bits.Get(2));
        Assert.True(bits.Get(3));
        Assert.False(bits.Get(4));
        Assert.False(bits.Get(7));
    }

    [Fact]
    public void ByteCount_PacksBitsCorrectly()
    {
        // 1 bit per row → 8 rows per byte
        Assert.Equal(1, PooledBitArray.ByteCount(1));
        Assert.Equal(1, PooledBitArray.ByteCount(8));
        Assert.Equal(2, PooledBitArray.ByteCount(9));
        Assert.Equal(32, PooledBitArray.ByteCount(256));
        Assert.Equal(1024, PooledBitArray.ByteCount(8192));
    }

    [Fact]
    public void Create_ZeroLength_Works()
    {
        using var bits = PooledBitArray.Create(0);
        Assert.Equal(0, bits.Count);
    }

    [Fact]
    public void Count_ReturnsCapacity()
    {
        using var bits = PooledBitArray.Create(100);
        Assert.Equal(100, bits.Count);
    }

    [Fact]
    public void SetAll_AllBitsBecome_True()
    {
        using var bits = PooledBitArray.Create(100);
        for (int i = 0; i < 100; i++)
            bits.Set(i);
        for (int i = 0; i < 100; i++)
            Assert.True(bits.Get(i));
    }

    [Fact]
    public void LargeArray_8192Bits_WorksCorrectly()
    {
        using var bits = PooledBitArray.Create(8192);

        // Set every 7th bit
        for (int i = 0; i < 8192; i += 7)
            bits.Set(i);

        for (int i = 0; i < 8192; i++)
        {
            bool expected = (i % 7) == 0;
            Assert.Equal(expected, bits.Get(i));
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var bits = PooledBitArray.Create(64);
        bits.Dispose();
        bits.Dispose(); // should not throw
    }

    [Fact]
    public void BoundaryBits_FirstAndLastInEachByte()
    {
        using var bits = PooledBitArray.Create(24);

        // Test bit 0, 7, 8, 15, 16, 23 (byte boundaries)
        bits.Set(0);
        bits.Set(7);
        bits.Set(8);
        bits.Set(15);
        bits.Set(16);
        bits.Set(23);

        Assert.True(bits.Get(0));
        Assert.True(bits.Get(7));
        Assert.True(bits.Get(8));
        Assert.True(bits.Get(15));
        Assert.True(bits.Get(16));
        Assert.True(bits.Get(23));

        // Middle bits should be false
        Assert.False(bits.Get(1));
        Assert.False(bits.Get(6));
        Assert.False(bits.Get(9));
        Assert.False(bits.Get(14));
    }

    [Fact]
    public void NonByteAligned_Count_WorksCorrectly()
    {
        // 13 bits needs 2 bytes (ceil(13/8) = 2)
        using var bits = PooledBitArray.Create(13);
        Assert.Equal(13, bits.Count);

        bits.Set(12); // last valid bit
        Assert.True(bits.Get(12));
    }
}
