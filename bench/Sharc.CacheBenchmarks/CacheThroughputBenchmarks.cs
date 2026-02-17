// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Measures single-key and bulk throughput for cache operations.
/// Compares individual loops vs batch APIs to quantify lock contention savings.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "Throughput")]
public class CacheThroughputBenchmarks
{
    private CacheEngine _engine = null!;
    private byte[][] _values = null!;
    private string[] _keys = null!;
    private KeyValuePair<string, byte[]>[] _bulkEntries = null!;

    [Params(100, 1000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxCacheSize = 512L * 1024 * 1024,
        });

        _keys = new string[BatchSize];
        _values = new byte[BatchSize][];
        _bulkEntries = new KeyValuePair<string, byte[]>[BatchSize];

        for (int i = 0; i < BatchSize; i++)
        {
            _keys[i] = $"key:{i:D6}";
            _values[i] = new byte[256];
            Random.Shared.NextBytes(_values[i]);
            _bulkEntries[i] = new KeyValuePair<string, byte[]>(_keys[i], _values[i]);
        }

        // Pre-populate for Get benchmarks
        foreach (var kvp in _bulkEntries)
            _engine.Set(kvp.Key, kvp.Value);
    }

    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    [Benchmark(Baseline = true)]
    public void SingleGet_Loop()
    {
        for (int i = 0; i < BatchSize; i++)
            _engine.Get(_keys[i]);
    }

    [Benchmark]
    public Dictionary<string, byte[]?> BulkGetMany()
    {
        return _engine.GetMany(_keys);
    }

    [Benchmark]
    public void SingleSet_Loop()
    {
        for (int i = 0; i < BatchSize; i++)
            _engine.Set(_keys[i], _values[i]);
    }

    [Benchmark]
    public void BulkSetMany()
    {
        _engine.SetMany(_bulkEntries);
    }

    [Benchmark]
    public void SingleRemove_Loop()
    {
        // Re-populate first
        foreach (var kvp in _bulkEntries)
            _engine.Set(kvp.Key, kvp.Value);

        for (int i = 0; i < BatchSize; i++)
            _engine.Remove(_keys[i]);
    }

    [Benchmark]
    public int BulkRemoveMany()
    {
        // Re-populate first
        _engine.SetMany(_bulkEntries);
        return _engine.RemoveMany(_keys);
    }
}
