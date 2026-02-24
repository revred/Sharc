// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Algorithms;
using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Algorithms;

public sealed class PageRankTests
{
    [Fact]
    public void PageRank_SingleNode_ReturnsScoreOfOne()
    {
        var nodes = new List<NodeKey> { new(100) };
        var graph = new FakeGraphBuilder();

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        Assert.Single(result);
        Assert.Equal(100L, result[0].Key.Value);
        Assert.Equal(1.0, result[0].Score, 3);
    }

    [Fact]
    public void PageRank_TwoNodesCycle_ReturnsEqualScores()
    {
        // A <-> B
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 100);
        var nodes = new List<NodeKey> { new(100), new(200) };

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        Assert.Equal(2, result.Count);
        Assert.Equal(result[0].Score, result[1].Score, 3);
    }

    [Fact]
    public void PageRank_LinearChain_HigherScoreAtEnd()
    {
        // A -> B -> C (C receives rank from A and B transitively)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 300);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        var scoreA = result.First(r => r.Key.Value == 100).Score;
        var scoreC = result.First(r => r.Key.Value == 300).Score;
        Assert.True(scoreC > scoreA, "End of chain should have higher rank than start");
    }

    [Fact]
    public void PageRank_StarTopology_CenterHasHighestScore()
    {
        // B,C,D all point to A
        var graph = new FakeGraphBuilder()
            .AddEdge(200, 100)
            .AddEdge(300, 100)
            .AddEdge(400, 100);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        // First result (highest score) should be node 100
        Assert.Equal(100L, result[0].Key.Value);
    }

    [Fact]
    public void PageRank_CustomDamping_AffectsDistribution()
    {
        var graph = new FakeGraphBuilder()
            .AddEdge(200, 100)
            .AddEdge(300, 100);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var highDamp = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing,
            new PageRankOptions { DampingFactor = 0.99 });
        var lowDamp = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing,
            new PageRankOptions { DampingFactor = 0.50 });

        var highScore = highDamp.First(r => r.Key.Value == 100).Score;
        var lowScore = lowDamp.First(r => r.Key.Value == 100).Score;
        // Higher damping → rank flows more → center gets higher score
        Assert.True(highScore > lowScore);
    }

    [Fact]
    public void PageRank_EmptyGraph_ReturnsEmptyList()
    {
        var result = GraphAlgorithms.PageRank(
            Array.Empty<NodeKey>(),
            _ => new FakeEdgeCursor(0, null));

        Assert.Empty(result);
    }

    [Fact]
    public void PageRank_DisconnectedComponents_EachConverges()
    {
        // A -> B, C -> D (two disconnected pairs)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(300, 400);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        Assert.Equal(4, result.Count);
        // All scores should be positive
        foreach (var r in result)
            Assert.True(r.Score > 0);
    }

    [Fact]
    public void PageRank_MaxIterationsReached_ReturnsCurrentScores()
    {
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 100);
        var nodes = new List<NodeKey> { new(100), new(200) };

        // Only 1 iteration — won't converge but should still return results
        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing,
            new PageRankOptions { MaxIterations = 1 });

        Assert.Equal(2, result.Count);
        foreach (var r in result)
            Assert.True(r.Score > 0);
    }

    [Fact]
    public void PageRank_KindFilter_OnlyCountsFilteredEdges()
    {
        // A -[10]-> B, C -[20]-> B (only kind=10 should count)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200, kind: 10)
            .AddEdge(300, 200, kind: 20);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var resultAll = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);
        var resultFiltered = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing,
            new PageRankOptions { Kind = (RelationKind)10 });

        // With filter, B gets rank only from A, not from C
        var bAll = resultAll.First(r => r.Key.Value == 200).Score;
        var bFiltered = resultFiltered.First(r => r.Key.Value == 200).Score;
        Assert.True(bAll > bFiltered, "Filtered PageRank should give B a lower score");
    }

    [Fact]
    public void PageRank_ResultsSortedDescending()
    {
        var graph = new FakeGraphBuilder()
            .AddEdge(200, 100)
            .AddEdge(300, 100)
            .AddEdge(400, 100);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.PageRank(nodes, graph.CreateOutgoing);

        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Score >= result[i].Score);
    }
}
