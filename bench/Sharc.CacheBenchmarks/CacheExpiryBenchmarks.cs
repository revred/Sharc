// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Benchmarks for SweepExpired performance with varying expired entry ratios.
/// Measures the cost of TTL cleanup in a production-like cache.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "Expiry")]
public class CacheExpiryBenchmarks
{
    private CacheEngine _engine = null!;
    private FakeBenchTimeProvider _time = null!;

    [Params(10_000)]
    public int EntryCount { get; set; }

    [Params(10, 50, 90)]
    public int ExpiredPercentage { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _time = new FakeBenchTimeProvider();
        _engine = new CacheEngine(new CacheOptions
        {
            TimeProvider = _time,
            SweepInterval = TimeSpan.Zero, // manual sweep in benchmark
        });

        var payload = new byte[64];
        int expiredCount = EntryCount * ExpiredPercentage / 100;

        // Insert entries with short TTL (will be expired when we advance time)
        for (int i = 0; i < expiredCount; i++)
        {
            _engine.Set($"expire:{i}", payload, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
            });
        }

        // Insert entries with no expiry (will survive sweep)
        for (int i = expiredCount; i < EntryCount; i++)
            _engine.Set($"alive:{i}", payload);

        // Advance time to expire the short-TTL entries
        _time.Advance(TimeSpan.FromMinutes(2));
    }

    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    [Benchmark]
    public void SweepExpired()
    {
        _engine.SweepExpired();
    }

    /// <summary>Minimal TimeProvider for benchmarks.</summary>
    private sealed class FakeBenchTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
