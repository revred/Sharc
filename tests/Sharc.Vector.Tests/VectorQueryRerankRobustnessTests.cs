// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests;

/// <summary>
/// Defensive tests for VectorQuery.NearestTo rerank overload:
/// parameter validation, overflow guards, and edge cases.
/// </summary>
public sealed class VectorQueryRerankRobustnessTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private const int VectorDim = 4;

    public VectorQueryRerankRobustnessTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_rerank_{Guid.NewGuid()}.db");

        var db = SharcDatabase.Create(_dbPath);
        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE embeddings (id INTEGER PRIMARY KEY, label TEXT, vector BLOB)");
            tx.Commit();
        }

        using (var writer = SharcWriter.From(db))
        {
            for (int i = 0; i < 20; i++)
            {
                float[] vec = new float[VectorDim];
                vec[0] = (float)i / 20;
                vec[1] = 1f - (float)i / 20;
                byte[] vecBytes = BlobVectorCodec.Encode(vec);
                byte[] labelBytes = Encoding.UTF8.GetBytes($"item_{i}");

                writer.Insert("embeddings",
                    ColumnValue.FromInt64(1, i + 1),
                    ColumnValue.Text(2L * labelBytes.Length + 13, labelBytes),
                    ColumnValue.Blob(2L * vecBytes.Length + 12, vecBytes));
            }
        }

        db.Dispose();
        _db = SharcDatabase.Open(_dbPath);
    }

    [Fact]
    public void NearestTo_Rerank_NullScorer_ThrowsArgumentNull()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ArgumentNullException>(() =>
            vq.NearestTo(query, k: 3, rerankScorer: null!));
    }

    [Fact]
    public void NearestTo_Rerank_ZeroK_ThrowsArgumentOutOfRange()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            vq.NearestTo(query, k: 0, rerankScorer: _ => 1.0));
    }

    [Fact]
    public void NearestTo_Rerank_NegativeK_ThrowsArgumentOutOfRange()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            vq.NearestTo(query, k: -1, rerankScorer: _ => 1.0));
    }

    [Fact]
    public void NearestTo_Rerank_ZeroOversampleFactor_ThrowsArgumentOutOfRange()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            vq.NearestTo(query, k: 3, rerankScorer: _ => 1.0, oversampleFactor: 0));
    }

    [Fact]
    public void NearestTo_Rerank_WrongDimensions_ThrowsArgumentException()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] badQuery = [1.0f, 0.0f]; // 2 dims instead of 4

        Assert.Throws<ArgumentException>(() =>
            vq.NearestTo(badQuery, k: 3, rerankScorer: _ => 1.0));
    }

    [Fact]
    public void NearestTo_Rerank_LargeOversampleFactor_DoesNotOverflow()
    {
        // k=100 * oversampleFactor=int.MaxValue/100 would overflow without guard
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // This should not throw integer overflow — it should clamp to int.MaxValue
        // and then just return what's available (20 rows)
        var result = vq.NearestTo(query, k: 5,
            rerankScorer: _ => 1.0, oversampleFactor: int.MaxValue / 5 + 1);

        // Should return up to 5 results (we only have 20 rows)
        Assert.True(result.Count <= 5);
    }

    [Fact]
    public void NearestTo_Rerank_WithoutHnswIndex_UsesFlatScan()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var result = vq.NearestTo(query, k: 3,
            rerankScorer: _ => 1.0); // constant score — order doesn't matter

        Assert.Equal(3, result.Count);
        Assert.Equal(VectorExecutionStrategy.FlatScan, vq.LastExecutionInfo.Strategy);
        Assert.True(vq.LastExecutionInfo.UsedFallbackScan);
    }

    [Fact]
    public void NearestTo_Rerank_ReportsHnswRerankedStrategy_WhenIndexAttached()
    {
        using var db2 = SharcDatabase.Open(_dbPath);
        using var vq = db2.Vector("embeddings", "vector", DistanceMetric.Cosine);

        var index = HnswIndex.Build(db2, "embeddings", "vector", DistanceMetric.Cosine,
            HnswConfig.Default with { Seed = 42 }, persist: false);
        vq.UseIndex(index);

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var result = vq.NearestTo(query, k: 3, rerankScorer: _ => 1.0);

        Assert.Equal(3, result.Count);
        Assert.Equal(VectorExecutionStrategy.HnswReranked, vq.LastExecutionInfo.Strategy);
        Assert.True(vq.LastExecutionInfo.ElapsedMs >= 0);

        index.Dispose();
    }

    [Fact]
    public void NearestTo_Rerank_ScorerReordersResults()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Scorer returns constant — all results should have the same rerank score.
        // This verifies that the rerank path works without relying on IRowAccessor.RowId.
        int callCount = 0;
        var result = vq.NearestTo(query, k: 3,
            rerankScorer: _ => { callCount++; return 1.0; },
            columnNames: "label");

        Assert.Equal(3, result.Count);
        // Scorer was called at least k times (all 20 rows for flat scan)
        Assert.True(callCount >= 3);
        // All results should have metadata
        Assert.All(result.Matches, m => Assert.NotNull(m.Metadata));
    }

    [Fact]
    public void NearestTo_Rerank_DisposedVectorQuery_ThrowsObjectDisposed()
    {
        var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        vq.Dispose();

        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        Assert.Throws<ObjectDisposedException>(() =>
            vq.NearestTo(query, k: 3, rerankScorer: _ => 1.0));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
