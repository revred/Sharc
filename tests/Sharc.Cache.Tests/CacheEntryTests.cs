// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CacheEntryTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Constructor_WithValue_StoresValueAndSize()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var entry = new CacheEntry(data, null, null, _time.GetUtcNow());

        Assert.Same(data, entry.Value);
        Assert.True(entry.Size > data.Length, "Size should include overhead");
    }

    [Fact]
    public void IsExpired_NoExpiration_ReturnsFalse()
    {
        var entry = new CacheEntry(new byte[] { 1 }, null, null, _time.GetUtcNow());

        _time.Advance(TimeSpan.FromDays(365));

        Assert.False(entry.IsExpired(_time.GetUtcNow()));
    }

    [Fact]
    public void IsExpired_AbsoluteExpirationInFuture_ReturnsFalse()
    {
        var now = _time.GetUtcNow();
        var entry = new CacheEntry(new byte[] { 1 }, now + TimeSpan.FromMinutes(5), null, now);

        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.False(entry.IsExpired(_time.GetUtcNow()));
    }

    [Fact]
    public void IsExpired_AbsoluteExpirationInPast_ReturnsTrue()
    {
        var now = _time.GetUtcNow();
        var entry = new CacheEntry(new byte[] { 1 }, now + TimeSpan.FromMinutes(5), null, now);

        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.True(entry.IsExpired(_time.GetUtcNow()));
    }

    [Fact]
    public void IsExpired_SlidingExpirationNotElapsed_ReturnsFalse()
    {
        var now = _time.GetUtcNow();
        var entry = new CacheEntry(new byte[] { 1 }, null, TimeSpan.FromMinutes(10), now);

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(entry.IsExpired(_time.GetUtcNow()));
    }

    [Fact]
    public void IsExpired_SlidingExpirationElapsed_ReturnsTrue()
    {
        var now = _time.GetUtcNow();
        var entry = new CacheEntry(new byte[] { 1 }, null, TimeSpan.FromMinutes(10), now);

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.True(entry.IsExpired(_time.GetUtcNow()));
    }
}
