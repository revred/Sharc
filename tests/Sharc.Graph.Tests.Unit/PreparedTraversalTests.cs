// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Schema;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public sealed class PreparedTraversalTests
{
    [Fact]
    public void PrepareTraversal_ValidPolicy_ReturnsNonNull()
    {
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy { Direction = TraversalDirection.Outgoing, MaxDepth = 2 };
        using var prepared = graph.PrepareTraversal(policy);

        Assert.NotNull(prepared);
    }

    [Fact]
    public void Execute_SimpleTraversal_MatchesTraverseResults()
    {
        // A -> B -> C
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 2,
            MaxFanOut = 10
        };

        // Collect results from Traverse()
        var traverseResult = graph.Traverse(new NodeKey(100), policy);

        // Collect results from PrepareTraversal().Execute()
        using var prepared = graph.PrepareTraversal(policy);
        var preparedResult = prepared.Execute(new NodeKey(100));

        Assert.Equal(traverseResult.Nodes.Count, preparedResult.Nodes.Count);

        var traverseKeys = traverseResult.Nodes.Select(n => n.Record.Key.Value).OrderBy(k => k).ToList();
        var preparedKeys = preparedResult.Nodes.Select(n => n.Record.Key.Value).OrderBy(k => k).ToList();
        Assert.Equal(traverseKeys, preparedKeys);
    }

    [Fact]
    public void Execute_WithDepthLimit_RespectsPolicy()
    {
        // A -> B -> C, depth 1 from A should find A and B only
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 1,
            MaxFanOut = 10
        };

        using var prepared = graph.PrepareTraversal(policy);
        var result = prepared.Execute(new NodeKey(100));

        Assert.Equal(2, result.Nodes.Count); // A (start) + B (depth 1)
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 100);
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 200);
    }

    [Fact]
    public void Execute_MultipleTimes_ProducesConsistentResults()
    {
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Both,
            MaxDepth = 2,
            MaxFanOut = 10
        };

        using var prepared = graph.PrepareTraversal(policy);

        for (int i = 0; i < 3; i++)
        {
            var result = prepared.Execute(new NodeKey(200));
            Assert.Equal(3, result.Nodes.Count); // A, B, C
        }
    }

    [Fact]
    public void Execute_Bidirectional_FindsBothDirections()
    {
        // A -> B -> C, start at B with Both direction
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Both,
            MaxDepth = 1,
            MaxFanOut = 10
        };

        using var prepared = graph.PrepareTraversal(policy);
        var result = prepared.Execute(new NodeKey(200));

        Assert.Equal(3, result.Nodes.Count);
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 100); // A via incoming
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 200); // B (start)
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 300); // C via outgoing
    }

    [Fact]
    public void Dispose_ThenExecute_ThrowsObjectDisposed()
    {
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policy = new TraversalPolicy { Direction = TraversalDirection.Outgoing, MaxDepth = 1 };
        var prepared = graph.PrepareTraversal(policy);
        prepared.Dispose();

        Assert.Throws<ObjectDisposedException>(() => prepared.Execute(new NodeKey(100)));
    }

    [Fact]
    public void TwoTraversals_IndependentState_NoInterference()
    {
        var (schema, adapter) = CreateSimpleGraph();
        using var graph = new SharcContextGraph(schema.reader, adapter);
        graph.Initialize(schema.schema);

        var policyOutgoing = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 2,
            MaxFanOut = 10
        };

        var policyIncoming = new TraversalPolicy
        {
            Direction = TraversalDirection.Incoming,
            MaxDepth = 2,
            MaxFanOut = 10
        };

        using var prepared1 = graph.PrepareTraversal(policyOutgoing);
        using var prepared2 = graph.PrepareTraversal(policyIncoming);

        // Execute both — they should each produce correct results independently
        var outgoingFromA = prepared1.Execute(new NodeKey(100));
        var incomingFromC = prepared2.Execute(new NodeKey(300));

        // Outgoing from A: A -> B -> C (3 nodes)
        Assert.Equal(3, outgoingFromA.Nodes.Count);
        Assert.Contains(outgoingFromA.Nodes, n => n.Record.Key.Value == 300);

        // Incoming from C: C <- B <- A (3 nodes)
        Assert.Equal(3, incomingFromC.Nodes.Count);
        Assert.Contains(incomingFromC.Nodes, n => n.Record.Key.Value == 100);
    }

    #region Graph Setup Helpers

    /// <summary>
    /// Creates a simple A -> B -> C graph for testing.
    /// </summary>
    private static (
        (MultiTableFakeReader reader, SharcSchema schema) schema,
        ISchemaAdapter adapter)
        CreateSimpleGraph()
    {
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{\"name\":\"A\"}")),
            (2, RelationStoreTests_Helpers.BuildNodeRecord("n2", 200, 1, "{\"name\":\"B\"}")),
            (3, RelationStoreTests_Helpers.BuildNodeRecord("n3", 300, 1, "{\"name\":\"C\"}"))
        });

        multiTableReader.AddTable(3, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildEdgeRecord("e1", 100, 10, 200, "{}")), // A -> B
            (2, RelationStoreTests_Helpers.BuildEdgeRecord("e2", 200, 10, 300, "{}"))  // B -> C
        });

        var edgeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "origin", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "target", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
        };

        var nodeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
        };

        var schema = new SharcSchema
        {
            Tables = new List<TableInfo>
            {
                new()
                {
                    Name = "_concepts",
                    RootPage = 2,
                    Columns = nodeCols,
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT)",
                    IsWithoutRowId = false
                },
                new()
                {
                    Name = "_relations",
                    RootPage = 3,
                    Columns = edgeCols,
                    Sql = "CREATE TABLE _relations (id TEXT, origin INTEGER, kind INTEGER, target INTEGER, data TEXT)",
                    IsWithoutRowId = false
                }
            },
            Indexes = new List<IndexInfo>(),
            Views = new List<ViewInfo>()
        };

        return ((multiTableReader, schema), new TestSchemaAdapter());
    }

    #endregion
}