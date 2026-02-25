// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswIndexTests
{
    [Fact]
    public void BuildFromMemory_SearchFindsNearest()
    {
        var vectors = GenerateRandomVectors(100, 8, seed: 42);
        var rowIds = new long[100];
        for (int i = 0; i < 100; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean, config);

        Assert.Equal(100, index.Count);
        Assert.Equal(8, index.Dimensions);

        var result = index.Search(vectors[0], k: 5);

        Assert.Equal(5, result.Count);
        Assert.Equal(1L, result[0].RowId); // should find itself
    }

    [Fact]
    public void BuildFromMemory_Properties_Correct()
    {
        var vectors = GenerateRandomVectors(10, 4, seed: 1);
        var rowIds = new long[10];
        for (int i = 0; i < 10; i++) rowIds[i] = i;

        var config = HnswConfig.Default with { Seed = 1 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Cosine, config);

        Assert.Equal(10, index.Count);
        Assert.Equal(4, index.Dimensions);
        Assert.Equal(DistanceMetric.Cosine, index.Metric);
        Assert.Equal(16, index.Config.M);
    }

    [Fact]
    public void Search_DimensionMismatch_Throws()
    {
        var vectors = new float[][] { new[] { 1f, 0f } };
        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 1 },
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 1 });

        var wrongDim = new float[] { 1f, 0f, 0f }; // 3 dims, index is 2

        Assert.Throws<ArgumentException>(() => index.Search(wrongDim, k: 1));
    }

    [Fact]
    public void Search_KZero_Throws()
    {
        var vectors = new float[][] { new[] { 1f, 0f } };
        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 1 },
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 1 });

        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search(new float[] { 1f, 0f }, k: 0));
    }

    [Fact]
    public void Search_AfterDispose_Throws()
    {
        var vectors = new float[][] { new[] { 1f, 0f } };
        var index = HnswIndex.BuildFromMemory(vectors, new long[] { 1 },
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 1 });
        index.Dispose();

        Assert.Throws<ObjectDisposedException>(() => index.Search(new float[] { 1f, 0f }, k: 1));
    }

    [Fact]
    public void Search_KGreaterThanCount_ReturnsAll()
    {
        var vectors = new float[][]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f },
            new[] { -1f, 0f },
        };

        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 10, 20, 30 },
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 42 });

        var result = index.Search(new float[] { 0.5f, 0.5f }, k: 10);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Search_OverridesEf()
    {
        var vectors = GenerateRandomVectors(50, 4, seed: 42);
        var rowIds = new long[50];
        for (int i = 0; i < 50; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { EfSearch = 10, Seed = 42 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean, config);

        // Should not throw — ef overridden per query
        var result = index.Search(vectors[0], k: 5, ef: 200);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void BuildFromMemory_ZeroVectors_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => HnswIndex.BuildFromMemory(Array.Empty<float[]>(), Array.Empty<long>()));
    }

    [Fact]
    public void BuildFromMemory_SingleVector_Works()
    {
        var vectors = new float[][] { new[] { 1f, 2f, 3f } };
        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 42 },
            DistanceMetric.Euclidean, HnswConfig.Default with { Seed = 1 });

        Assert.Equal(1, index.Count);

        var result = index.Search(new float[] { 1f, 2f, 3f }, k: 1);
        Assert.Equal(1, result.Count);
        Assert.Equal(42L, result[0].RowId);
        Assert.Equal(0f, result[0].Distance, 1e-5f);
    }

    [Fact]
    public void Search_CosineMetric_FindsSimilarVectors()
    {
        // Vectors in different directions
        var vectors = new float[][]
        {
            new[] { 1f, 0f, 0f },      // east
            new[] { 0f, 1f, 0f },      // north
            new[] { 0f, 0f, 1f },      // up
            new[] { 0.95f, 0.05f, 0f }, // almost east
        };

        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 1, 2, 3, 4 },
            DistanceMetric.Cosine, HnswConfig.Default with { Seed = 42 });

        // Query: pure east — should find east (rowId 1) and almost-east (rowId 4) as top 2
        var result = index.Search(new float[] { 1f, 0f, 0f }, k: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(1L, result[0].RowId); // exact match
    }

    [Fact]
    public void BuildFromMemory_DotProduct_HigherIsBetter()
    {
        // Vectors with known dot product relationships
        var vectors = new float[][]
        {
            new[] { 1f, 0f, 0f },       // rowId 1: aligned with query
            new[] { 0.5f, 0.5f, 0f },   // rowId 2: partial alignment
            new[] { 0f, 0f, 1f },        // rowId 3: orthogonal
            new[] { -1f, 0f, 0f },       // rowId 4: opposite direction
        };

        using var index = HnswIndex.BuildFromMemory(vectors, new long[] { 1, 2, 3, 4 },
            DistanceMetric.DotProduct, HnswConfig.Default with { Seed = 42 });

        // Query: pure X axis — dot product with vector 0 = 1.0 (highest), vector 3 = -1.0 (lowest)
        var result = index.Search(new float[] { 1f, 0f, 0f }, k: 4);

        Assert.Equal(4, result.Count);
        // DotProduct results should be descending (higher = better)
        Assert.Equal(1L, result[0].RowId); // dot=1.0
        Assert.True(result[0].Distance >= result[1].Distance);
        Assert.True(result[1].Distance >= result[2].Distance);
        Assert.True(result[2].Distance >= result[3].Distance);
    }

    [Fact]
    public void BuildFromMemory_DotProduct_RecallAbove90Pct()
    {
        // Build with DotProduct and verify recall vs brute-force
        int count = 500;
        int dims = 16;
        var vectors = GenerateRandomVectors(count, dims, seed: 42);
        var rowIds = new long[count];
        for (int i = 0; i < count; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42, EfSearch = 100 };
        using var index = HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.DotProduct, config);

        // Brute-force top-10 by DotProduct
        float[] query = vectors[0];
        var bruteForce = new List<(long RowId, float Dist)>();
        var distFn = VectorDistanceFunctions.Resolve(DistanceMetric.DotProduct);
        for (int i = 0; i < count; i++)
        {
            float d = distFn(query, vectors[i]);
            bruteForce.Add((rowIds[i], d));
        }
        bruteForce.Sort((a, b) => b.Dist.CompareTo(a.Dist)); // descending for DotProduct
        var bruteTopK = bruteForce.Take(10).Select(x => x.RowId).ToHashSet();

        var result = index.Search(query, k: 10);
        var hnswTopK = new HashSet<long>();
        for (int i = 0; i < result.Count; i++)
            hnswTopK.Add(result[i].RowId);

        int overlap = bruteTopK.Intersect(hnswTopK).Count();
        Assert.True(overlap >= 9, $"Recall@10 for DotProduct is {overlap}/10 (expected >= 9)");
    }

    [Fact]
    public void BuildFromMemory_VectorsRowIdsMismatch_Throws()
    {
        var vectors = new float[][] { new[] { 1f, 2f } };
        var rowIds = new long[] { 1, 2 }; // length mismatch

        Assert.Throws<ArgumentException>(() =>
            HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean));
    }

    private static float[][] GenerateRandomVectors(int count, int dimensions, int seed)
    {
        var rng = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimensions];
            for (int d = 0; d < dimensions; d++)
                vectors[i][d] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        return vectors;
    }
}
