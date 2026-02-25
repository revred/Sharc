// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class HnswNeighborSelectorTests
{
    [Fact]
    public void SelectSimple_TakesNearestM()
    {
        var candidates = new (float Distance, int NodeIndex)[]
        {
            (3.0f, 30),
            (1.0f, 10),
            (2.0f, 20),
            (4.0f, 40),
            (0.5f, 5)
        };

        var result = HnswNeighborSelector.SelectSimple(candidates.AsSpan(), 3);

        Assert.Equal(3, result.Length);
        Assert.Equal(5, result[0]);   // closest
        Assert.Equal(10, result[1]);
        Assert.Equal(20, result[2]);
    }

    [Fact]
    public void SelectSimple_FewerCandidatesThanM_ReturnsAll()
    {
        var candidates = new (float Distance, int NodeIndex)[]
        {
            (2.0f, 20),
            (1.0f, 10)
        };

        var result = HnswNeighborSelector.SelectSimple(candidates.AsSpan(), 5);

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void SelectHeuristic_PrefersDistantDiverseNeighbors()
    {
        // Target at origin, three candidates:
        // A at (1,0) dist=1.0  — nearest
        // B at (1.1,0) dist=1.1 — very close to A (redundant)
        // C at (0,2) dist=2.0  — far from A (diverse)
        // With M=2, heuristic should pick A (nearest) and C (diverse), not A and B (redundant)
        var vectors = new float[][]
        {
            new[] { 0f, 0f },  // target (not a node)
            new[] { 1f, 0f },  // A (node 0)
            new[] { 1.1f, 0f }, // B (node 1)
            new[] { 0f, 2f },   // C (node 2)
        };

        // Shift: node 0 = vectors[1], node 1 = vectors[2], node 2 = vectors[3]
        var resolver = new MemoryVectorResolver(new[] { vectors[1], vectors[2], vectors[3] });
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var target = vectors[0].AsSpan();
        var candidates = new (float Distance, int NodeIndex)[]
        {
            (VectorDistanceFunctions.EuclideanDistance(target, vectors[1]), 0), // A
            (VectorDistanceFunctions.EuclideanDistance(target, vectors[2]), 1), // B
            (VectorDistanceFunctions.EuclideanDistance(target, vectors[3]), 2), // C
        };

        var result = HnswNeighborSelector.SelectHeuristic(
            target, candidates.AsSpan(), 2, resolver, distanceFn);

        Assert.Equal(2, result.Length);
        Assert.Contains(0, result); // A should be selected (nearest)
        Assert.Contains(2, result); // C should be selected (diverse)
    }

    [Fact]
    public void SelectHeuristic_EmptyCandidates_ReturnsEmpty()
    {
        var target = new float[] { 1f, 0f }.AsSpan();
        var resolver = new MemoryVectorResolver(Array.Empty<float[]>());
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Cosine);

        var candidates = Array.Empty<(float, int)>();
        var result = HnswNeighborSelector.SelectHeuristic(
            target, candidates.AsSpan(), 5, resolver, distanceFn);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectSimple_SingleCandidate_ReturnsSingle()
    {
        var candidates = new (float Distance, int NodeIndex)[] { (1.0f, 42) };

        var result = HnswNeighborSelector.SelectSimple(candidates.AsSpan(), 5);

        Assert.Single(result);
        Assert.Equal(42, result[0]);
    }

    [Fact]
    public void SelectHeuristic_AllCandidatesSelected_WhenFarApart()
    {
        // Four vectors far apart in different quadrants — all should be selected
        var vectors = new float[][]
        {
            new[] { 10f, 0f },
            new[] { -10f, 0f },
            new[] { 0f, 10f },
            new[] { 0f, -10f },
        };

        var resolver = new MemoryVectorResolver(vectors);
        var distanceFn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);

        var target = new float[] { 0f, 0f }.AsSpan();
        var candidates = new (float Distance, int NodeIndex)[4];
        for (int i = 0; i < 4; i++)
            candidates[i] = (VectorDistanceFunctions.EuclideanDistance(target, vectors[i]), i);

        var result = HnswNeighborSelector.SelectHeuristic(
            target, candidates.AsSpan(), 4, resolver, distanceFn);

        Assert.Equal(4, result.Length);
    }
}
