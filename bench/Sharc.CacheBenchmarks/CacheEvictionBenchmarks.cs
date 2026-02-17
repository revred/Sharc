// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Stress-tests LRU eviction under memory pressure.
/// Measures eviction throughput when continuously exceeding cache capacity.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "Eviction")]
public class CacheEvictionBenchmarks
{
    private CacheEngine _engine = null!;
    private byte[] _payload = null!;

    [Params(500, 2000)]
    public int MaxEntries { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxEntries = MaxEntries,
        });

        _payload = new byte[512];
        Random.Shared.NextBytes(_payload);

        // Pre-fill to capacity
        for (int i = 0; i < MaxEntries; i++)
            _engine.Set($"pre:{i}", _payload);
    }

    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    [Benchmark(Baseline = true)]
    public void InsertWithEviction_2x()
    {
        // Insert 2x capacity → every insert evicts one entry
        for (int i = 0; i < MaxEntries * 2; i++)
            _engine.Set($"evict:{i}", _payload);
    }

    [Benchmark]
    public void InterleavedReadWrite()
    {
        // Simulates a read-heavy workload with eviction pressure
        for (int i = 0; i < MaxEntries; i++)
        {
            _engine.Set($"new:{i}", _payload);
            // Read a few recently accessed entries to test MRU promotion
            _engine.Get($"new:{Math.Max(0, i - 1)}");
            _engine.Get($"new:{Math.Max(0, i - 2)}");
        }
    }
}
