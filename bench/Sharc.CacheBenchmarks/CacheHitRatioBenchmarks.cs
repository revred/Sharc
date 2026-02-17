// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Measures cache performance under realistic access patterns:
/// Zipfian (80/20), uniform random, and sequential scan.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "HitRatio")]
public class CacheHitRatioBenchmarks
{
    private CacheEngine _engine = null!;
    private string[] _allKeys = null!;
    private int[] _zipfianIndices = null!;
    private int[] _uniformIndices = null!;

    private const int TotalKeys = 10_000;
    private const int CacheCapacityKeys = 2_000; // 20% of keys fit in cache
    private const int OperationCount = 50_000;

    [GlobalSetup]
    public void Setup()
    {
        // Size cache so only ~20% of keys fit → realistic eviction pressure
        int entrySize = 256 + 96; // value + overhead
        long maxCacheSize = (long)CacheCapacityKeys * entrySize;

        _engine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxCacheSize = maxCacheSize,
        });

        _allKeys = new string[TotalKeys];
        for (int i = 0; i < TotalKeys; i++)
        {
            _allKeys[i] = $"item:{i:D6}";
            _engine.Set(_allKeys[i], new byte[256]);
        }

        // Generate Zipfian distribution (80/20 rule)
        var rng = new Random(42);
        _zipfianIndices = new int[OperationCount];
        for (int i = 0; i < OperationCount; i++)
        {
            // Simple Zipf approximation: 80% of accesses go to 20% of keys
            if (rng.NextDouble() < 0.8)
                _zipfianIndices[i] = rng.Next(0, TotalKeys / 5); // hot 20%
            else
                _zipfianIndices[i] = rng.Next(0, TotalKeys);
        }

        // Generate uniform random distribution
        _uniformIndices = new int[OperationCount];
        for (int i = 0; i < OperationCount; i++)
            _uniformIndices[i] = rng.Next(0, TotalKeys);
    }

    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    [Benchmark(Baseline = true)]
    public int ZipfianAccess()
    {
        int hits = 0;
        for (int i = 0; i < OperationCount; i++)
        {
            if (_engine.Get(_allKeys[_zipfianIndices[i]]) != null)
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int UniformRandomAccess()
    {
        int hits = 0;
        for (int i = 0; i < OperationCount; i++)
        {
            if (_engine.Get(_allKeys[_uniformIndices[i]]) != null)
                hits++;
        }
        return hits;
    }

    [Benchmark]
    public int SequentialScan()
    {
        int hits = 0;
        for (int i = 0; i < OperationCount; i++)
        {
            if (_engine.Get(_allKeys[i % TotalKeys]) != null)
                hits++;
        }
        return hits;
    }
}
