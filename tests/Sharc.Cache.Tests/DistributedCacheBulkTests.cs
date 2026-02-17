// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Sharc.Cache.Tests;

/// <summary>
/// Tests for <see cref="DistributedCacheExtensions"/> bulk operations.
/// </summary>
public sealed class DistributedCacheBulkTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly CacheEngine _engine;
    private readonly DistributedCache _cache;

    public DistributedCacheBulkTests()
    {
        _engine = new CacheEngine(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero,
        });
        _cache = new DistributedCache(_engine);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    [Fact]
    public void GetMany_DelegatesToEngine()
    {
        _cache.Set("a", new byte[] { 1 }, new DistributedCacheEntryOptions());
        _cache.Set("b", new byte[] { 2 }, new DistributedCacheEntryOptions());

        var result = ((IDistributedCache)_cache).GetMany(new[] { "a", "b", "missing" });

        Assert.Equal(3, result.Count);
        Assert.Equal(new byte[] { 1 }, result["a"]);
        Assert.Equal(new byte[] { 2 }, result["b"]);
        Assert.Null(result["missing"]);
    }

    [Fact]
    public void SetMany_DelegatesToEngine()
    {
        var entries = new[]
        {
            new KeyValuePair<string, byte[]>("x", new byte[] { 10 }),
            new KeyValuePair<string, byte[]>("y", new byte[] { 20 }),
        };

        ((IDistributedCache)_cache).SetMany(entries);

        Assert.Equal(new byte[] { 10 }, _cache.Get("x"));
        Assert.Equal(new byte[] { 20 }, _cache.Get("y"));
    }

    [Fact]
    public void RemoveMany_DelegatesToEngine()
    {
        _cache.Set("a", new byte[] { 1 }, new DistributedCacheEntryOptions());
        _cache.Set("b", new byte[] { 2 }, new DistributedCacheEntryOptions());
        _cache.Set("c", new byte[] { 3 }, new DistributedCacheEntryOptions());

        int removed = ((IDistributedCache)_cache).RemoveMany(new[] { "a", "c" });

        Assert.Equal(2, removed);
        Assert.Null(_cache.Get("a"));
        Assert.NotNull(_cache.Get("b"));
        Assert.Null(_cache.Get("c"));
    }

    [Fact]
    public void SetMany_WithOptions_MapsCorrectly()
    {
        var entries = new[]
        {
            new KeyValuePair<string, byte[]>("a", new byte[] { 1 }),
            new KeyValuePair<string, byte[]>("b", new byte[] { 2 }),
        };

        ((IDistributedCache)_cache).SetMany(entries, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        Assert.NotNull(_cache.Get("a"));
        Assert.NotNull(_cache.Get("b"));

        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(_cache.Get("a"));
        Assert.Null(_cache.Get("b"));
    }

    [Fact]
    public void GetMany_NonSharcCache_FallsBackToLoop()
    {
        IDistributedCache fake = new FakeDistributedCache();
        fake.Set("k1", new byte[] { 1 }, new DistributedCacheEntryOptions());
        fake.Set("k2", new byte[] { 2 }, new DistributedCacheEntryOptions());

        var result = fake.GetMany(new[] { "k1", "k2", "k3" });

        Assert.Equal(3, result.Count);
        Assert.Equal(new byte[] { 1 }, result["k1"]);
        Assert.Equal(new byte[] { 2 }, result["k2"]);
        Assert.Null(result["k3"]);
    }

    [Fact]
    public void SetMany_NonSharcCache_FallsBackToLoop()
    {
        var fake = new FakeDistributedCache();
        IDistributedCache cache = fake;

        cache.SetMany(new[]
        {
            new KeyValuePair<string, byte[]>("a", new byte[] { 10 }),
            new KeyValuePair<string, byte[]>("b", new byte[] { 20 }),
        });

        Assert.Equal(new byte[] { 10 }, cache.Get("a"));
        Assert.Equal(new byte[] { 20 }, cache.Get("b"));
    }

    [Fact]
    public void RemoveMany_NonSharcCache_FallsBackToLoop()
    {
        var fake = new FakeDistributedCache();
        IDistributedCache cache = fake;

        cache.Set("a", new byte[] { 1 }, new DistributedCacheEntryOptions());
        cache.Set("b", new byte[] { 2 }, new DistributedCacheEntryOptions());

        int removed = cache.RemoveMany(new[] { "a", "b", "missing" });

        // Fallback can't report exact count, returns count of all keys attempted
        Assert.Equal(3, removed);
        Assert.Null(cache.Get("a"));
        Assert.Null(cache.Get("b"));
    }

    [Fact]
    public void BulkOperations_LargeDataset_1000Keys()
    {
        var entries = new List<KeyValuePair<string, byte[]>>();
        for (int i = 0; i < 1000; i++)
            entries.Add(new($"item:{i}", BitConverter.GetBytes(i)));

        ((IDistributedCache)_cache).SetMany(entries);

        var keys = entries.Select(e => e.Key);
        var result = ((IDistributedCache)_cache).GetMany(keys);

        Assert.Equal(1000, result.Count);
        for (int i = 0; i < 1000; i++)
        {
            Assert.NotNull(result[$"item:{i}"]);
            Assert.Equal(i, BitConverter.ToInt32(result[$"item:{i}"]!));
        }

        int removed = ((IDistributedCache)_cache).RemoveMany(keys);
        Assert.Equal(1000, removed);
        Assert.Equal(0, _engine.GetCount());
    }

    /// <summary>
    /// Minimal IDistributedCache implementation for testing fallback paths.
    /// </summary>
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
