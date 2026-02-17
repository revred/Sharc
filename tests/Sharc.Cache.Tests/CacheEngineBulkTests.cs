// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

/// <summary>
/// Tests for <see cref="CacheEngine.GetMany"/>, <see cref="CacheEngine.SetMany"/>,
/// and <see cref="CacheEngine.RemoveMany"/> bulk operations.
/// </summary>
public sealed class CacheEngineBulkTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly CacheEngine _engine;

    public CacheEngineBulkTests()
    {
        _engine = CreateEngine();
    }

    public void Dispose() => _engine.Dispose();

    private CacheEngine CreateEngine(long maxSize = 256L * 1024 * 1024, int maxEntries = 0)
    {
        return new CacheEngine(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero,
            MaxCacheSize = maxSize,
            MaxEntries = maxEntries,
        });
    }

    // ── GetMany ──────────────────────────────────────────────────────

    [Fact]
    public void GetMany_AllHits_ReturnsAll()
    {
        for (int i = 0; i < 5; i++)
            _engine.Set($"k{i}", new byte[] { (byte)i });

        var result = _engine.GetMany(new[] { "k0", "k1", "k2", "k3", "k4" });

        Assert.Equal(5, result.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(new byte[] { (byte)i }, result[$"k{i}"]);
    }

    [Fact]
    public void GetMany_MixedHitMiss_ReturnsNullForMisses()
    {
        _engine.Set("a", new byte[] { 1 });
        _engine.Set("b", new byte[] { 2 });
        _engine.Set("c", new byte[] { 3 });

        var result = _engine.GetMany(new[] { "a", "missing1", "c", "missing2" });

        Assert.Equal(4, result.Count);
        Assert.Equal(new byte[] { 1 }, result["a"]);
        Assert.Null(result["missing1"]);
        Assert.Equal(new byte[] { 3 }, result["c"]);
        Assert.Null(result["missing2"]);
    }

    [Fact]
    public void GetMany_WithExpiredEntries_ReturnsNull()
    {
        _engine.Set("alive", new byte[] { 1 });
        _engine.Set("dying", new byte[] { 2 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(1),
        });

        _time.Advance(TimeSpan.FromMinutes(2));

        var result = _engine.GetMany(new[] { "alive", "dying" });

        Assert.Equal(new byte[] { 1 }, result["alive"]);
        Assert.Null(result["dying"]);
    }

    [Fact]
    public void GetMany_EmptyKeys_ReturnsEmptyDictionary()
    {
        var result = _engine.GetMany(Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void GetMany_PromotesHitsToMru()
    {
        // MaxEntries=3 → adding a 4th evicts LRU
        using var engine = CreateEngine(maxEntries: 3);

        engine.Set("a", new byte[] { 1 });
        engine.Set("b", new byte[] { 2 });
        engine.Set("c", new byte[] { 3 });

        // Bulk get "a" and "b" → promotes them to MRU
        engine.GetMany(new[] { "a", "b" });

        // Add "d" → should evict "c" (LRU), not "a" or "b"
        engine.Set("d", new byte[] { 4 });

        Assert.NotNull(engine.Get("a"));
        Assert.NotNull(engine.Get("b"));
        Assert.Null(engine.Get("c"));
        Assert.NotNull(engine.Get("d"));
    }

    // ── SetMany ──────────────────────────────────────────────────────

    [Fact]
    public void SetMany_AllNewKeys_AllStored()
    {
        var entries = new List<KeyValuePair<string, byte[]>>();
        for (int i = 0; i < 10; i++)
            entries.Add(new($"k{i}", new byte[] { (byte)i }));

        _engine.SetMany(entries);

        Assert.Equal(10, _engine.GetCount());
        for (int i = 0; i < 10; i++)
            Assert.Equal(new byte[] { (byte)i }, _engine.Get($"k{i}"));
    }

    [Fact]
    public void SetMany_OverwritesExisting_UpdatesValues()
    {
        _engine.Set("a", new byte[] { 1 });
        _engine.Set("b", new byte[] { 2 });
        _engine.Set("c", new byte[] { 3 });

        _engine.SetMany(new[]
        {
            new KeyValuePair<string, byte[]>("a", new byte[] { 10 }),
            new KeyValuePair<string, byte[]>("c", new byte[] { 30 }),
        });

        Assert.Equal(new byte[] { 10 }, _engine.Get("a"));
        Assert.Equal(new byte[] { 2 }, _engine.Get("b"));
        Assert.Equal(new byte[] { 30 }, _engine.Get("c"));
        Assert.Equal(3, _engine.GetCount());
    }

    [Fact]
    public void SetMany_WithExpiration_AllEntriesExpire()
    {
        var entries = new[]
        {
            new KeyValuePair<string, byte[]>("a", new byte[] { 1 }),
            new KeyValuePair<string, byte[]>("b", new byte[] { 2 }),
        };

        _engine.SetMany(entries, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.NotNull(_engine.Get("a"));
        Assert.NotNull(_engine.Get("b"));

        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(_engine.Get("a"));
        Assert.Null(_engine.Get("b"));
    }

    [Fact]
    public void SetMany_EvictsOnce_NotPerEntry()
    {
        // MaxEntries=5 → batch-setting 10 entries should evict oldest after all are added
        using var engine = CreateEngine(maxEntries: 5);

        var entries = new List<KeyValuePair<string, byte[]>>();
        for (int i = 0; i < 10; i++)
            entries.Add(new($"k{i}", new byte[] { (byte)i }));

        engine.SetMany(entries);

        // Should have exactly 5 entries (evicted down to max)
        Assert.Equal(5, engine.GetCount());

        // The last 5 inserted should survive (most recently used)
        for (int i = 5; i < 10; i++)
            Assert.NotNull(engine.Get($"k{i}"));
    }

    [Fact]
    public void SetMany_EmptyEntries_NoOp()
    {
        _engine.Set("existing", new byte[] { 1 });
        _engine.SetMany(Array.Empty<KeyValuePair<string, byte[]>>());
        Assert.Equal(1, _engine.GetCount());
    }

    // ── RemoveMany ───────────────────────────────────────────────────

    [Fact]
    public void RemoveMany_AllExist_ReturnsCount()
    {
        for (int i = 0; i < 5; i++)
            _engine.Set($"k{i}", new byte[] { (byte)i });

        int removed = _engine.RemoveMany(new[] { "k0", "k2", "k4" });

        Assert.Equal(3, removed);
        Assert.Equal(2, _engine.GetCount());
    }

    [Fact]
    public void RemoveMany_SomeMissing_ReturnsOnlyFoundCount()
    {
        _engine.Set("a", new byte[] { 1 });
        _engine.Set("b", new byte[] { 2 });

        int removed = _engine.RemoveMany(new[] { "a", "missing1", "b", "missing2" });

        Assert.Equal(2, removed);
        Assert.Equal(0, _engine.GetCount());
    }

    [Fact]
    public void RemoveMany_EmptyKeys_ReturnsZero()
    {
        _engine.Set("a", new byte[] { 1 });
        int removed = _engine.RemoveMany(Array.Empty<string>());
        Assert.Equal(0, removed);
        Assert.Equal(1, _engine.GetCount());
    }

    [Fact]
    public void RemoveMany_UpdatesSize()
    {
        _engine.Set("a", new byte[100]);
        _engine.Set("b", new byte[200]);
        var sizeBefore = _engine.GetSize();

        _engine.RemoveMany(new[] { "a", "b" });

        Assert.Equal(0, _engine.GetSize());
        Assert.True(sizeBefore > 0);
    }

    // ── Cross-cutting ────────────────────────────────────────────────

    [Fact]
    public void ConcurrentBulkSetAndGet_NoCorruption()
    {
        Parallel.For(0, 10, batch =>
        {
            var entries = new List<KeyValuePair<string, byte[]>>();
            for (int i = 0; i < 20; i++)
                entries.Add(new($"b{batch}_k{i}", new byte[] { (byte)batch, (byte)i }));

            _engine.SetMany(entries);

            var keys = entries.Select(e => e.Key).ToList();
            var result = _engine.GetMany(keys);
            // All keys from this batch should be present (no cross-batch corruption)
            foreach (var key in keys)
                Assert.True(result.ContainsKey(key));
        });

        Assert.True(_engine.GetCount() > 0);
        Assert.True(_engine.GetSize() > 0);
    }

    [Fact]
    public void BulkSet_ThenBulkGet_RoundTrips()
    {
        var entries = new List<KeyValuePair<string, byte[]>>();
        for (int i = 0; i < 100; i++)
            entries.Add(new($"item:{i}", BitConverter.GetBytes(i)));

        _engine.SetMany(entries);

        var keys = entries.Select(e => e.Key);
        var result = _engine.GetMany(keys);

        Assert.Equal(100, result.Count);
        for (int i = 0; i < 100; i++)
        {
            var value = result[$"item:{i}"];
            Assert.NotNull(value);
            Assert.Equal(i, BitConverter.ToInt32(value));
        }
    }
}
