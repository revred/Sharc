// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Cypher;
using Sharc.Graph.Model;
using Sharc.Graph.Query;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

/// <summary>
/// Defensive tests verifying hardening of Graph APIs against malicious or corrupt input.
/// </summary>
public sealed class GraphHardeningTests
{
    // ─── EstimateCapacity: integer overflow guard ───

    [Fact]
    public void EstimateCapacity_HugeFanOut_DoesNotOverflow()
    {
        // MaxFanOut = int.MaxValue with MaxDepth = 10 would overflow via repeated multiplication.
        // After hardening, it should cap at 4096 instead of wrapping to negative.
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}"))
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>());

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 10,
            MaxFanOut = int.MaxValue
        };

        // Should not throw — capacity is capped at 4096
        var result = graph.Traverse(new NodeKey(100), policy);
        Assert.True(result.Nodes.Count >= 0); // GraphResult is a value type; just verify it's valid
    }

    [Fact]
    public void EstimateCapacity_HugeFanOut_PreparedTraversal_DoesNotOverflow()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}"))
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>());

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 10,
            MaxFanOut = int.MaxValue
        };

        // PreparedTraversal computes capacity at construction time
        using var prepared = graph.PrepareTraversal(policy);
        var result = prepared.Execute(new NodeKey(100));
        Assert.True(result.Nodes.Count >= 0);
    }

    // ─── ReconstructPath: bounds check on corrupted parent pointers ───

    [Fact]
    public void Traverse_IncludePaths_ValidPaths_Succeeds()
    {
        // A -> B -> C with IncludePaths=true — verifies the hardened path code works for valid data
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}")),
            (2, RelationStoreTests_Helpers.BuildNodeRecord("n2", 200, 1, "{}")),
            (3, RelationStoreTests_Helpers.BuildNodeRecord("n3", 300, 1, "{}"))
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, RelationStoreTests_Helpers.BuildEdgeRecord("e2", 200, 10, 300, "{}"))
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 5,
            IncludePaths = true
        };

        var result = graph.Traverse(new NodeKey(100), policy);

        var nodeC = result.Nodes.First(n => n.Record.Key.Value == 300);
        Assert.NotNull(nodeC.Path);
        Assert.Equal(3, nodeC.Path!.Count);
        Assert.Equal(100, nodeC.Path[0].Value);
        Assert.Equal(300, nodeC.Path[2].Value);
    }

    // ─── GraphTraversalLayer: null graph rejected at construction ───

    [Fact]
    public void GraphTraversalLayer_NullGraph_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GraphTraversalLayer("test", null!, new NodeKey(1), default));
    }

    [Fact]
    public void GraphTraversalLayer_NullName_ThrowsArgumentNull()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>());
        multiTableReader.AddTable(3, new List<(long, byte[])>());

        using var graph = new SharcContextGraph(multiTableReader, adapter);

        Assert.Throws<ArgumentNullException>(() =>
            new GraphTraversalLayer(null!, graph, new NodeKey(1), default));
    }

    // ─── SharcContextGraph.Dispose: nulls cursors ───

    [Fact]
    public void Dispose_NullsCursors_CanDisposeMultipleTimes()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}"))
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>());

        var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        // First dispose should succeed
        graph.Dispose();
        // Second dispose should not throw (cursors already null)
        graph.Dispose();
    }

    // ─── CypherTokenizer: integer overflow ───

    [Fact]
    public void CypherTokenizer_HugeIntegerLiteral_ThrowsFormatException()
    {
        // Create a number that overflows long: 99999999999999999999 (20 digits)
        // Ref struct CypherTokenizer can't be used in lambdas, so use try/catch
        string huge = "99999999999999999999";
        var tokenizer = new CypherTokenizer(huge.AsSpan());

        try
        {
            tokenizer.Next();
            Assert.Fail("Expected FormatException for integer overflow.");
        }
        catch (FormatException)
        {
            // Expected
        }
    }

    [Fact]
    public void CypherTokenizer_MaxLongValue_DoesNotOverflow()
    {
        // long.MaxValue = 9223372036854775807 — should parse without error
        string maxLong = "9223372036854775807";
        var tokenizer = new CypherTokenizer(maxLong.AsSpan());

        var token = tokenizer.Next();
        Assert.Equal(CypherTokenKind.Integer, token.Kind);
        Assert.Equal(9223372036854775807L, token.IntegerValue);
    }

    [Fact]
    public void CypherTokenizer_NormalInteger_ParsesCorrectly()
    {
        var tokenizer = new CypherTokenizer("42".AsSpan());
        var token = tokenizer.Next();
        Assert.Equal(CypherTokenKind.Integer, token.Kind);
        Assert.Equal(42L, token.IntegerValue);
    }

    // ─── CypherParser: MaxHops overflow ───

    [Fact]
    public void CypherParser_MaxHops_NormalValue_ParsesCorrectly()
    {
        var parser = new CypherParser("MATCH (a) |> [r:CALLS*..5] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal(5, stmt.Relationship!.MaxHops);
        Assert.True(stmt.Relationship.IsVariableLength);
    }

    [Fact]
    public void CypherParser_MaxHops_HugeValue_ThrowsFormatException()
    {
        // A value exceeding int range should throw FormatException from SafeCastToInt
        // Ref struct CypherParser can't be used in lambdas, so use try/catch
        string query = "MATCH (a) |> [r:CALLS*..9999999999] |> (b) RETURN b";
        var parser = new CypherParser(query);

        try
        {
            parser.Parse();
            Assert.Fail("Expected FormatException for MaxHops overflow.");
        }
        catch (FormatException)
        {
            // Expected
        }
    }

    [Fact]
    public void CypherParser_KindAsInteger_ValidValue_Works()
    {
        var parser = new CypherParser("MATCH (a) |> [r:15] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal(15, stmt.Relationship!.Kind);
    }

    // ─── PreparedTraversal: disposed reuse ───

    [Fact]
    public void PreparedTraversal_ExecuteAfterDispose_ThrowsObjectDisposed()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}"))
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>());

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var prepared = graph.PrepareTraversal(new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 1
        });
        prepared.Dispose();

        Assert.Throws<ObjectDisposedException>(() => prepared.Execute(new NodeKey(100)));
    }

    // ─── PreparedTraversal: IncludePaths works after hardening ───

    [Fact]
    public void PreparedTraversal_IncludePaths_ValidPaths_Succeeds()
    {
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildNodeRecord("n1", 100, 1, "{}")),
            (2, RelationStoreTests_Helpers.BuildNodeRecord("n2", 200, 1, "{}")),
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>
        {
            (1, RelationStoreTests_Helpers.BuildEdgeRecord("e1", 100, 10, 200, "{}"))
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        using var prepared = graph.PrepareTraversal(new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 3,
            IncludePaths = true
        });

        var result = prepared.Execute(new NodeKey(100));
        var nodeB = result.Nodes.First(n => n.Record.Key.Value == 200);
        Assert.NotNull(nodeB.Path);
        Assert.Equal(2, nodeB.Path!.Count);
        Assert.Equal(100, nodeB.Path[0].Value);
        Assert.Equal(200, nodeB.Path[1].Value);
    }

    // ─── Cypher edge cases ───

    [Fact]
    public void CypherParser_EmptyInput_ThrowsFormatException()
    {
        var parser = new CypherParser("".AsSpan());
        try
        {
            parser.Parse();
            Assert.Fail("Expected FormatException for empty input.");
        }
        catch (FormatException)
        {
            // Expected
        }
    }

    [Fact]
    public void CypherTokenizer_EmptyInput_ReturnsEof()
    {
        var tokenizer = new CypherTokenizer("".AsSpan());
        var token = tokenizer.Next();
        Assert.Equal(CypherTokenKind.Eof, token.Kind);
    }

    [Fact]
    public void CypherParser_BackEdge_ParsesCorrectly()
    {
        var parser = new CypherParser("MATCH (a) <| [r:CALLS] <| (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal(CypherDirection.Incoming, stmt.Relationship!.Direction);
    }

    [Fact]
    public void CypherParser_BidiEdge_ParsesCorrectly()
    {
        var parser = new CypherParser("MATCH (a) <|> [r:CALLS] <|> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.Equal(CypherDirection.Both, stmt.Relationship!.Direction);
    }

    [Fact]
    public void CypherParser_StarShorthand_ParsesMaxHops()
    {
        // *3 shorthand (no ..)
        var parser = new CypherParser("MATCH (a) |> [r:CALLS*3] |> (b) RETURN b");
        var stmt = parser.Parse();

        Assert.True(stmt.Relationship!.IsVariableLength);
        Assert.Equal(3, stmt.Relationship.MaxHops);
    }

    #region Helpers

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

    #endregion
}
