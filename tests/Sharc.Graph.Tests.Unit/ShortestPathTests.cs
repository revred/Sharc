// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public sealed class ShortestPathTests
{
    [Fact]
    public void ShortestPath_DirectConnection_ReturnsTwoNodePath()
    {
        // A -> B (direct edge)
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}"))
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(200));

        Assert.NotNull(path);
        Assert.Equal(2, path.Count);
        Assert.Equal(100L, path[0].Value);
        Assert.Equal(200L, path[1].Value);
    }

    [Fact]
    public void ShortestPath_TwoHops_ReturnsThreeNodePath()
    {
        // A -> B -> C
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}"))
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(300));

        Assert.NotNull(path);
        Assert.Equal(3, path.Count);
        Assert.Equal(100L, path[0].Value);
        Assert.Equal(200L, path[1].Value);
        Assert.Equal(300L, path[2].Value);
    }

    [Fact]
    public void ShortestPath_SameNode_ReturnsSingleNodePath()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>());

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(100));

        Assert.NotNull(path);
        Assert.Single(path);
        Assert.Equal(100L, path[0].Value);
    }

    [Fact]
    public void ShortestPath_NoPath_ReturnsNull()
    {
        // A -> B, C isolated
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}"))
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(300));

        Assert.Null(path);
    }

    [Fact]
    public void ShortestPath_Diamond_ReturnsShortestRoute()
    {
        // Diamond: A -> B -> D and A -> C -> D
        // Both paths have length 3, BFS should find one of them
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}")),
            (4, BuildNodeRecord("n4", 400, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),  // A -> B
            (2, BuildEdgeRecord("e2", 100, 10, 300, "{}")),  // A -> C
            (3, BuildEdgeRecord("e3", 200, 10, 400, "{}")),  // B -> D
            (4, BuildEdgeRecord("e4", 300, 10, 400, "{}"))   // C -> D
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(400));

        Assert.NotNull(path);
        Assert.Equal(3, path.Count);
        Assert.Equal(100L, path[0].Value);
        Assert.Equal(400L, path[2].Value);
        // Middle node is either 200 or 300 (both are shortest)
        Assert.True(path[1].Value == 200L || path[1].Value == 300L);
    }

    [Fact]
    public void ShortestPath_WithMaxDepth_ReturnsNullWhenTooDeep()
    {
        // A -> B -> C -> D, maxDepth = 2 can't reach D from A
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}")),
            (4, BuildNodeRecord("n4", 400, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),
            (3, BuildEdgeRecord("e3", 300, 10, 400, "{}"))
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy { MaxDepth = 2 };
        var path = graph.ShortestPath(new NodeKey(100), new NodeKey(400), policy);

        Assert.Null(path);
    }

    [Fact]
    public void ShortestPath_WithKindFilter_UsesFilteredEdges()
    {
        // A -[10]-> B -[10]-> C, A -[20]-> C  (kind 20 is a direct shortcut)
        // With kind filter = 10, shortest path is A -> B -> C (3 nodes)
        var (schema, adapter) = CreateGraphTestSetup();
        var reader = new MultiTableFakeReader();
        reader.AddTable(2, new List<(long, byte[])>
        {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}"))
        });
        reader.AddTable(3, new List<(long, byte[])>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),  // A -[10]-> B
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),  // B -[10]-> C
            (3, BuildEdgeRecord("e3", 100, 20, 300, "{}"))   // A -[20]-> C (shortcut, different kind)
        });

        using var graph = new SharcContextGraph(reader, adapter);
        graph.Initialize(schema);

        // Without kind filter: A -> C directly (2 nodes)
        var pathNoFilter = graph.ShortestPath(new NodeKey(100), new NodeKey(300));
        Assert.NotNull(pathNoFilter);
        Assert.Equal(2, pathNoFilter.Count); // Direct via kind=20

        // With kind=10 filter: must go A -> B -> C (3 nodes)
        var policy = new TraversalPolicy { Kind = (RelationKind)10 };
        var pathFiltered = graph.ShortestPath(new NodeKey(100), new NodeKey(300), policy);
        Assert.NotNull(pathFiltered);
        Assert.Equal(3, pathFiltered.Count);
        Assert.Equal(200L, pathFiltered[1].Value);
    }

    // ── Helpers ──

    private static (SharcSchema schema, ISchemaAdapter adapter) CreateGraphTestSetup()
    {
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

        return (schema, new TestSchemaAdapter());
    }

    private static byte[] BuildEdgeRecord(string id, long origin, int kind, long target, string data)
        => RelationStoreTests_Helpers.BuildEdgeRecord(id, origin, kind, target, data);

    private static byte[] BuildNodeRecord(string id, long key, int kind, string data)
        => RelationStoreTests_Helpers.BuildNodeRecord(id, key, kind, data);
}
