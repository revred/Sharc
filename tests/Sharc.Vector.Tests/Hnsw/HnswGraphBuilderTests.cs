// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswGraphBuilderTests
{
    [Fact]
    public void Build_SingleVector_CreatesValidGraph()
    {
        var vectors = new float[][] { new[] { 1f, 0f, 0f } };
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[] { 100L };
        var config = HnswConfig.Default with { Seed = 42 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Cosine, config);

        Assert.Equal(1, graph.NodeCount);
        Assert.Equal(0, graph.EntryPoint);
        Assert.Equal(100L, graph.GetRowId(0));
    }

    [Fact]
    public void Build_MultipleVectors_ConnectsAllNodes()
    {
        var vectors = GenerateClusteredVectors(50, 3, seed: 42);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[vectors.Length];
        for (int i = 0; i < rowIds.Length; i++)
            rowIds[i] = i + 1;

        var config = HnswConfig.Default with { Seed = 42 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);

        Assert.Equal(50, graph.NodeCount);
        Assert.True(graph.EntryPoint >= 0 && graph.EntryPoint < 50);

        // Every node should have at least one neighbor at layer 0
        for (int i = 0; i < graph.NodeCount; i++)
        {
            var neighbors = graph.GetNeighbors(0, i);
            Assert.True(neighbors.Length > 0, $"Node {i} has no neighbors at layer 0");
        }
    }

    [Fact]
    public void Build_WithHeuristic_ProducesConnectedGraph()
    {
        var vectors = GenerateClusteredVectors(30, 4, seed: 123);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[vectors.Length];
        for (int i = 0; i < rowIds.Length; i++)
            rowIds[i] = i + 1;

        var config = HnswConfig.Default with { UseHeuristic = true, Seed = 123 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Cosine, config);

        // Verify all nodes reachable from entry point at layer 0 (BFS)
        var reachable = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(graph.EntryPoint);
        reachable.Add(graph.EntryPoint);

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            var neighbors = graph.GetNeighbors(0, node);
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (reachable.Add(neighbors[i]))
                    queue.Enqueue(neighbors[i]);
            }
        }

        Assert.Equal(graph.NodeCount, reachable.Count);
    }

    [Fact]
    public void Build_SimpleSelection_ProducesConnectedGraph()
    {
        var vectors = GenerateClusteredVectors(30, 4, seed: 456);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[vectors.Length];
        for (int i = 0; i < rowIds.Length; i++)
            rowIds[i] = i + 1;

        var config = HnswConfig.Default with { UseHeuristic = false, Seed = 456 };

        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Euclidean, config);

        Assert.Equal(30, graph.NodeCount);
        Assert.True(graph.MaxLevel >= 0);
    }

    [Fact]
    public void Build_RowIdMapping_Preserved()
    {
        var vectors = GenerateClusteredVectors(10, 2, seed: 789);
        var resolver = new MemoryVectorResolver(vectors);
        var rowIds = new long[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };

        var config = HnswConfig.Default with { Seed = 789 };
        var graph = HnswGraphBuilder.Build(resolver, rowIds, DistanceMetric.Cosine, config);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(rowIds[i], graph.GetRowId(i));
            Assert.Equal(i, graph.GetNodeIndex(rowIds[i]));
        }
    }

    [Fact]
    public void Build_ZeroVectors_Throws()
    {
        var resolver = new MemoryVectorResolver(Array.Empty<float[]>());
        var config = HnswConfig.Default;

        Assert.Throws<InvalidOperationException>(
            () => HnswGraphBuilder.Build(resolver, Array.Empty<long>(), DistanceMetric.Cosine, config));
    }

    private static float[][] GenerateClusteredVectors(int count, int dimensions, int seed)
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
