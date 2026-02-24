// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Algorithms;
using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Algorithms;

public sealed class DegreeCentralityTests
{
    [Fact]
    public void DegreeCentrality_SingleNodeNoEdges_ZeroDegrees()
    {
        var nodes = new List<NodeKey> { new(100) };
        var graph = new FakeGraphBuilder();

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        Assert.Single(result);
        Assert.Equal(100L, result[0].Key.Value);
        Assert.Equal(0, result[0].InDegree);
        Assert.Equal(0, result[0].OutDegree);
        Assert.Equal(0, result[0].TotalDegree);
    }

    [Fact]
    public void DegreeCentrality_LinearChain_CorrectInOutDegrees()
    {
        // A -> B -> C
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 300);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        var a = result.First(r => r.Key.Value == 100);
        var b = result.First(r => r.Key.Value == 200);
        var c = result.First(r => r.Key.Value == 300);

        Assert.Equal(0, a.InDegree);
        Assert.Equal(1, a.OutDegree);
        Assert.Equal(1, b.InDegree);
        Assert.Equal(1, b.OutDegree);
        Assert.Equal(1, c.InDegree);
        Assert.Equal(0, c.OutDegree);
    }

    [Fact]
    public void DegreeCentrality_StarTopology_CenterHasHighestTotal()
    {
        // Center(100) -> 200, 300, 400
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(100, 300)
            .AddEdge(100, 400);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        // Sorted by TotalDegree descending — center first
        Assert.Equal(100L, result[0].Key.Value);
        Assert.Equal(3, result[0].OutDegree);
        Assert.Equal(3, result[0].TotalDegree);
    }

    [Fact]
    public void DegreeCentrality_EmptyGraph_ReturnsEmptyList()
    {
        var result = GraphAlgorithms.DegreeCentrality(
            Array.Empty<NodeKey>(),
            _ => new FakeEdgeCursor(0, null),
            _ => new FakeEdgeCursor(0, null));

        Assert.Empty(result);
    }

    [Fact]
    public void DegreeCentrality_KindFilter_OnlyCountsMatchingEdges()
    {
        // A -[10]-> B, A -[20]-> C
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200, kind: 10)
            .AddEdge(100, 300, kind: 20);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming, kind: (RelationKind)10);

        var a = result.First(r => r.Key.Value == 100);
        Assert.Equal(1, a.OutDegree); // Only kind=10 edge counted
    }

    [Fact]
    public void DegreeCentrality_ResultsSortedByTotalDescending()
    {
        // A -> B, A -> C, B -> C (C has highest total: 2 in, 0 out)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(100, 300)
            .AddEdge(200, 300);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        // C(300) has total=2, A(100) has total=2, B(200) has total=2 — all equal
        // When equal, order is stable (original node order)
        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].TotalDegree >= result[i].TotalDegree);
    }

    [Fact]
    public void DegreeCentrality_MultipleEdgesSameNodes_CountsEach()
    {
        // A -> B (kind=10), A -> B (kind=20)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200, kind: 10)
            .AddEdge(100, 200, kind: 20);
        var nodes = new List<NodeKey> { new(100), new(200) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        var a = result.First(r => r.Key.Value == 100);
        var b = result.First(r => r.Key.Value == 200);
        Assert.Equal(2, a.OutDegree);
        Assert.Equal(2, b.InDegree);
    }

    [Fact]
    public void DegreeCentrality_BothDirections_CountedSeparately()
    {
        // A -> B, B -> A (bidirectional)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 100);
        var nodes = new List<NodeKey> { new(100), new(200) };

        var result = GraphAlgorithms.DegreeCentrality(
            nodes, graph.CreateOutgoing, graph.CreateIncoming);

        var a = result.First(r => r.Key.Value == 100);
        Assert.Equal(1, a.InDegree);
        Assert.Equal(1, a.OutDegree);
        Assert.Equal(2, a.TotalDegree);
    }
}
