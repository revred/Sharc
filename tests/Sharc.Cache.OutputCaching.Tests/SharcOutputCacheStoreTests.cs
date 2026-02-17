// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.OutputCaching.Tests;

public sealed class SharcOutputCacheStoreTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly SharcOutputCacheStore _store;

    public SharcOutputCacheStoreTests()
    {
        _store = new SharcOutputCacheStore(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero,
        });
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task GetAsync_Hit_ReturnsValue()
    {
        var value = new byte[] { 1, 2, 3 };
        await _store.SetAsync("k1", value, null, TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await _store.GetAsync("k1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var result = await _store.GetAsync("nonexistent", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_StoresWithTtl_ExpiresEntry()
    {
        var value = new byte[] { 10, 20, 30 };
        await _store.SetAsync("k1", value, null, TimeSpan.FromMinutes(1), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(2));

        var result = await _store.GetAsync("k1", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_NotYetExpired_ReturnsValue()
    {
        var value = new byte[] { 10, 20, 30 };
        await _store.SetAsync("k1", value, null, TimeSpan.FromMinutes(5), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(2));

        var result = await _store.GetAsync("k1", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SetAsync_WithTags_AssociatesTags()
    {
        var value = new byte[] { 1, 2, 3 };
        await _store.SetAsync("k1", value, ["tag1", "tag2"], TimeSpan.FromMinutes(5), CancellationToken.None);

        await _store.EvictByTagAsync("tag1", CancellationToken.None);

        var result = await _store.GetAsync("k1", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task EvictByTagAsync_RemovesAllTaggedEntries()
    {
        await _store.SetAsync("a", [1], ["shared"], TimeSpan.FromMinutes(5), CancellationToken.None);
        await _store.SetAsync("b", [2], ["shared"], TimeSpan.FromMinutes(5), CancellationToken.None);
        await _store.SetAsync("c", [3], ["shared"], TimeSpan.FromMinutes(5), CancellationToken.None);

        await _store.EvictByTagAsync("shared", CancellationToken.None);

        Assert.Null(await _store.GetAsync("a", CancellationToken.None));
        Assert.Null(await _store.GetAsync("b", CancellationToken.None));
        Assert.Null(await _store.GetAsync("c", CancellationToken.None));
    }

    [Fact]
    public async Task EvictByTagAsync_NonexistentTag_NoError()
    {
        // Should not throw
        await _store.EvictByTagAsync("no-such-tag", CancellationToken.None);
    }

    [Fact]
    public async Task SetAsync_NullTags_WorksWithoutTags()
    {
        var value = new byte[] { 1, 2, 3 };
        await _store.SetAsync("k1", value, null, TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await _store.GetAsync("k1", CancellationToken.None);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SetAsync_OverwriteExisting_ReplacesValue()
    {
        await _store.SetAsync("k1", [1, 2, 3], null, TimeSpan.FromMinutes(5), CancellationToken.None);
        await _store.SetAsync("k1", [4, 5, 6], null, TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await _store.GetAsync("k1", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(new byte[] { 4, 5, 6 }, result);
    }

    [Fact]
    public async Task EvictByTag_OnlyTaggedRemoved_UntaggedSurvive()
    {
        await _store.SetAsync("tagged", [1], ["evict-me"], TimeSpan.FromMinutes(5), CancellationToken.None);
        await _store.SetAsync("untagged", [2], null, TimeSpan.FromMinutes(5), CancellationToken.None);

        await _store.EvictByTagAsync("evict-me", CancellationToken.None);

        Assert.Null(await _store.GetAsync("tagged", CancellationToken.None));
        Assert.NotNull(await _store.GetAsync("untagged", CancellationToken.None));
    }

    internal sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
