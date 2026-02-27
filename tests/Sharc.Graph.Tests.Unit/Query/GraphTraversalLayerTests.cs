// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;
using Sharc.Graph.Model;
using Sharc.Graph.Query;
using Sharc.Graph.Schema;
using Sharc.Views;
using Xunit;

namespace Sharc.Graph.Tests.Unit.Query;

public sealed class GraphTraversalLayerTests
{
    [Fact]
    public void Layer_Name_ReturnsConfiguredName()
    {
        using var graph = CreateMinimalGraph();
        var layer = new GraphTraversalLayer("reachable", graph, new NodeKey(1), default);
        Assert.Equal("reachable", layer.Name);
    }

    [Fact]
    public void Layer_Strategy_IsEager()
    {
        using var graph = CreateMinimalGraph();
        var layer = new GraphTraversalLayer("test", graph, new NodeKey(1), default);
        Assert.Equal(MaterializationStrategy.Eager, layer.Strategy);
    }

    [Fact]
    public void Layer_NullName_ThrowsArgumentNullException()
    {
        using var graph = CreateMinimalGraph();
        Assert.Throws<ArgumentNullException>(() =>
            new GraphTraversalLayer(null!, graph, new NodeKey(1), default));
    }

    [Fact]
    public void Layer_NullGraph_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GraphTraversalLayer("test", null!, new NodeKey(1), default));
    }

    private static SharcContextGraph CreateMinimalGraph()
    {
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>());
        multiTableReader.AddTable(3, new List<(long, byte[])>());
        var adapter = new TestSchemaAdapter();

        var schema = new SharcSchema
        {
            Tables = new List<TableInfo>
            {
                new()
                {
                    Name = "_concepts", RootPage = 2,
                    Columns = new List<ColumnInfo>
                    {
                        new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
                        new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
                        new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
                        new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
                    },
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT)",
                    IsWithoutRowId = false
                },
                new()
                {
                    Name = "_relations", RootPage = 3,
                    Columns = new List<ColumnInfo>
                    {
                        new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
                        new() { Name = "origin", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
                        new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
                        new() { Name = "target", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
                        new() { Name = "data", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
                    },
                    Sql = "CREATE TABLE _relations (id TEXT, origin INTEGER, kind INTEGER, target INTEGER, data TEXT)",
                    IsWithoutRowId = false
                }
            },
            Indexes = new List<IndexInfo>(),
            Views = new List<ViewInfo>()
        };

        var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);
        return graph;
    }
}
