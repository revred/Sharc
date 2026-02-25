// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswGraphSearcherTests
{
    [Fact]
    public void Search_FindsNearestNeighbor()
    {
        // 5 known vectors, search for the nearest to a query
        var vectors = new float[][]
        {
            new[] { 1f, 0f },     // node 0
            new[] { 0f, 1f },     // node 1
            new[] { -1f, 0f },    // node 2
            new[] { 0f, -1f },    // node 3
            new[] { 0.9f, 0.1f }, // node 4 — closest to query (1, 0)
        };

        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[] { 10, 20, 30, 40, 50 };
        var config = HnswConfig.Default with { Seed = 42 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var query = new float[] { 1f, 0f };
        var result = HnswGraphSearcher.Search(graph, query, k: 1, ef: 50, resolver, distanceFn, DistanceMetric.Euclidean);

        Assert.Equal(1, result.Count);
        Assert.Equal(10L, result[0].RowId); // node 0 at (1,0) is exact match
    }

    [Fact]
    public void Search_ReturnsKNearest()
    {
        var vectors = GenerateRandomVectors(100, 8, seed: 42);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[100];
        for (int i = 0; i < 100; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42 };
        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var query = vectors[0]; // search for itself
        var result = HnswGraphSearcher.Search(graph, query, k: 5, ef: 50, resolver, distanceFn, DistanceMetric.Euclidean);

        Assert.Equal(5, result.Count);
        Assert.Equal(1L, result[0].RowId); // should find itself as nearest
        Assert.Equal(0f, result[0].Distance, 1e-5f);
    }

    [Fact]
    public void Search_KGreaterThanNodeCount_ReturnsAll()
    {
        var vectors = new float[][]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f },
            new[] { -1f, 0f },
        };

        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[] { 10, 20, 30 };
        var config = HnswConfig.Default with { Seed = 42 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var query = new float[] { 0.5f, 0.5f };
        var result = HnswGraphSearcher.Search(graph, query, k: 10, ef: 50, resolver, distanceFn, DistanceMetric.Euclidean);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Search_ResultsOrderedByDistance()
    {
        var vectors = GenerateRandomVectors(50, 4, seed: 123);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[50];
        for (int i = 0; i < 50; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 123 };
        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var query = new float[] { 0f, 0f, 0f, 0f };
        var result = HnswGraphSearcher.Search(graph, query, k: 10, ef: 50, resolver, distanceFn, DistanceMetric.Euclidean);

        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i].Distance >= result[i - 1].Distance,
                $"Results not sorted: [{i - 1}]={result[i - 1].Distance} > [{i}]={result[i].Distance}");
        }
    }

    [Fact]
    public void Search_CosineMetric_Works()
    {
        var vectors = new float[][]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
            new[] { 0f, 0f, 1f },
            new[] { 0.9f, 0.1f, 0f }, // closest to query by cosine
        };

        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[] { 1, 2, 3, 4 };
        var config = HnswConfig.Default with { Seed = 42 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Cosine, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Cosine);

        var query = new float[] { 1f, 0f, 0f };
        var result = HnswGraphSearcher.Search(graph, query, k: 2, ef: 50, resolver, distanceFn, DistanceMetric.Cosine);

        Assert.Equal(2, result.Count);
        // First result should be node 0 (exact match, distance ~0)
        Assert.Equal(1L, result[0].RowId);
        Assert.True(result[0].Distance < 0.01f);
    }

    [Fact]
    public void Search_HigherEf_BetterRecall()
    {
        // Build graph, search with low ef and high ef — high ef should find
        // at least as good results
        var vectors = GenerateRandomVectors(200, 16, seed: 42);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[200];
        for (int i = 0; i < 200; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42 };
        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var query = new float[16];
        for (int i = 0; i < 16; i++) query[i] = 0.5f;

        var resultLowEf = HnswGraphSearcher.Search(graph, query, k: 5, ef: 10, resolver, distanceFn, DistanceMetric.Euclidean);
        var resultHighEf = HnswGraphSearcher.Search(graph, query, k: 5, ef: 200, resolver, distanceFn, DistanceMetric.Euclidean);

        // Higher ef should produce results at least as good (lower worst distance)
        Assert.True(resultHighEf[resultHighEf.Count - 1].Distance <= resultLowEf[resultLowEf.Count - 1].Distance + 0.01f,
            $"High ef worst dist {resultHighEf[resultHighEf.Count - 1].Distance} should be <= low ef worst dist {resultLowEf[resultLowEf.Count - 1].Distance}");
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
