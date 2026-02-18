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

public class GraphAliasTests
{
    [Fact]
    public void GetNode_WithAlias_ReturnsAlias()
    {
        // 1. Setup Schema with Alias
        var adapter = new AliasSchemaAdapter();
        
        // 2. Build Record with Alias
        var nodeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "alias", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false }
        };

        var edgeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "origin", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "target", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
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
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, alias TEXT)",
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

        // Build payload
        var payload = BuildNodeRecordWithAlias("n1", 100, 1, "{}", "my-alias");

        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, payload)
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>()); // Empty Relations table

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        // 3. Act
        var node = graph.GetNode(new NodeKey(100));

        // 4. Assert
        Assert.NotNull(node);
        Assert.Equal(100, node.Value.Key.Value);
        Assert.Equal("my-alias", node.Value.Alias);
    }
    
    [Fact]
    public void GetNode_WithoutAlias_ReturnsNull()
    {
        var adapter = new AliasSchemaAdapter();
        var nodeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "alias", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false }
        };

        var edgeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = true, IsNotNull = true },
            new() { Name = "origin", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "target", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true }
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
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT, alias TEXT)",
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

        // Build payload with NULL alias
        var payload = BuildNodeRecordWithAlias("n1", 100, 1, "{}", null);

        var multiTableReader = new MultiTableFakeReader();
        multiTableReader.AddTable(2, new List<(long, byte[])> {
            (1, payload)
        });
        multiTableReader.AddTable(3, new List<(long, byte[])>()); // Empty Relations table

        using var graph = new SharcContextGraph(multiTableReader, adapter);
        graph.Initialize(schema);

        var node = graph.GetNode(new NodeKey(100));

        Assert.NotNull(node);
        Assert.Null(node.Value.Alias);
    }

    // --- Helpers ---

    private sealed class AliasSchemaAdapter : ISchemaAdapter
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
        public string? NodeAliasColumn => "alias"; // <--- Enabled
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

    private static byte[] BuildNodeRecordWithAlias(string id, long key, int kind, string data, string? alias)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var aliasBytes = alias != null ? Encoding.UTF8.GetBytes(alias) : null;

        long idSt = idBytes.Length * 2 + 13;
        long dataSt = dataBytes.Length * 2 + 13;
        long aliasSt = aliasBytes != null ? aliasBytes.Length * 2 + 13 : 0; // 0 = NULL

        var keyCol = EncodeInteger(key);
        var kindCol = EncodeInteger(kind);

        return BuildRecord(
            (idSt, idBytes),
            (keyCol.serialType, keyCol.body),
            (kindCol.serialType, kindCol.body),
            (dataSt, dataBytes),
            (aliasSt, aliasBytes ?? [])
        );
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

    // Copied from SharcContextGraphTests
    internal sealed class MultiTableFakeReader : IBTreeReader
    {
        private readonly Dictionary<uint, List<(long rowId, byte[] payload)>> _tables = new();
        public void AddTable(uint rootPage, List<(long, byte[])> rows) => _tables[rootPage] = rows;
        public IBTreeCursor CreateCursor(uint rootPage)
        {
            if (_tables.TryGetValue(rootPage, out var rows)) return new FakeBTreeCursor(rows);
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
            for (int i = 0; i < _rows.Count; i++) { if (_rows[i].rowId == rowId) { _index = i; return true; } }
            return false;
        }
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }
}
