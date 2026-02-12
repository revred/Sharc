using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Graph.Store;
using Sharc.Core.Schema;

namespace Sharc.Graph.Tests.Unit.Store;

[TestClass]
public class RelationStoreTests
{
    [TestMethod]
    public void GetEdges_WithMatchingOrigin_ReturnsEdges()
    {
        var (schema, adapter) = CreateEdgeTestSetup();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 100, 15, 300, "{}")),
            (3, BuildEdgeRecord("e3", 200, 10, 100, "{}"))
        };
        var reader = new FakeBTreeReader(rows);
        var store = new RelationStore(reader, adapter);
        store.Initialize(schema);

        var edges = store.GetEdges(new NodeKey(100)).ToList();

        Assert.HasCount(2, edges);
        Assert.AreEqual(200, edges[0].TargetKey.Value);
        Assert.AreEqual(300, edges[1].TargetKey.Value);
    }

    [TestMethod]
    public void GetEdges_WithKindFilter_FiltersCorrectly()
    {
        var (schema, adapter) = CreateEdgeTestSetup();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}")),
            (2, BuildEdgeRecord("e2", 100, 15, 300, "{}")),
        };
        var reader = new FakeBTreeReader(rows);
        var store = new RelationStore(reader, adapter);
        store.Initialize(schema);

        var edges = store.GetEdges(new NodeKey(100), RelationKind.Contains).ToList();

        Assert.HasCount(1, edges);
        Assert.AreEqual(10, edges[0].Kind);
    }

    [TestMethod]
    public void GetEdges_NoMatchingOrigin_ReturnsEmpty()
    {
        var (schema, adapter) = CreateEdgeTestSetup();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEdgeRecord("e1", 100, 10, 200, "{}"))
        };
        var reader = new FakeBTreeReader(rows);
        var store = new RelationStore(reader, adapter);
        store.Initialize(schema);

        var edges = store.GetEdges(new NodeKey(999)).ToList();

        Assert.IsEmpty(edges);
    }

    #region Test Helpers

    private static (SharcSchema schema, ISchemaAdapter adapter) CreateEdgeTestSetup()
    {
        var edgeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "origin", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "target", Ordinal = 3, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "data", Ordinal = 4, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false }
        };

        var nodeCols = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false },
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false }
        };

        var schema = new SharcSchema
        {
            Tables = new List<TableInfo>
            {
                new()
                {
                    Name = "_concepts",
                    RootPage = 2,
                    Sql = "CREATE TABLE _concepts (id TEXT, key INTEGER, kind INTEGER, data TEXT)",
                    Columns = nodeCols,
                    IsWithoutRowId = false
                },
                new()
                {
                    Name = "_relations",
                    RootPage = 3,
                    Sql = "CREATE TABLE _relations (id TEXT, origin INTEGER, kind INTEGER, target INTEGER, data TEXT)",
                    Columns = edgeCols,
                    IsWithoutRowId = false
                }
            },
            Indexes = new List<IndexInfo>(),
            Views = new List<ViewInfo>()
        };

        return (schema, new TestSchemaAdapter());
    }

    private static byte[] BuildEdgeRecord(string id, long origin, int kind, long target, string data)
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

    #endregion

    #region Test Doubles

    private sealed class TestSchemaAdapter : ISchemaAdapter
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
        public string EdgeIdColumn => "id";
        public string EdgeOriginColumn => "origin";
        public string EdgeTargetColumn => "target";
        public string EdgeKindColumn => "kind";
        public string EdgeDataColumn => "data";
        public string? EdgeCvnColumn => null;
        public string? EdgeLvnColumn => null;
        public string? EdgeSyncColumn => null;
        public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>();
        public IReadOnlyList<string> RequiredIndexDDL { get; } = [];
    }

    private sealed class FakeBTreeReader : IBTreeReader
    {
        private readonly List<(long rowId, byte[] payload)> _rows;
        public FakeBTreeReader(List<(long rowId, byte[] payload)> rows) => _rows = rows;
        public IBTreeCursor CreateCursor(uint rootPage) => new FakeBTreeCursor(_rows);
        public IIndexBTreeCursor CreateIndexCursor(uint rootPage) => throw new NotSupportedException();
    }

    private sealed class FakeBTreeCursor : IBTreeCursor
    {
        private readonly List<(long rowId, byte[] payload)> _rows;
        private int _index = -1;
        public FakeBTreeCursor(List<(long rowId, byte[] payload)> rows) => _rows = rows;
        public long RowId => _index >= 0 && _index < _rows.Count ? _rows[_index].rowId : 0;
        public ReadOnlySpan<byte> Payload => _index >= 0 && _index < _rows.Count ? _rows[_index].payload : default;
        public int PayloadSize => _index >= 0 && _index < _rows.Count ? _rows[_index].payload.Length : 0;
        public bool MoveNext() { _index++; return _index < _rows.Count; }
        public bool Seek(long rowId) => false;
        public void Dispose() { }
    }

    #endregion
}
