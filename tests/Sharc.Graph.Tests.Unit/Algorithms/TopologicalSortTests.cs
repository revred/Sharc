// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Algorithms;
using Sharc.Graph.Model;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Algorithms;

public sealed class TopologicalSortTests
{
    [Fact]
    public void TopologicalSort_LinearChain_ReturnsCorrectOrder()
    {
        // A -> B -> C
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 300);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing);

        // A must come before B, B before C
        int posA = result.ToList().FindIndex(k => k.Value == 100);
        int posB = result.ToList().FindIndex(k => k.Value == 200);
        int posC = result.ToList().FindIndex(k => k.Value == 300);
        Assert.True(posA < posB);
        Assert.True(posB < posC);
    }

    [Fact]
    public void TopologicalSort_Diamond_RespectsDependencies()
    {
        // A -> B, A -> C, B -> D, C -> D
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(100, 300)
            .AddEdge(200, 400)
            .AddEdge(300, 400);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing);

        Assert.Equal(4, result.Count);
        var list = result.ToList();
        int posA = list.FindIndex(k => k.Value == 100);
        int posD = list.FindIndex(k => k.Value == 400);
        Assert.True(posA < posD); // A before D
    }

    [Fact]
    public void TopologicalSort_SingleNode_ReturnsThatNode()
    {
        var nodes = new List<NodeKey> { new(100) };
        var graph = new FakeGraphBuilder();

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing);

        Assert.Single(result);
        Assert.Equal(100L, result[0].Value);
    }

    [Fact]
    public void TopologicalSort_EmptyGraph_ReturnsEmptyList()
    {
        var result = GraphAlgorithms.TopologicalSort(
            Array.Empty<NodeKey>(),
            _ => new FakeEdgeCursor(0, null));

        Assert.Empty(result);
    }

    [Fact]
    public void TopologicalSort_CycleDetected_ThrowsInvalidOperationException()
    {
        // A -> B -> C -> A (cycle)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(200, 300)
            .AddEdge(300, 100);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        Assert.Throws<InvalidOperationException>(() =>
            GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing));
    }

    [Fact]
    public void TopologicalSort_DisconnectedDAG_IncludesAllNodes()
    {
        // A -> B, C -> D (two disconnected chains)
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200)
            .AddEdge(300, 400);
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400) };

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing);

        Assert.Equal(4, result.Count);
        var set = new HashSet<long>(result.Select(k => k.Value));
        Assert.Contains(100L, set);
        Assert.Contains(200L, set);
        Assert.Contains(300L, set);
        Assert.Contains(400L, set);
    }

    [Fact]
    public void TopologicalSort_KindFilter_OnlyFollowsMatchingEdges()
    {
        // A -[10]-> B -[10]-> C, A -[20]-> C (kind 20 creates shortcut)
        // With kind=10 filter: A->B->C, C depends on B depends on A
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 200, kind: 10)
            .AddEdge(200, 300, kind: 10)
            .AddEdge(100, 300, kind: 20);
        var nodes = new List<NodeKey> { new(100), new(200), new(300) };

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing, kind: (RelationKind)10);

        var list = result.ToList();
        int posA = list.FindIndex(k => k.Value == 100);
        int posB = list.FindIndex(k => k.Value == 200);
        int posC = list.FindIndex(k => k.Value == 300);
        Assert.True(posA < posB);
        Assert.True(posB < posC);
    }

    [Fact]
    public void TopologicalSort_DependencyOrder_AllDependenciesBeforeDependents()
    {
        // E depends on C,D; C depends on A,B; D depends on B
        var graph = new FakeGraphBuilder()
            .AddEdge(100, 300) // A -> C
            .AddEdge(200, 300) // B -> C
            .AddEdge(200, 400) // B -> D
            .AddEdge(300, 500) // C -> E
            .AddEdge(400, 500); // D -> E
        var nodes = new List<NodeKey> { new(100), new(200), new(300), new(400), new(500) };

        var result = GraphAlgorithms.TopologicalSort(nodes, graph.CreateOutgoing);

        var list = result.ToList();
        // Verify: for each edge u->v, u appears before v
        Assert.True(list.FindIndex(k => k.Value == 100) < list.FindIndex(k => k.Value == 300));
        Assert.True(list.FindIndex(k => k.Value == 200) < list.FindIndex(k => k.Value == 300));
        Assert.True(list.FindIndex(k => k.Value == 200) < list.FindIndex(k => k.Value == 400));
        Assert.True(list.FindIndex(k => k.Value == 300) < list.FindIndex(k => k.Value == 500));
        Assert.True(list.FindIndex(k => k.Value == 400) < list.FindIndex(k => k.Value == 500));
    }
}
