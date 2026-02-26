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

        double flatUs = MeasureMeanMicroseconds(80, () => query.NearestTo(probe, k: 16, flatOptions));
        double hnswUs = MeasureMeanMicroseconds(80, () => query.NearestTo(probe, k: 16));

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

        var baseline = query.NearestTo(probe, k: 32, flatOptions);
        Assert.True(baseline.Count > 0);
        float radius = baseline[baseline.Count - 1].Distance;

        for (int i = 0; i < 8; i++)
        {
            _ = query.WithinDistance(probe, radius, flatOptions);
            _ = query.WithinDistance(probe, radius);
        }

        double flatUs = MeasureMeanMicroseconds(60, () => query.WithinDistance(probe, radius, flatOptions));
        double hnswUs = MeasureMeanMicroseconds(60, () => query.WithinDistance(probe, radius));

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

    private static double MeasureMeanMicroseconds(int iterations, Func<VectorSearchResult> run)
    {
        long checksum = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var result = run();
            checksum += result.Count;
        }
        sw.Stop();

        GC.KeepAlive(checksum);
        return sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
    }
}
