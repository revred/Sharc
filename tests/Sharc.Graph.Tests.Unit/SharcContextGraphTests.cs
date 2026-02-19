using System.Text;
using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;
using Xunit;

namespace Sharc.Graph.Tests.Unit;

public class SharcContextGraphTests
{
    [Fact]
    public void Traverse_Bidirectional_FindsBothUpstreamAndDownstream()
    {
        // Setup: A -> B -> C
        // A calls B (Outgoing)
        // B is called by A (Incoming)
        // B defines C (Outgoing)
        // C is defined by B (Incoming)

        // Traversal starting at B with Direction=Both should find A (incoming) and C (outgoing).

        var (schema, adapter) = CreateGraphTestSetup();
        var rows = new List<(long rowId, byte[] payload)>
        {
            // Nodes (Concepts)
            (1, BuildNodeRecord("n1", 100, 1, "{\"name\":\"A\"}")), // A
            (2, BuildNodeRecord("n2", 200, 1, "{\"name\":\"B\"}")), // B
            (3, BuildNodeRecord("n3", 300, 1, "{\"name\":\"C\"}")), // C
            
            // Edges (Relations) - Table ID 3
            // Note: FakeBTreeReader uses rowId, which is unique per table. currently shared list.
            // We need separate readers for separate tables or a smarter fake.
            // But SharcContextGraph takes ONE reader for the whole DB.
            // So reader needs to handle different root pages.
        };

        // Enhanced Fake to route by RootPage
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{\"name\":\"A\"}")),
            (2, BuildNodeRecord("n2", 200, 1, "{\"name\":\"B\"}")),
            (3, BuildNodeRecord("n3", 300, 1, "{\"name\":\"C\"}"))
        });

        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")), // A -> B
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}"))  // B -> C
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        // Start at B (200)
        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Both,
            MaxDepth = 1,
            MaxFanOut = 10
        };

        var result = graph.Traverse(new NodeKey(200), policy);

        // Should find B (start), A (incoming), C (outgoing)
        Assert.Equal(3, result.Nodes.Count);
        
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 200);
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 100); // Found via incoming
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 300); // Found via outgoing
    }

    [Fact]
    public void Traverse_Incoming_only_FindsUpstream()
    {
        // A -> B -> C
        // Start at C (300). Incoming should find B. B incoming finds A.

        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{\"name\":\"A\"}")),
            (2, BuildNodeRecord("n2", 200, 1, "{\"name\":\"B\"}")),
            (3, BuildNodeRecord("n3", 300, 1, "{\"name\":\"C\"}"))
        });

        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")), // A -> B
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}"))  // B -> C
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Incoming,
            MaxDepth = 2
        };

        var result = graph.Traverse(new NodeKey(300), policy);

        Assert.Equal(3, result.Nodes.Count); // C, B, A
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 100);
    }


    [Fact]
    public void Traverse_TargetTypeFilter_OnlyReturnsMatchingType()
    {
        // A(type=1) -> B(type=2) -> C(type=1)
        // Traverse from A with TargetTypeFilter=1 → should return A and C only
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),  // A type=1
            (2, BuildNodeRecord("n2", 200, 2, "{}")),  // B type=2
            (3, BuildNodeRecord("n3", 300, 1, "{}")),  // C type=1
        });
        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")), // A -> B
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")), // B -> C
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 3,
            TargetTypeFilter = 1
        };

        var result = graph.Traverse(new NodeKey(100), policy);

        // BFS discovers A, B, C. Phase 2 filters B (type=2) out.
        Assert.Equal(2, result.Nodes.Count);
        Assert.All(result.Nodes, n => Assert.Equal(1, n.Record.TypeId));
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 100);
        Assert.Contains(result.Nodes, n => n.Record.Key.Value == 300);
    }

    [Fact]
    public void Traverse_MaxTokens_StopsWhenBudgetExhausted()
    {
        // 4 nodes each with 100 tokens. Budget of 250 → should return 2 (200 <= 250), stop at 3rd (300 > 250).
        var (schema, adapter) = CreateTokenGraphSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecordWithTokens("n1", 100, 1, "{}", 100)),
            (2, BuildNodeRecordWithTokens("n2", 200, 1, "{}", 100)),
            (3, BuildNodeRecordWithTokens("n3", 300, 1, "{}", 100)),
            (4, BuildNodeRecordWithTokens("n4", 400, 1, "{}", 100)),
        });
        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),
            (3, BuildEdgeRecord("e3", 300, 10, 400, "{}")),
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 5,
            MaxTokens = 250
        };

        var result = graph.Traverse(new NodeKey(100), policy);

        // 100 (cumulative 100) + 200 (cumulative 200) are within budget.
        // 300 (cumulative 300) exceeds 250 → stop.
        Assert.Equal(2, result.Nodes.Count);
    }

    [Fact]
    public void Traverse_Timeout_ReturnsPartialResults()
    {
        // With a generous timeout, all nodes should be returned (no early exit).
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}")),
        });
        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 5,
            Timeout = TimeSpan.FromSeconds(30)
        };

        var result = graph.Traverse(new NodeKey(100), policy);

        Assert.Equal(3, result.Nodes.Count);
    }

    [Fact]
    public void Traverse_StopAtKey_StopsAtTargetNode()
    {
        // A -> B -> C -> D
        // StopAtKey = C (300) → should return A, B, C but not D.
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}")),
            (4, BuildNodeRecord("n4", 400, 1, "{}")),
        });
        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),
            (3, BuildEdgeRecord("e3", 300, 10, 400, "{}")),
        });

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var policy = new TraversalPolicy
        {
            Direction = TraversalDirection.Outgoing,
            MaxDepth = 5,
            StopAtKey = new NodeKey(300)
        };

        var result = graph.Traverse(new NodeKey(100), policy);

        // A(depth 0), B(depth 1), C(depth 2) — BFS stops when C is dequeued.
        Assert.Equal(3, result.Nodes.Count);
        Assert.DoesNotContain(result.Nodes, n => n.Record.Key.Value == 400);
    }

    [Fact]
    public void Traverse_IncludePaths_ReturnsCorrectPaths()
    {
        // A -> B -> C
        // With IncludePaths=true, C's path should be [A, B, C].
        var (schema, adapter) = CreateGraphTestSetup();
        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, BuildNodeRecord("n1", 100, 1, "{}")),
            (2, BuildNodeRecord("n2", 200, 1, "{}")),
            (3, BuildNodeRecord("n3", 300, 1, "{}")),
        });
        multiTableReader.AddTable(3, new List<(long, byte[])> {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 200, 10, 300, "{}")),
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

        Assert.Equal(3, result.Nodes.Count);

        var nodeC = result.Nodes.First(n => n.Record.Key.Value == 300);
        Assert.NotNull(nodeC.Path);
        Assert.Equal(3, nodeC.Path!.Count); // [A, B, C]
        Assert.Equal(100, nodeC.Path[0].Value);
        Assert.Equal(200, nodeC.Path[1].Value);
        Assert.Equal(300, nodeC.Path[2].Value);
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
            Indexes = new List<IndexInfo>(), // No indexes for table scan test
            Views = new List<ViewInfo>()
        };

        // We need to inject this schema into SchemaLoader? 
        // SharcContextGraph uses SchemaLoader which reads from DB.
        // We need to mock SchemaLoader or pre-populate schema table in FakeReader.
        // Or simply: conceptStore and relationStore use the schema passed?
        // Ah, SharcContextGraph uses SchemaLoader internally:
        // var loader = new SchemaLoader(_reader);
        // var schemaInfo = loader.Load();
        
        // This makes it hard to test without writing a valid SQLite schema page (page 1).
        // OR we can make a SharcContextGraph constructor used for testing that accepts schema directly?
        // But SharcContextGraph is sealed.

        // Workaround: We will mock Page 1 in the fake reader?
        // Too complex.
        
        // Alternative: Use Reflection to set _initialized and the stores?
        // Or, assume SchemaLoader just reads `sqlite_schema` table which is RootPage 1.
        // We can populate RootPage 1 with the schema definitions!
        
        return (schema, new TestSchemaAdapter());
    }

    private static (SharcSchema schema, ISchemaAdapter adapter) CreateTokenGraphSetup()
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
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "tokens", Ordinal = 4, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false }
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
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, tokens INTEGER)",
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

        return (schema, new TokenSchemaAdapter());
    }

    // Creating a Mock SchemaLoader is blocked by strict internal chain.
    // Let's populate page 1 in the MultiTableFakeReader!
    // But encoding SQLite schema records is tedious.

    // Better approach: Test RelationStore directly (done).
    // Test SharcContextGraph logic:
    // We can rely on RelationStore unit tests for "getting edges".
    // For traversal logic, we need SharcContextGraph to be workable.
    
    // Let's use `SchemaLoader` but mock the `sqlite_schema` table content in FakeReader.
    // The SchemaLoader reads from table "sqlite_schema" which is always at root page 1.
    // If we put the schema records in page 1's mock data, it should work.

    #endregion

    #region Record Builders (Copied from RelationStoreTests)
    private static byte[] BuildEdgeRecord(string id, long origin, int kind, long target, string data)
    {
        // ... (Simplified version for brevity, assuming copier works)
        // Actually, we need the real implementation.
        // Let's reuse the one from RelationStoreTests via copy-paste for now.
        return RelationStoreTests_Helpers.BuildEdgeRecord(id, origin, kind, target, data);
    }

    private static byte[] BuildNodeRecord(string id, long key, int kind, string data) {
        return RelationStoreTests_Helpers.BuildNodeRecord(id, key, kind, data);
    }

    private static byte[] BuildNodeRecordWithTokens(string id, long key, int kind, string data, int tokens) {
        return RelationStoreTests_Helpers.BuildNodeRecordWithTokens(id, key, kind, data, tokens);
    }
    #endregion
}

// Helper class to expose builder methods
public static class RelationStoreTests_Helpers
{
    public static byte[] BuildEdgeRecord(string id, long origin, int kind, long target, string data)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        long idSt = idBytes.Length * 2 + 13;
        long dataSt = dataBytes.Length * 2 + 13;
        var originCol = EncodeInteger(origin);
        var kindCol = EncodeInteger(kind);
        var targetCol = EncodeInteger(target);

        return BuildRecord(
            (idSt, idBytes),
            (originCol.serialType, originCol.body),
            (kindCol.serialType, kindCol.body),
            (targetCol.serialType, targetCol.body),
            (dataSt, dataBytes));
    }

    public static byte[] BuildNodeRecord(string id, long key, int kind, string data)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        long idSt = idBytes.Length * 2 + 13;
        long dataSt = dataBytes.Length * 2 + 13;
        var keyCol = EncodeInteger(key);
        var kindCol = EncodeInteger(kind);

        return BuildRecord(
            (idSt, idBytes),
            (keyCol.serialType, keyCol.body),
            (kindCol.serialType, kindCol.body),
            (dataSt, dataBytes));
    }

    public static byte[] BuildNodeRecordWithTokens(string id, long key, int kind, string data, int tokens)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        long idSt = idBytes.Length * 2 + 13;
        long dataSt = dataBytes.Length * 2 + 13;
        var keyCol = EncodeInteger(key);
        var kindCol = EncodeInteger(kind);
        var tokensCol = EncodeInteger(tokens);

        return BuildRecord(
            (idSt, idBytes),
            (keyCol.serialType, keyCol.body),
            (kindCol.serialType, kindCol.body),
            (dataSt, dataBytes),
            (tokensCol.serialType, tokensCol.body));
    }

    private static (long serialType, byte[] body) EncodeInteger(long value)
    {
        if (value == 0) return (8, []);
        if (value == 1) return (9, []);
        if (value >= -128 && value <= 127) return (1, [(byte)(value & 0xFF)]);
        if (value >= -32768 && value <= 32767)
        {
            var buf = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buf, (short)value);
            return (2, buf);
        }
        {
            var buf = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, (int)value);
            return (4, buf);
        }
    }

    private static byte[] BuildRecord(params (long serialType, byte[] data)[] columns)
    {
        var stBuffer = new byte[columns.Length * 9];
        int stLen = 0;
        foreach (var (st, _) in columns)
            stLen += VarintDecoder.Write(stBuffer.AsSpan(stLen), st);

        int headerSizeVarintLen = VarintDecoder.GetEncodedLength(stLen + 1);
        int totalHeaderSize = headerSizeVarintLen + stLen;
        if (VarintDecoder.GetEncodedLength(totalHeaderSize) != headerSizeVarintLen)
        {
            headerSizeVarintLen = VarintDecoder.GetEncodedLength(totalHeaderSize + 1);
            totalHeaderSize = headerSizeVarintLen + stLen;
        }

        int bodySize = 0;
        foreach (var (_, data) in columns)
            bodySize += data.Length;

        var result = new byte[totalHeaderSize + bodySize];
        int offset = VarintDecoder.Write(result, totalHeaderSize);
        foreach (var (st, _) in columns)
            offset += VarintDecoder.Write(result.AsSpan(offset), st);
        foreach (var (_, data) in columns)
        {
            data.CopyTo(result, offset);
            offset += data.Length;
        }
        return result;
    }
}

internal sealed class MultiTableFakeReader : IBTreeReader
{
    private readonly Dictionary<uint, List<(long rowId, byte[] payload)>> _tables = new();

    public void AddTable(uint rootPage, List<(long, byte[])> rows) => _tables[rootPage] = rows;

    public IBTreeCursor CreateCursor(uint rootPage)
    {
        if (_tables.TryGetValue(rootPage, out var rows))
            return new FakeBTreeCursor(rows);
        return new FakeBTreeCursor(new List<(long, byte[])>());
    }

    public IIndexBTreeCursor CreateIndexCursor(uint rootPage) => throw new NotSupportedException();
}

internal sealed class FakeBTreeCursor : IBTreeCursor
{
    private readonly List<(long rowId, byte[] payload)> _rows;
    private int _index = -1;
    public FakeBTreeCursor(List<(long rowId, byte[] payload)> rows) => _rows = rows;
    public long RowId => _index >= 0 && _index < _rows.Count ? _rows[_index].rowId : 0;
    public ReadOnlySpan<byte> Payload => _index >= 0 && _index < _rows.Count ? _rows[_index].payload : default;
    public int PayloadSize => _index >= 0 && _index < _rows.Count ? _rows[_index].payload.Length : 0;
    public bool MoveNext() { _index++; return _index < _rows.Count; }
    public bool MoveLast() { if (_rows.Count == 0) return false; _index = _rows.Count - 1; return true; }
    public bool Seek(long rowId) 
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].rowId == rowId)
            {
                _index = i;
                return true;
            }
        }
        return false;
    }
    public void Reset() { _index = -1; }
    public void Dispose() { }
}

internal sealed class TestSchemaAdapter : ISchemaAdapter
{
    public string NodeTableName => "_concepts";
    public string EdgeTableName => "_relations";
    public string? EdgeHistoryTableName => null;
    public string? MetaTableName => null;
    public string NodeIdColumn => "id";
    public string NodeKeyColumn => "key";
    public string NodeTypeColumn => "kind";
    public string NodeDataColumn => "data";
    public string? NodeCvnColumn => null;
    public string? NodeLvnColumn => null;
    public string? NodeSyncColumn => null;
    public string? NodeUpdatedColumn => null;
    public string? NodeAliasColumn => null;
    public string? NodeTokensColumn => null;
    public string EdgeIdColumn => "id";
    public string EdgeOriginColumn => "origin";
    public string EdgeTargetColumn => "target";
    public string EdgeKindColumn => "kind";
    public string EdgeDataColumn => "data";
    public string? EdgeCvnColumn => null;
    public string? EdgeLvnColumn => null;
    public string? EdgeSyncColumn => null;
    public string? EdgeWeightColumn => null;
    public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>();
    public IReadOnlyList<string> RequiredIndexDDL { get; } = [];
}

internal sealed class TokenSchemaAdapter : ISchemaAdapter
{
    public string NodeTableName => "_concepts";
    public string EdgeTableName => "_relations";
    public string? EdgeHistoryTableName => null;
    public string? MetaTableName => null;
    public string NodeIdColumn => "id";
    public string NodeKeyColumn => "key";
    public string NodeTypeColumn => "kind";
    public string NodeDataColumn => "data";
    public string? NodeCvnColumn => null;
    public string? NodeLvnColumn => null;
    public string? NodeSyncColumn => null;
    public string? NodeUpdatedColumn => null;
    public string? NodeAliasColumn => null;
    public string? NodeTokensColumn => "tokens";
    public string EdgeIdColumn => "id";
    public string EdgeOriginColumn => "origin";
    public string EdgeTargetColumn => "target";
    public string EdgeKindColumn => "kind";
    public string EdgeDataColumn => "data";
    public string? EdgeCvnColumn => null;
    public string? EdgeLvnColumn => null;
    public string? EdgeSyncColumn => null;
    public string? EdgeWeightColumn => null;
    public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>();
    public IReadOnlyList<string> RequiredIndexDDL { get; } = [];
}
