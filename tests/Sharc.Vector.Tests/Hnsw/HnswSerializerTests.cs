// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesGraphTopology()
    {
        var vectors = GenerateRandomVectors(50, 4, seed: 42);
        var rowIds = new long[50];
        for (int i = 0; i < 50; i++) rowIds[i] = (i + 1) * 100;

        var config = HnswConfig.Default with { Seed = 42 };
        var resolver = new MemoryVectorResolver(vectors);
        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Cosine, config);

        // Serialize
        byte[] data = HnswSerializer.Serialize(graph, config, 4, DistanceMetric.Cosine);
        Assert.True(data.Length > 0);

        // Deserialize
        var (loaded, loadedConfig, dims, metric) = HnswSerializer.Deserialize(data);

        // Verify metadata
        Assert.Equal(50, loaded.NodeCount);
        Assert.Equal(graph.EntryPoint, loaded.EntryPoint);
        Assert.Equal(graph.MaxLevel, loaded.MaxLevel);
        Assert.Equal(4, dims);
        Assert.Equal(DistanceMetric.Cosine, metric);

        // Verify config
        Assert.Equal(config.M, loadedConfig.M);
        Assert.Equal(config.M0, loadedConfig.M0);
        Assert.Equal(config.EfConstruction, loadedConfig.EfConstruction);
        Assert.Equal(config.EfSearch, loadedConfig.EfSearch);
        Assert.Equal(config.UseHeuristic, loadedConfig.UseHeuristic);
        Assert.Equal(config.Seed, loadedConfig.Seed);

        // Verify row IDs and levels
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(graph.GetRowId(i), loaded.GetRowId(i));
            Assert.Equal(graph.GetLevel(i), loaded.GetLevel(i));
        }

        // Verify neighbor lists
        for (int i = 0; i < 50; i++)
        {
            int level = graph.GetLevel(i);
            for (int l = 0; l <= level; l++)
            {
                var original = graph.GetNeighbors(l, i);
                var restored = loaded.GetNeighbors(l, i);
                Assert.Equal(original.Length, restored.Length);
                for (int n = 0; n < original.Length; n++)
                    Assert.Equal(original[n], restored[n]);
            }
        }
    }

    [Fact]
    public void RoundTrip_SingleNode()
    {
        var graph = new HnswGraph(1, 0);
        graph.SetRowId(0, 42);
        graph.SetLevel(0, 0);
        graph.EntryPoint = 0;
        graph.MaxLevel = 0;

        var config = HnswConfig.Default;
        byte[] data = HnswSerializer.Serialize(graph, config, 3, DistanceMetric.Euclidean);

        var (loaded, _, dims, metric) = HnswSerializer.Deserialize(data);

        Assert.Equal(1, loaded.NodeCount);
        Assert.Equal(0, loaded.EntryPoint);
        Assert.Equal(42L, loaded.GetRowId(0));
        Assert.Equal(3, dims);
        Assert.Equal(DistanceMetric.Euclidean, metric);
    }

    [Fact]
    public void RoundTrip_WithNeighbors()
    {
        var graph = new HnswGraph(3, 0);
        graph.SetRowId(0, 10);
        graph.SetRowId(1, 20);
        graph.SetRowId(2, 30);
        graph.SetLevel(0, 0);
        graph.SetLevel(1, 0);
        graph.SetLevel(2, 0);
        graph.SetNeighbors(0, 0, new[] { 1, 2 });
        graph.SetNeighbors(0, 1, new[] { 0, 2 });
        graph.SetNeighbors(0, 2, new[] { 0, 1 });
        graph.EntryPoint = 0;
        graph.MaxLevel = 0;

        var config = HnswConfig.Default;
        byte[] data = HnswSerializer.Serialize(graph, config, 2, DistanceMetric.DotProduct);

        var (loaded, _, _, _) = HnswSerializer.Deserialize(data);

        var n0 = loaded.GetNeighbors(0, 0);
        Assert.Equal(2, n0.Length);
        Assert.Equal(1, n0[0]);
        Assert.Equal(2, n0[1]);
    }

    [Fact]
    public void RoundTrip_MultiLayer()
    {
        var graph = new HnswGraph(3, 2);
        graph.SetRowId(0, 100);
        graph.SetRowId(1, 200);
        graph.SetRowId(2, 300);
        graph.SetLevel(0, 2);
        graph.SetLevel(1, 0);
        graph.SetLevel(2, 1);
        graph.SetNeighbors(0, 0, new[] { 1, 2 });
        graph.SetNeighbors(1, 0, new[] { 2 });
        graph.SetNeighbors(2, 0, Array.Empty<int>());
        graph.SetNeighbors(0, 1, new[] { 0 });
        graph.SetNeighbors(0, 2, new[] { 0, 1 });
        graph.SetNeighbors(1, 2, new[] { 0 });
        graph.EntryPoint = 0;
        graph.MaxLevel = 2;

        var config = HnswConfig.Default;
        byte[] data = HnswSerializer.Serialize(graph, config, 4, DistanceMetric.Cosine);

        var (loaded, _, _, _) = HnswSerializer.Deserialize(data);

        Assert.Equal(2, loaded.GetLevel(0));
        Assert.Equal(0, loaded.GetLevel(1));
        Assert.Equal(1, loaded.GetLevel(2));

        var n_l2_0 = loaded.GetNeighbors(2, 0);
        Assert.Equal(0, n_l2_0.Length);

        var n_l1_0 = loaded.GetNeighbors(1, 0);
        Assert.Equal(1, n_l1_0.Length);
        Assert.Equal(2, n_l1_0[0]);
    }

    [Fact]
    public void Deserialize_InvalidVersion_Throws()
    {
        var data = new byte[45]; // minimum valid header + some padding
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data, 99); // bad version

        Assert.Throws<InvalidOperationException>(() => HnswSerializer.Deserialize(data));
    }

    [Fact]
    public void RoundTrip_SearchProducesSameResults()
    {
        var vectors = GenerateRandomVectors(100, 8, seed: 42);
        var rowIds = new long[100];
        for (int i = 0; i < 100; i++) rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42 };
        var resolver = new MemoryVectorResolver(vectors);
        var originalGraph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        // Search on original
        var query = new float[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        var originalResult = HnswGraphSearcher.Search(originalGraph, query, 5, 50, resolver, distanceFn, DistanceMetric.Euclidean);

        // Serialize and deserialize
        byte[] data = HnswSerializer.Serialize(originalGraph, config, 8, DistanceMetric.Euclidean);
        var (loadedGraph, _, _, _) = HnswSerializer.Deserialize(data);

        // Search on loaded
        var loadedResult = HnswGraphSearcher.Search(loadedGraph, query, 5, 50, resolver, distanceFn, DistanceMetric.Euclidean);

        // Results should be identical
        Assert.Equal(originalResult.Count, loadedResult.Count);
        for (int i = 0; i < originalResult.Count; i++)
        {
            Assert.Equal(originalResult[i].RowId, loadedResult[i].RowId);
            Assert.Equal(originalResult[i].Distance, loadedResult[i].Distance, 1e-6f);
        }
    }

    private static float[][] GenerateRandomVectors(int count, int dims, int seed)
    {
        var rng = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dims];
            for (int d = 0; d < dims; d++)
                vectors[i][d] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }
        return vectors;
    }
}
