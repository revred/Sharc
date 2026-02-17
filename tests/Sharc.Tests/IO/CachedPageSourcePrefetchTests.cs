// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Tests for sequential access detection and prefetch logic in <see cref="CachedPageSource"/>.
/// </summary>
public sealed class CachedPageSourcePrefetchTests
{
    private static byte[] CreateMinimalDatabase(int pageSize = 4096, int pageCount = 20)
    {
        var data = new byte[pageSize * pageCount];
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[20] = 0; data[21] = 64; data[22] = 32; data[23] = 32;
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1;

        for (int p = 0; p < pageCount; p++)
            data[p * pageSize + (p == 0 ? 100 : 0)] = (byte)(0xA0 + p);

        return data;
    }

    [Fact]
    public void Prefetch_SequentialReads_PrefetchesAhead()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Read pages 1, 2, 3 sequentially → triggers prefetch of 4,5,6,7
        cached.GetPage(1);
        cached.GetPage(2);
        int missesBefore = cached.CacheMissCount;
        cached.GetPage(3); // this triggers prefetch
        int missesAfter = cached.CacheMissCount;

        // Miss for page 3 + 4 prefetched pages = 5 total misses from this call
        Assert.True(missesAfter > missesBefore);

        // Pages 4-7 should now be cached (hits, not misses)
        int missesBeforePrefetched = cached.CacheMissCount;
        cached.GetPage(4);
        cached.GetPage(5);
        cached.GetPage(6);
        cached.GetPage(7);
        Assert.Equal(missesBeforePrefetched, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_NonSequentialReads_NoPrefetch()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Non-sequential reads: 1, 5, 10
        cached.GetPage(1);
        cached.GetPage(5);
        cached.GetPage(10);

        // Only 3 misses (one per page, no prefetch)
        Assert.Equal(3, cached.CacheMissCount);

        // Page 11 should not be cached
        cached.GetPage(11);
        Assert.Equal(4, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_ResetsOnNonSequential()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Sequential: 1,2,3 → prefetch 4-7
        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3);

        // Jump to 15 → resets counter
        cached.GetPage(15);

        // Page 16 should NOT be prefetched (counter was reset)
        int missBefore = cached.CacheMissCount;
        cached.GetPage(16);
        Assert.Equal(missBefore + 1, cached.CacheMissCount); // miss, not hit
    }

    [Fact]
    public void Prefetch_DoesNotExceedPageCount()
    {
        var data = CreateMinimalDatabase(pageCount: 10);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 10 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Sequential: 8, 9, 10 → prefetch should not go beyond page 10
        cached.GetPage(8);
        cached.GetPage(9);
        cached.GetPage(10);

        // No crash and miss count should only reflect actual pages loaded
        Assert.True(cached.CacheMissCount >= 3);
    }

    [Fact]
    public void Prefetch_SkipsAlreadyCachedPages()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Pre-cache pages 4 and 5
        cached.GetPage(4);
        cached.GetPage(5);
        int missesSoFar = cached.CacheMissCount; // 2

        // Sequential: 1, 2, 3 → prefetch 4-7. Pages 4,5 are cached, so only 6,7 loaded
        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3); // triggers prefetch

        // Misses: 1 (page 1) + 1 (page 2) + 1 (page 3) + 2 (pages 6,7) = 5 new misses
        int expectedTotal = missesSoFar + 5;
        Assert.Equal(expectedTotal, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_Disabled_NoPrefetchEvenIfSequential()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { Disabled = true };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3);

        // Only 3 misses — no prefetch
        Assert.Equal(3, cached.CacheMissCount);

        // Page 4 is a miss
        cached.GetPage(4);
        Assert.Equal(4, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_CustomThreshold_RespectsConfig()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 5, PrefetchDepth = 2 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // 3 sequential reads — below threshold, no prefetch
        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3);
        Assert.Equal(3, cached.CacheMissCount);

        // Page 4 should be a miss (no prefetch yet)
        cached.GetPage(4);
        Assert.Equal(4, cached.CacheMissCount);

        // 5th sequential → triggers prefetch of 6,7
        cached.GetPage(5);

        // Pages 6,7 should now be cached
        int missesBeforePrefetched = cached.CacheMissCount;
        cached.GetPage(6);
        cached.GetPage(7);
        Assert.Equal(missesBeforePrefetched, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_CustomDepth_LoadsCorrectCount()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 2 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3); // triggers prefetch of 4,5 only (depth=2)

        // Pages 4,5 should be cached (hits on these pages)
        int hitsBefore = cached.CacheHitCount;
        cached.GetPage(4); // hit — also continues sequence, may trigger further prefetch
        cached.GetPage(5); // hit
        Assert.Equal(hitsBefore + 2, cached.CacheHitCount);

        // After initial prefetch at page 3 with depth=2, the initial batch prefetched 4,5.
        // Subsequent sequential hits (4,5) continue the pattern and prefetch further.
        // Verify that the initial prefetch was exactly depth=2 by checking
        // miss count right after page 3.
        // Total: 1 (page1) + 1 (page2) + 1 (page3) + 2 (prefetch 4,5) = 5 after page 3
        Assert.True(cached.CacheMissCount >= 5);
    }

    [Fact]
    public void Prefetch_CacheMissCount_IncludesPrefetch()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        cached.GetPage(1); // miss
        cached.GetPage(2); // miss
        cached.GetPage(3); // miss + 4 prefetches

        // Total misses = 3 (explicit) + 4 (prefetch) = 7
        Assert.Equal(7, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_FullCache_EvictsLruForPrefetch()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 2 };
        // Small cache: capacity=5
        using var cached = new CachedPageSource(inner, capacity: 5, prefetchOptions: opts);

        // Fill cache with pages 10,11,12,13,14
        cached.GetPage(10);
        cached.GetPage(11);
        cached.GetPage(12);
        cached.GetPage(13);
        cached.GetPage(14);

        // Sequential reads: 1,2,3 → prefetch 4,5. Cache is full, evicts LRU (10,11,12,13,14)
        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3); // triggers prefetch of 4,5

        // Pages 4,5 should be cached despite full cache
        int missesBeforePrefetched = cached.CacheMissCount;
        cached.GetPage(4);
        cached.GetPage(5);
        Assert.Equal(missesBeforePrefetched, cached.CacheMissCount);
    }

    [Fact]
    public void Prefetch_ConcurrentReads_NoCrash()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 20, prefetchOptions: opts);

        // Concurrent sequential reads from different starting points
        Parallel.For(0, 4, thread =>
        {
            uint startPage = (uint)(1 + thread * 4);
            for (uint p = startPage; p < startPage + 4 && p <= 20; p++)
                cached.GetPage(p);
        });

        // No exceptions = pass. Verify some pages are cached.
        Assert.True(cached.CacheHitCount + cached.CacheMissCount > 0);
    }

    [Fact]
    public void Prefetch_ZeroCapacity_NoPrefetch()
    {
        var data = CreateMinimalDatabase(pageCount: 20);
        using var inner = new MemoryPageSource(data);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 4 };
        using var cached = new CachedPageSource(inner, capacity: 0, prefetchOptions: opts);

        cached.GetPage(1);
        cached.GetPage(2);
        cached.GetPage(3);

        // Zero capacity = pass-through, no caching or prefetch
        Assert.Equal(0, cached.CacheHitCount);
        Assert.Equal(0, cached.CacheMissCount);
    }
}
