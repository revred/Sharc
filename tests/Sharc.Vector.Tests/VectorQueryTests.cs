// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc;
using Sharc.Core;
using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class VectorQueryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;
    private const int VectorDim = 4;

    public VectorQueryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_vec_{Guid.NewGuid()}.db");

        // Create a test database with vector data
        var db = SharcDatabase.Create(_dbPath);

        using (var tx = db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE embeddings (id INTEGER PRIMARY KEY, label TEXT, category TEXT, score REAL, vector BLOB)");
            tx.Commit();
        }

        // Insert 10 vectors of dimension 4
        var vectors = GenerateTestVectors(10, VectorDim);
        using (var writer = SharcWriter.From(db))
        {
            for (int i = 0; i < 10; i++)
            {
                byte[] vecBytes = BlobVectorCodec.Encode(vectors[i]);
                long blobSerialType = 2L * vecBytes.Length + 12;
                byte[] labelBytes = Encoding.UTF8.GetBytes($"item_{i}");
                byte[] catBytes = Encoding.UTF8.GetBytes(i % 2 == 0 ? "A" : "B");

                writer.Insert("embeddings",
                    ColumnValue.FromInt64(1, i + 1),
                    ColumnValue.Text(2L * labelBytes.Length + 13, labelBytes),
                    ColumnValue.Text(2L * catBytes.Length + 13, catBytes),
                    ColumnValue.FromDouble((double)(i * 10)),
                    ColumnValue.Blob(blobSerialType, vecBytes));
            }
        }

        // Re-open for read
        db.Dispose();
        _db = SharcDatabase.Open(_dbPath);
    }

    [Fact]
    public void NearestTo_ReturnsCorrectTopK()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 3);

        Assert.Equal(3, results.Count);
        // All results should have non-negative distances
        Assert.All(results.Matches, m => Assert.True(m.Distance >= 0));
        // Results should be sorted by distance ascending
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Distance <= results[i].Distance);
    }

    [Fact]
    public void NearestTo_KLargerThanTable_ReturnsAll()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 100);

        Assert.Equal(10, results.Count);
    }

    [Fact]
    public void NearestTo_WithMetadataFilter_FiltersBeforeDistance()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        vq.Where(FilterStar.Column("category").Eq("A"));
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 10);

        // Only category "A" rows (5 out of 10)
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void WithinDistance_ReturnsAllWithinThreshold()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Euclidean);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Use a very large threshold to get all rows
        var results = vq.WithinDistance(query, maxDistance: 1000.0f);

        Assert.True(results.Count > 0);
        Assert.All(results.Matches, m => Assert.True(m.Distance <= 1000.0f));
    }

    [Fact]
    public void WithinDistance_TightThreshold_ReturnsSubset()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Euclidean);

        // Use the first vector as query — distance to itself should be ~0
        var vectors = GenerateTestVectors(10, VectorDim);
        float[] query = vectors[0];

        var results = vq.WithinDistance(query, maxDistance: 0.001f);

        // Should find at least the exact match
        Assert.True(results.Count >= 1);
    }

    [Fact]
    public void DimensionMismatch_ThrowsArgumentException()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] wrongDimQuery = [1.0f, 2.0f]; // 2-dim, but vectors are 4-dim

        Assert.Throws<ArgumentException>(() => vq.NearestTo(wrongDimQuery, k: 5));
    }

    [Fact]
    public void ClearFilters_ResetsState()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Filter to category A (5 results)
        vq.Where(FilterStar.Column("category").Eq("A"));
        var filtered = vq.NearestTo(query, k: 10);
        Assert.Equal(5, filtered.Count);

        // Clear and get all
        vq.ClearFilters();
        var all = vq.NearestTo(query, k: 10);
        Assert.Equal(10, all.Count);
    }

    [Fact]
    public void DisposedQuery_ThrowsObjectDisposedException()
    {
        var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        vq.Dispose();
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        Assert.Throws<ObjectDisposedException>(() => vq.NearestTo(query, k: 5));
    }

    [Fact]
    public void DotProduct_ReturnsDescendingOrder()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.DotProduct);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 5);

        // DotProduct results should be sorted descending (highest first)
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Distance >= results[i].Distance);
    }

    [Fact]
    public void RowIds_AreValid()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 5);

        // All row IDs should be in [1, 10]
        Assert.All(results.Matches, m =>
        {
            Assert.InRange(m.RowId, 1, 10);
        });

        // All row IDs should be distinct
        var ids = results.Matches.Select(m => m.RowId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EuclideanDistance_MetricWorks()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Euclidean);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 3);

        Assert.Equal(3, results.Count);
        // Euclidean results should be sorted ascending
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Distance <= results[i].Distance);
    }

    [Fact]
    public void VectorSearchResult_RowIds_ReturnsAllIds()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        var results = vq.NearestTo(query, k: 5);
        var rowIds = results.RowIds.ToList();

        Assert.Equal(5, rowIds.Count);
        Assert.All(rowIds, id => Assert.InRange(id, 1, 10));
    }

    [Fact]
    public void NearestTo_WithMetadataProjection_ReturnsExtractedMetadata()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Cosine);
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];

        // Request "label", "category", and "score" metadata
        var results = vq.NearestTo(query, k: 3, "label", "category", "score");

        Assert.Equal(3, results.Count);

        foreach (var match in results.Matches)
        {
            Assert.NotNull(match.Metadata);
            Assert.Equal(3, match.Metadata!.Count);
            
            Assert.True(match.Metadata.ContainsKey("label"));
            Assert.True(match.Metadata.ContainsKey("category"));
            Assert.True(match.Metadata.ContainsKey("score"));
            
            var label = Assert.IsType<string>(match.Metadata["label"]);
            Assert.StartsWith("item_", label);
            
            var category = Assert.IsType<string>(match.Metadata["category"]);
            Assert.True(category == "A" || category == "B");
            
            var score = Assert.IsType<double>(match.Metadata["score"]);
            Assert.True(score >= 0);
        }
    }

    [Fact]
    public void WithinDistance_WithMetadataProjection_ReturnsExtractedMetadata()
    {
        using var vq = _db.Vector("embeddings", "vector", DistanceMetric.Euclidean);

        // Query point very close to vector[0]
        var vectors = GenerateTestVectors(10, VectorDim);
        float[] query = vectors[0];

        // Ensure we retrieve some rows
        var results = vq.WithinDistance(query, maxDistance: 1000.0f, "label");

        Assert.True(results.Count > 0);

        foreach (var match in results.Matches)
        {
            Assert.NotNull(match.Metadata);
            Assert.Single(match.Metadata!);
            
            Assert.True(match.Metadata.ContainsKey("label"));
            var label = Assert.IsType<string>(match.Metadata["label"]);
            Assert.StartsWith("item_", label);
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { /* cleanup */ }
        try { File.Delete(_dbPath + ".journal"); } catch { /* cleanup */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Generates deterministic test vectors for reproducible tests.</summary>
    private static float[][] GenerateTestVectors(int count, int dimensions)
    {
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimensions];
            // Create vectors that spread across the space
            for (int d = 0; d < dimensions; d++)
                vectors[i][d] = MathF.Sin((i + 1) * (d + 1) * 0.7f);
        }
        return vectors;
    }
}
