// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Sharc.Cache.Tests;

public sealed class DistributedCacheTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly DistributedCache _cache;

    public DistributedCacheTests()
    {
        var engine = new CacheEngine(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero,
        });
        _cache = new DistributedCache(engine);
    }

    public void Dispose() => _cache.Dispose();

    // ── Step 9: Sync Methods ────────────────────────────────────────

    [Fact]
    public void Get_NonexistentKey_ReturnsNull()
    {
        Assert.Null(_cache.Get("missing"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        var data = new byte[] { 10, 20, 30 };
        _cache.Set("key1", data, new DistributedCacheEntryOptions());

        Assert.Equal(data, _cache.Get("key1"));
    }

    [Fact]
    public void Set_WithAbsoluteExpiration_Expires()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(_cache.Get("key1"));
    }

    [Fact]
    public void Set_WithAbsoluteExpirationRelativeToNow_Expires()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(_cache.Get("key1"));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(_cache.Get("key1"));
    }

    [Fact]
    public void Set_WithSlidingExpiration_ExpiresAfterIdle()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(_cache.Get("key1"));
    }

    [Fact]
    public void Refresh_WithSlidingExpiry_ExtendsLifetime()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(8));
        _cache.Refresh("key1");

        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(_cache.Get("key1"));
    }

    [Fact]
    public void Remove_ExistingKey_RemovesEntry()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions());
        _cache.Remove("key1");

        Assert.Null(_cache.Get("key1"));
    }

    [Fact]
    public void Set_NullKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _cache.Set(null!, new byte[] { 1 }, new DistributedCacheEntryOptions()));
    }

    // ── Step 10: Async Methods ──────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsCompletedTask()
    {
        var task = _cache.GetAsync("missing");
        Assert.True(task.IsCompleted, "In-memory Get should return synchronously");
        Assert.Null(await task);
    }

    [Fact]
    public async Task SetAsync_StoresValue_GetAsyncRetrieves()
    {
        var data = new byte[] { 42 };
        await _cache.SetAsync("key1", data, new DistributedCacheEntryOptions());

        var result = await _cache.GetAsync("key1");
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task RefreshAsync_ExtendsLifetime()
    {
        await _cache.SetAsync("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(8));
        await _cache.RefreshAsync("key1");

        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(await _cache.GetAsync("key1"));
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _cache.SetAsync("key1", new byte[] { 1 }, new DistributedCacheEntryOptions());
        await _cache.RemoveAsync("key1");

        Assert.Null(await _cache.GetAsync("key1"));
    }

    // ── Step 11: Edge Cases ─────────────────────────────────────────

    [Fact]
    public void Set_BothAbsoluteAndRelative_UsesEarlier()
    {
        // AbsoluteExpiration is 10 min out, Relative is 3 min — relative wins (earlier)
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
        });

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(_cache.Get("key1"));
    }

    [Fact]
    public void Set_NoExpiration_EntryLivesIndefinitely()
    {
        _cache.Set("key1", new byte[] { 1 }, new DistributedCacheEntryOptions());

        _time.Advance(TimeSpan.FromDays(365));
        Assert.NotNull(_cache.Get("key1"));
    }

    [Fact]
    public async Task Set_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _cache.SetAsync("key1", new byte[] { 1 }, new DistributedCacheEntryOptions(), cts.Token));
    }

    [Fact]
    public void Set_EmptyValue_Stores()
    {
        _cache.Set("key1", Array.Empty<byte>(), new DistributedCacheEntryOptions());

        var result = _cache.Get("key1");
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
