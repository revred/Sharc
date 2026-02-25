// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Sharc.Comparisons;

/// <summary>
/// Focused A/B micro-benchmarks for recent hot-path optimizations:
/// 1) Parameter-key hashing (legacy list/sort/indexer vs pooled pair-sort).
/// 2) Candidate span materialization (legacy ToArray().AsSpan() vs CollectionsMarshal.AsSpan()).
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class FocusedPerfBenchmarks
{
    private Dictionary<string, object> _parameters = null!;
    private List<(float Distance, int NodeIndex)> _candidates = null!;

    [Params(1, 8, 32, 128)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parameters = new Dictionary<string, object>(Count, StringComparer.Ordinal);
        for (int i = 0; i < Count; i++)
            _parameters[$"p{i:D3}"] = i * 17L;

        _candidates = new List<(float Distance, int NodeIndex)>(Count);
        for (int i = 0; i < Count; i++)
            _candidates.Add((i * 0.125f, i));
    }

    [Benchmark(Description = "ParamKey legacy: List+Sort+Indexer")]
    [BenchmarkCategory("ParamKeyHash")]
    public long ParamKey_Legacy()
    {
        if (_parameters.Count == 0)
            return 0;

        var keys = new List<string>(_parameters.Keys);
        keys.Sort(StringComparer.Ordinal);

        var hc = new HashCode();
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            hc.Add(key, StringComparer.Ordinal);
            hc.Add(_parameters[key]);
        }
        return hc.ToHashCode();
    }

    [Benchmark(Description = "ParamKey optimized: ArrayPool pair-sort")]
    [BenchmarkCategory("ParamKeyHash")]
    public long ParamKey_Optimized()
    {
        int count = _parameters.Count;
        if (count == 0)
            return 0;

        if (count == 1)
        {
            foreach (var kvp in _parameters)
            {
                var single = new HashCode();
                single.Add(kvp.Key, StringComparer.Ordinal);
                single.Add(kvp.Value);
                return single.ToHashCode();
            }
        }

        var rented = ArrayPool<KeyValuePair<string, object>>.Shared.Rent(count);
        try
        {
            int i = 0;
            foreach (var kvp in _parameters)
                rented[i++] = kvp;

            Array.Sort(rented, 0, count, PairComparer.Instance);

            var hc = new HashCode();
            for (int idx = 0; idx < count; idx++)
            {
                ref readonly var pair = ref rented[idx];
                hc.Add(pair.Key, StringComparer.Ordinal);
                hc.Add(pair.Value);
            }
            return hc.ToHashCode();
        }
        finally
        {
            Array.Clear(rented, 0, count);
            ArrayPool<KeyValuePair<string, object>>.Shared.Return(rented, clearArray: false);
        }
    }

    [Benchmark(Description = "Candidate span legacy: ToArray().AsSpan()")]
    [BenchmarkCategory("CandidateSpan")]
    public float CandidateSpan_Legacy()
    {
        var span = _candidates.ToArray().AsSpan();
        return span.Length == 0
            ? 0f
            : span[0].Distance + span[^1].Distance;
    }

    [Benchmark(Description = "Candidate span optimized: CollectionsMarshal.AsSpan()")]
    [BenchmarkCategory("CandidateSpan")]
    public float CandidateSpan_Optimized()
    {
        var span = CollectionsMarshal.AsSpan(_candidates);
        return span.Length == 0
            ? 0f
            : span[0].Distance + span[^1].Distance;
    }

    private sealed class PairComparer : IComparer<KeyValuePair<string, object>>
    {
        internal static readonly PairComparer Instance = new();

        public int Compare(KeyValuePair<string, object> x, KeyValuePair<string, object> y)
            => StringComparer.Ordinal.Compare(x.Key, y.Key);
    }
}
