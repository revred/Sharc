// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

/// <summary>
/// Relative perf gates for interactive hover/neighborhood paths.
/// Gates compare ANN paths against forced flat scan on the same runner
/// to reduce machine-to-machine variance.
/// </summary>
public sealed class HnswPerformanceGateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;

    private const int VectorDim = 32;
    private const int RowCount = 5_000;

    public HnswPerformanceGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_hnsw_perf_gate_{Guid.NewGuid()}.db");

        var db = SharcDatabase.Create(_dbPath);
        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding BLOB)");
            tx.Commit();
        }

        var rng = new Random(1337);
        using (var writer = SharcWriter.From(db))
        {
            for (int i = 0; i < RowCount; i++)
            {
                var vector = new float[VectorDim];
                for (int d = 0; d < VectorDim; d++)
                    vector[d] = (float)(rng.NextDouble() * 2.0 - 1.0);

                byte[] payload = BlobVectorCodec.Encode(vector);
                writer.Insert("docs",
                    ColumnValue.Null(),
                    ColumnValue.Blob(12 + payload.Length * 2, payload));
            }
        }

        _db = db;
    }

    [Fact]
    [Trait("Category", "PerformanceGate")]
    public void HoverNearest_Hnsw_IsFasterThanForcedFlatScan()
    {
        using var index = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 42 }, persist: false);
        using var query = _db.Vector("docs", "embedding", DistanceMetric.Euclidean);
        query.UseIndex(index);

        float[] probe = BuildProbeVector(seed: 21);
        var flatOptions = new VectorSearchOptions { ForceFlatScan = true };

        for (int i = 0; i < 8; i++)
        {
            _ = query.NearestTo(probe, k: 16, flatOptions);
            _ = query.NearestTo(probe, k: 16);
        }

        var (flatUs, hnswUs) = MeasureInterleavedMicroseconds(
            iterations: 80,
            first: () => query.NearestTo(probe, k: 16, flatOptions),
            second: () => query.NearestTo(probe, k: 16));

        Assert.True(hnswUs < flatUs * 0.90,
            $"Hover-nearest perf gate failed: HNSW={hnswUs:F2}us, flat={flatUs:F2}us. Expected HNSW < 90% of flat.");
    }

    [Fact]
    [Trait("Category", "PerformanceGate")]
    public void NeighborhoodWithinDistance_HnswWidening_IsFasterThanForcedFlatScan()
    {
        using var index = HnswIndex.Build(_db, "docs", "embedding",
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 42 }, persist: false);
        using var query = _db.Vector("docs", "embedding", DistanceMetric.Euclidean);
        query.UseIndex(index);

        float[] probe = BuildProbeVector(seed: 99);
        var flatOptions = new VectorSearchOptions { ForceFlatScan = true };

        // Use a tighter neighborhood radius (k=12 frontier) to model
        // interactive hover/radius usage and keep ANN advantage stable on CI.
        var baseline = query.NearestTo(probe, k: 12, flatOptions);
        Assert.True(baseline.Count > 0);
        float radius = baseline[baseline.Count - 1].Distance;

        for (int i = 0; i < 8; i++)
        {
            _ = query.WithinDistance(probe, radius, flatOptions);
            _ = query.WithinDistance(probe, radius);
        }

        var (flatUs, hnswUs) = MeasureInterleavedMicroseconds(
            iterations: 60,
            first: () => query.WithinDistance(probe, radius, flatOptions),
            second: () => query.WithinDistance(probe, radius));

        _ = query.WithinDistance(probe, radius);
        Assert.False(query.LastExecutionInfo.UsedFallbackScan);
        Assert.Equal(VectorExecutionStrategy.HnswWithinDistanceWidening, query.LastExecutionInfo.Strategy);
        Assert.True(hnswUs < flatUs * 0.92,
            $"Neighborhood perf gate failed: HNSW={hnswUs:F2}us, flat={flatUs:F2}us. Expected HNSW < 92% of flat.");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-journal"); } catch { }
    }

    private static float[] BuildProbeVector(int seed)
    {
        var rng = new Random(seed);
        var vector = new float[VectorDim];
        for (int i = 0; i < vector.Length; i++)
            vector[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        return vector;
    }

    private static (double FirstUs, double SecondUs) MeasureInterleavedMicroseconds(
        int iterations,
        Func<VectorSearchResult> first,
        Func<VectorSearchResult> second)
    {
        long firstTicks = 0;
        long secondTicks = 0;
        long checksum = 0;

        for (int i = 0; i < iterations; i++)
        {
            bool firstStarts = (i & 1) == 0;

            if (firstStarts)
            {
                long t0 = Stopwatch.GetTimestamp();
                var a = first();
                long t1 = Stopwatch.GetTimestamp();
                var b = second();
                long t2 = Stopwatch.GetTimestamp();

                checksum += a.Count + b.Count;
                firstTicks += t1 - t0;
                secondTicks += t2 - t1;
            }
            else
            {
                long t0 = Stopwatch.GetTimestamp();
                var b = second();
                long t1 = Stopwatch.GetTimestamp();
                var a = first();
                long t2 = Stopwatch.GetTimestamp();

                checksum += a.Count + b.Count;
                secondTicks += t1 - t0;
                firstTicks += t2 - t1;
            }
        }

        GC.KeepAlive(checksum);
        double tickToUs = 1_000_000.0 / Stopwatch.Frequency;
        return (
            FirstUs: firstTicks * tickToUs / iterations,
            SecondUs: secondTicks * tickToUs / iterations);
    }
}
