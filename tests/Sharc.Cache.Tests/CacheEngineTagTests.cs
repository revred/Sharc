// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Cache.Tests;

public sealed class CacheEngineTagTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly CacheEngine _engine;

    public CacheEngineTagTests()
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

    [Fact]
    public void Set_WithTags_GetSucceeds()
    {
        var data = new byte[] { 1, 2, 3 };
        _engine.Set("k1", data, new CacheEntryOptions { Tags = ["t1", "t2"] });

        var result = _engine.Get("k1");
        Assert.Equal(data, result);
    }

    [Fact]
    public void EvictByTag_RemovesAllTaggedEntries()
    {
        _engine.Set("a", [1], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Set("b", [2], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Set("c", [3], new CacheEntryOptions { Tags = ["t1"] });

        int removed = _engine.EvictByTag("t1");

        Assert.Equal(3, removed);
        Assert.Equal(0, _engine.GetCount());
    }

    [Fact]
    public void EvictByTag_ReturnsRemoveCount()
    {
        _engine.Set("a", [1], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Set("b", [2], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Set("c", [3]); // no tag

        int removed = _engine.EvictByTag("t1");

        Assert.Equal(2, removed);
        Assert.Equal(1, _engine.GetCount()); // "c" survives
    }

    [Fact]
    public void EvictByTag_NonexistentTag_ReturnsZero()
    {
        _engine.Set("a", [1], new CacheEntryOptions { Tags = ["t1"] });

        int removed = _engine.EvictByTag("nosuchtag");

        Assert.Equal(0, removed);
        Assert.Equal(1, _engine.GetCount());
    }

    [Fact]
    public void EvictByTags_MultipleTagsUnion()
    {
        _engine.Set("a", [1], new CacheEntryOptions { Tags = ["x"] });
        _engine.Set("b", [2], new CacheEntryOptions { Tags = ["y"] });
        _engine.Set("c", [3], new CacheEntryOptions { Tags = ["x", "y"] });

        int removed = _engine.EvictByTags(["x", "y"]);

        Assert.Equal(3, removed);
        Assert.Equal(0, _engine.GetCount());
    }

    [Fact]
    public void Set_OverwriteWithNewTags_ReplacesOldTags()
    {
        _engine.Set("k1", [1], new CacheEntryOptions { Tags = ["old"] });
        _engine.Set("k1", [2], new CacheEntryOptions { Tags = ["new"] });

        // Old tag should no longer reference k1
        Assert.Equal(0, _engine.EvictByTag("old"));
        // New tag should reference k1
        Assert.Equal(1, _engine.EvictByTag("new"));
    }

    [Fact]
    public void Remove_CleansUpTagMaps()
    {
        _engine.Set("k1", [1], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Remove("k1");

        // Tag map should be cleaned up
        int removed = _engine.EvictByTag("t1");
        Assert.Equal(0, removed);
    }

    [Fact]
    public void LruEviction_CleansUpTagMaps()
    {
        using var engine = CreateEngine(maxEntries: 1);
        engine.Set("a", [1], new CacheEntryOptions { Tags = ["t1"] });
        engine.Set("b", [2]); // evicts "a"

        Assert.Null(engine.Get("a"));
        int removed = engine.EvictByTag("t1");
        Assert.Equal(0, removed);
    }

    [Fact]
    public void SweepExpired_CleansUpTagMaps()
    {
        _engine.Set("k1", [1], new CacheEntryOptions
        {
            Tags = ["t1"],
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        });

        _time.Advance(TimeSpan.FromMinutes(2));
        _engine.SweepExpired();

        int removed = _engine.EvictByTag("t1");
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Set_NoTags_WorksAsBeforeNoOverhead()
    {
        _engine.Set("k1", [1, 2, 3]);

        var result = _engine.Get("k1");
        Assert.Equal(new byte[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void EvictByTag_OnlyRemovesTaggedEntries_LeavesOthers()
    {
        _engine.Set("tagged", [1], new CacheEntryOptions { Tags = ["t1"] });
        _engine.Set("untagged", [2]);

        _engine.EvictByTag("t1");

        Assert.Null(_engine.Get("tagged"));
        Assert.NotNull(_engine.Get("untagged"));
    }

    [Fact]
    public void Set_MultipleEntriesSameTag_EvictRemovesAll()
    {
        for (int i = 0; i < 100; i++)
            _engine.Set($"k{i}", [(byte)(i % 256)], new CacheEntryOptions { Tags = ["bulk"] });

        int removed = _engine.EvictByTag("bulk");

        Assert.Equal(100, removed);
        Assert.Equal(0, _engine.GetCount());
    }
}
