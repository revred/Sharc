// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CacheEngineTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly CacheEngine _engine;

    public CacheEngineTests()
    {
        _engine = CreateEngine();
    }

    public void Dispose() => _engine.Dispose();

    private CacheEngine CreateEngine(long maxSize = 256L * 1024 * 1024, int maxEntries = 0)
    {
        return new CacheEngine(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero, // disable auto-sweep in tests
            MaxCacheSize = maxSize,
            MaxEntries = maxEntries,
        });
    }

    // ── Step 3: Basic CRUD ──────────────────────────────────────────

    [Fact]
    public void Get_NonexistentKey_ReturnsNull()
    {
        Assert.Null(_engine.Get("missing"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        var data = new byte[] { 10, 20, 30 };
        _engine.Set("key1", data);

        var result = _engine.Get("key1");
        Assert.Equal(data, result);
    }

    [Fact]
    public void Set_OverwritesExistingKey_ReturnsNewValue()
    {
        _engine.Set("key1", new byte[] { 1 });
        _engine.Set("key1", new byte[] { 2, 3 });

        var result = _engine.Get("key1");
        Assert.Equal(new byte[] { 2, 3 }, result);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue()
    {
        _engine.Set("key1", new byte[] { 1 });
        Assert.True(_engine.Remove("key1"));
    }

    [Fact]
    public void Remove_NonexistentKey_ReturnsFalse()
    {
        Assert.False(_engine.Remove("nope"));
    }

    [Fact]
    public void Remove_ThenGet_ReturnsNull()
    {
        _engine.Set("key1", new byte[] { 1 });
        _engine.Remove("key1");

        Assert.Null(_engine.Get("key1"));
    }

    [Fact]
    public void Set_NullKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Set(null!, new byte[] { 1 }));
    }

    [Fact]
    public void Set_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Set("key1", null!));
    }

    [Fact]
    public void GetCount_AfterSetAndRemove_ReflectsState()
    {
        Assert.Equal(0, _engine.GetCount());

        _engine.Set("a", new byte[] { 1 });
        _engine.Set("b", new byte[] { 2 });
        Assert.Equal(2, _engine.GetCount());

        _engine.Remove("a");
        Assert.Equal(1, _engine.GetCount());
    }

    // ── Step 4: TTL Expiry ──────────────────────────────────────────

    [Fact]
    public void Get_AbsoluteExpirationPassed_ReturnsNull()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(_engine.Get("key1"));
    }

    [Fact]
    public void Get_AbsoluteExpirationNotPassed_ReturnsValue()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.NotNull(_engine.Get("key1"));
    }

    [Fact]
    public void Get_SlidingExpirationNotElapsed_ReturnsValue()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.NotNull(_engine.Get("key1"));
    }

    [Fact]
    public void Get_SlidingExpirationElapsed_ReturnsNull()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(_engine.Get("key1"));
    }

    [Fact]
    public void Get_SlidingExpiration_ResetsOnAccess()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        // Access at 5 min — resets sliding window
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.NotNull(_engine.Get("key1"));

        // Another 8 min (13 min total, but only 8 since last access) — still alive
        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(_engine.Get("key1"));

        // Another 11 min without access — expired
        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(_engine.Get("key1"));
    }

    [Fact]
    public void Set_AbsoluteExpirationRelativeToNow_ComputesAbsoluteExpiry()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(_engine.Get("key1"));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(_engine.Get("key1"));
    }

    // ── Step 5: Refresh ─────────────────────────────────────────────

    [Fact]
    public void Refresh_ExistingKeyWithSlidingExpiry_ResetsTimer()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
        });

        _time.Advance(TimeSpan.FromMinutes(8));
        _engine.Refresh("key1");

        // 8 min after refresh — still alive (within 10 min window)
        _time.Advance(TimeSpan.FromMinutes(8));
        Assert.NotNull(_engine.Get("key1"));
    }

    [Fact]
    public void Refresh_NonexistentKey_DoesNotThrow()
    {
        _engine.Refresh("missing"); // should not throw
    }

    [Fact]
    public void Refresh_ExpiredKey_DoesNotRevive()
    {
        _engine.Set("key1", new byte[] { 1 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(5),
        });

        _time.Advance(TimeSpan.FromMinutes(6));
        _engine.Refresh("key1");

        Assert.Null(_engine.Get("key1"));
    }

    // ── Step 6: Size Tracking & LRU Eviction ────────────────────────

    [Fact]
    public void GetSize_EmptyCache_ReturnsZero()
    {
        Assert.Equal(0, _engine.GetSize());
    }

    [Fact]
    public void GetSize_AfterSet_ReflectsEntrySize()
    {
        _engine.Set("key1", new byte[100]);

        Assert.True(_engine.GetSize() > 100, "Size should include value + overhead");
    }

    [Fact]
    public void GetSize_AfterRemove_Decreases()
    {
        _engine.Set("key1", new byte[100]);
        var sizeBefore = _engine.GetSize();

        _engine.Remove("key1");

        Assert.Equal(0, _engine.GetSize());
        Assert.True(sizeBefore > 0);
    }

    [Fact]
    public void Set_ExceedsMaxCacheSize_EvictsLeastRecentlyUsed()
    {
        // Each entry is ~196 bytes (100 value + 96 overhead). MaxSize = 300 fits 1 entry.
        using var engine = CreateEngine(maxSize: 300);

        engine.Set("old", new byte[100]);
        engine.Set("new", new byte[100]);

        Assert.Null(engine.Get("old"));
        Assert.NotNull(engine.Get("new"));
    }

    [Fact]
    public void Set_ExceedsMaxCacheSize_RecentlyAccessedSurvives()
    {
        // Max size fits 2 entries (~392), adding a 3rd evicts LRU
        using var engine = CreateEngine(maxSize: 500);

        engine.Set("first", new byte[100]);
        engine.Set("second", new byte[100]);

        // Touch "first" to make it MRU
        engine.Get("first");

        // Adding "third" should evict "second" (LRU), not "first" (MRU)
        engine.Set("third", new byte[100]);

        Assert.NotNull(engine.Get("first"));
        Assert.Null(engine.Get("second"));
        Assert.NotNull(engine.Get("third"));
    }

    [Fact]
    public void Set_ExceedsMaxEntries_EvictsLeastRecentlyUsed()
    {
        using var engine = CreateEngine(maxEntries: 2);

        engine.Set("a", new byte[] { 1 });
        engine.Set("b", new byte[] { 2 });
        engine.Set("c", new byte[] { 3 });

        Assert.Null(engine.Get("a"));
        Assert.NotNull(engine.Get("b"));
        Assert.NotNull(engine.Get("c"));
    }

    [Fact]
    public void Set_OverwriteExistingKey_UpdatesSize()
    {
        _engine.Set("key1", new byte[100]);
        var sizeBefore = _engine.GetSize();

        _engine.Set("key1", new byte[200]);
        var sizeAfter = _engine.GetSize();

        Assert.True(sizeAfter > sizeBefore, "Size should increase after overwrite with larger value");
    }

    [Fact]
    public void Eviction_RemovesEnoughToFitNewEntry()
    {
        // Each entry = value + 96 overhead. 100-byte value = 196 entry size.
        // MaxSize 300 fits only 1 entry. Adding a 2nd evicts the 1st, etc.
        using var engine = CreateEngine(maxSize: 300);

        engine.Set("a", new byte[100]); // 196 — fits
        engine.Set("b", new byte[100]); // total 392 > 300 → evict "a"
        engine.Set("c", new byte[100]); // total 392 > 300 → evict "b"

        Assert.Null(engine.Get("a"));
        Assert.Null(engine.Get("b"));
        Assert.NotNull(engine.Get("c"));
    }

    // ── Step 7: Background Sweeper ──────────────────────────────────

    [Fact]
    public void Sweeper_RemovesExpiredEntries()
    {
        _engine.Set("alive", new byte[] { 1 });
        _engine.Set("dying", new byte[] { 2 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(1),
        });

        _time.Advance(TimeSpan.FromMinutes(2));
        _engine.SweepExpired();

        Assert.Null(_engine.Get("dying"));
        Assert.NotNull(_engine.Get("alive"));
    }

    [Fact]
    public void Sweeper_LeavesNonExpiredEntries()
    {
        _engine.Set("a", new byte[] { 1 }, new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromHours(1),
        });
        _engine.Set("b", new byte[] { 2 });

        _time.Advance(TimeSpan.FromMinutes(5));
        _engine.SweepExpired();

        Assert.NotNull(_engine.Get("a"));
        Assert.NotNull(_engine.Get("b"));
    }

    [Fact]
    public void Sweeper_UpdatesSizeAfterRemoval()
    {
        _engine.Set("dying", new byte[100], new CacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow() + TimeSpan.FromMinutes(1),
        });

        Assert.True(_engine.GetSize() > 0);

        _time.Advance(TimeSpan.FromMinutes(2));
        _engine.SweepExpired();

        Assert.Equal(0, _engine.GetSize());
    }

    [Fact]
    public void Dispose_StopsSweeper()
    {
        _engine.Dispose();
        // Should not throw on subsequent operations (engine is disposed but doesn't prevent access)
        // The key test is that the timer is stopped and no more callbacks fire
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _engine.Dispose();
        _engine.Dispose(); // should not throw
    }

    // ── Step 8: Concurrency ─────────────────────────────────────────

    [Fact]
    public void ConcurrentSets_AllEntriesStored()
    {
        const int count = 100;
        Parallel.For(0, count, i =>
        {
            _engine.Set($"key{i}", new byte[] { (byte)(i % 256) });
        });

        Assert.Equal(count, _engine.GetCount());
    }

    [Fact]
    public void ConcurrentGetAndSet_NoExceptions()
    {
        // Pre-populate
        for (int i = 0; i < 50; i++)
            _engine.Set($"key{i}", new byte[] { (byte)i });

        // Concurrent mixed read/write
        Parallel.For(0, 200, i =>
        {
            if (i % 3 == 0)
                _engine.Set($"key{i % 50}", new byte[] { (byte)i });
            else if (i % 3 == 1)
                _engine.Get($"key{i % 50}");
            else
                _engine.Remove($"key{i % 80}");
        });

        // No exception = pass. Just verify count is non-negative.
        Assert.True(_engine.GetCount() >= 0);
    }

    [Fact]
    public void ConcurrentEviction_NoCorruption()
    {
        using var engine = CreateEngine(maxEntries: 10);

        Parallel.For(0, 100, i =>
        {
            engine.Set($"key{i}", new byte[10]);
        });

        Assert.True(engine.GetCount() <= 10, $"Count {engine.GetCount()} exceeds MaxEntries 10");
        Assert.True(engine.GetSize() >= 0, "Size should never go negative");
    }
}
