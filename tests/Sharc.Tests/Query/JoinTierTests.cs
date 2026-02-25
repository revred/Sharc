// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Tests for <see cref="JoinTier"/> tier selection logic.
/// Thresholds: ≤256 → StackAlloc, 257–8192 → Pooled, >8192 → OpenAddress.
/// </summary>
public sealed class JoinTierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(256)]
    public void Select_AtOrBelowStackAllocThreshold_ReturnsStackAlloc(int count)
    {
        Assert.Equal(JoinTier.StackAlloc, JoinTier.Select(count));
    }

    [Theory]
    [InlineData(257)]
    [InlineData(1000)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void Select_BetweenStackAllocAndPooled_ReturnsPooled(int count)
    {
        Assert.Equal(JoinTier.Pooled, JoinTier.Select(count));
    }

    [Theory]
    [InlineData(8193)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void Select_AbovePooledThreshold_ReturnsOpenAddress(int count)
    {
        Assert.Equal(JoinTier.OpenAddress, JoinTier.Select(count));
    }

    [Fact]
    public void StackAllocThreshold_Is256()
    {
        Assert.Equal(256, JoinTier.StackAllocThreshold);
    }

    [Fact]
    public void PooledThreshold_Is8192()
    {
        Assert.Equal(8192, JoinTier.PooledThreshold);
    }
}
