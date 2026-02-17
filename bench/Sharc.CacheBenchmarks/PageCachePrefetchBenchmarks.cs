// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Core.IO;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Benchmarks comparing page-level LRU cache with and without prefetch.
/// Measures sequential table scan performance and random access impact.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "PagePrefetch")]
public class PageCachePrefetchBenchmarks
{
    private byte[] _dbData = null!;

    [Params(100, 500)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int pageSize = 4096;
        _dbData = new byte[pageSize * PageCount];

        // Write valid SQLite header
        "SQLite format 3\0"u8.CopyTo(_dbData);
        _dbData[16] = (byte)(pageSize >> 8);
        _dbData[17] = (byte)(pageSize & 0xFF);
        _dbData[18] = 1; _dbData[19] = 1;
        _dbData[20] = 0; _dbData[21] = 64; _dbData[22] = 32; _dbData[23] = 32;
        _dbData[28] = (byte)(PageCount >> 24);
        _dbData[29] = (byte)(PageCount >> 16);
        _dbData[30] = (byte)(PageCount >> 8);
        _dbData[31] = (byte)(PageCount & 0xFF);
        _dbData[47] = 4;

        // Write unique marker per page for data verification
        for (int p = 0; p < PageCount; p++)
        {
            int offset = p * pageSize + (p == 0 ? 100 : 0);
            _dbData[offset] = (byte)(p & 0xFF);
            _dbData[offset + 1] = (byte)((p >> 8) & 0xFF);
        }
    }

    [Benchmark(Baseline = true)]
    public int SequentialScan_NoPrefetch()
    {
        using var inner = new MemoryPageSource(_dbData);
        using var cached = new CachedPageSource(inner, capacity: 64);

        int sum = 0;
        for (uint p = 1; p <= (uint)PageCount; p++)
        {
            var page = cached.GetPage(p);
            sum += page[0];
        }
        return sum;
    }

    [Benchmark]
    public int SequentialScan_WithPrefetch()
    {
        using var inner = new MemoryPageSource(_dbData);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 8 };
        using var cached = new CachedPageSource(inner, capacity: 64, prefetchOptions: opts);

        int sum = 0;
        for (uint p = 1; p <= (uint)PageCount; p++)
        {
            var page = cached.GetPage(p);
            sum += page[0];
        }
        return sum;
    }

    [Benchmark]
    public int RandomAccess_NoPrefetch()
    {
        using var inner = new MemoryPageSource(_dbData);
        using var cached = new CachedPageSource(inner, capacity: 64);

        var rng = new Random(42);
        int sum = 0;
        for (int i = 0; i < PageCount; i++)
        {
            uint p = (uint)(rng.Next(1, PageCount + 1));
            var page = cached.GetPage(p);
            sum += page[0];
        }
        return sum;
    }

    [Benchmark]
    public int RandomAccess_WithPrefetch()
    {
        using var inner = new MemoryPageSource(_dbData);
        var opts = new PrefetchOptions { SequentialThreshold = 3, PrefetchDepth = 8 };
        using var cached = new CachedPageSource(inner, capacity: 64, prefetchOptions: opts);

        var rng = new Random(42);
        int sum = 0;
        for (int i = 0; i < PageCount; i++)
        {
            uint p = (uint)(rng.Next(1, PageCount + 1));
            var page = cached.GetPage(p);
            sum += page[0];
        }
        return sum;
    }
}
