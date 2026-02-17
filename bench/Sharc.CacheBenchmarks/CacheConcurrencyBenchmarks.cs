// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Sharc.Cache;

namespace Sharc.CacheBenchmarks;

/// <summary>
/// Multi-threaded cache benchmarks measuring contention patterns:
/// read-heavy (N readers, 1 writer) and balanced (N readers, N writers).
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Cache", "Concurrency")]
public class CacheConcurrencyBenchmarks
{
    private CacheEngine _engine = null!;
    private string[] _keys = null!;
    private byte[] _payload = null!;

    private const int KeyCount = 5000;
    private const int OpsPerThread = 10_000;

    [Params(1, 4, 8)]
    public int ThreadCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new CacheEngine(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            MaxEntries = KeyCount,
        });

        _keys = new string[KeyCount];
        _payload = new byte[128];
        Random.Shared.NextBytes(_payload);

        for (int i = 0; i < KeyCount; i++)
        {
            _keys[i] = $"key:{i:D5}";
            _engine.Set(_keys[i], _payload);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _engine.Dispose();

    [Benchmark(Baseline = true)]
    public void ReadHeavy_NReaders_1Writer()
    {
        var tasks = new Task[ThreadCount + 1];

        // Writer thread
        tasks[0] = Task.Run(() =>
        {
            var rng = new Random(0);
            for (int i = 0; i < OpsPerThread; i++)
                _engine.Set(_keys[rng.Next(KeyCount)], _payload);
        });

        // Reader threads
        for (int t = 1; t <= ThreadCount; t++)
        {
            int seed = t;
            tasks[t] = Task.Run(() =>
            {
                var rng = new Random(seed);
                for (int i = 0; i < OpsPerThread; i++)
                    _engine.Get(_keys[rng.Next(KeyCount)]);
            });
        }

        Task.WaitAll(tasks);
    }

    [Benchmark]
    public void Balanced_NReaders_NWriters()
    {
        var tasks = new Task[ThreadCount * 2];

        for (int t = 0; t < ThreadCount; t++)
        {
            int seed = t;
            // Writer
            tasks[t * 2] = Task.Run(() =>
            {
                var rng = new Random(seed);
                for (int i = 0; i < OpsPerThread; i++)
                    _engine.Set(_keys[rng.Next(KeyCount)], _payload);
            });

            // Reader
            tasks[t * 2 + 1] = Task.Run(() =>
            {
                var rng = new Random(seed + 1000);
                for (int i = 0; i < OpsPerThread; i++)
                    _engine.Get(_keys[rng.Next(KeyCount)]);
            });
        }

        Task.WaitAll(tasks);
    }
}
