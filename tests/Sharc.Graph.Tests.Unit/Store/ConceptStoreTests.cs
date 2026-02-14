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
public class ConceptStoreTests
{
    /// <summary>
    /// BUG-01 regression test: ConceptStore.Get() must match on the BarID column value,
    /// not on the SQLite rowid. When BarID != rowid, Seek(key.Value) returns the wrong row.
    /// This test creates rows where rowid and BarID differ, and verifies Get() finds the
    /// correct row by scanning the BarID column.
    /// </summary>
    [TestMethod]
    public void Get_BarIdDifferentFromRowid_ReturnsCorrectRecord()
    {
        // Schema: id TEXT(0), key INTEGER(1), kind INTEGER(2), data TEXT(3)
        // Row at rowid=1: id="guid-a", key=100, kind=1, data="{\"name\":\"A\"}"
        // Row at rowid=2: id="guid-b", key=200, kind=2, data="{\"name\":\"B\"}"
        // Looking for key=200 should return row 2, NOT seek to rowid=200

        var schema = CreateTestSchema();
        var adapter = new TestSchemaAdapter();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEntityRecord("guid-a", 100, 1, "{\"name\":\"A\"}")),
            (2, BuildEntityRecord("guid-b", 200, 2, "{\"name\":\"B\"}"))
        };
        var reader = new FakeBTreeReader(rows);

        var store = new ConceptStore(reader, adapter);
        store.Initialize(schema);

        var result = store.Get(new NodeKey(200));

        Assert.IsNotNull(result);
        Assert.AreEqual(200, result.Key.Value);
        Assert.AreEqual("{\"name\":\"B\"}", result.JsonData);
    }

    [TestMethod]
    public void Get_WithTokens_ReturnsCorrectTokens()
    {
        var schema = CreateTestSchemaWithTokens();
        var adapter = new TestSchemaAdapterWithTokens();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEntityRecordWithTokens("guid-a", 100, 1, "{}", 500))
        };
        var reader = new FakeBTreeReader(rows);

        var store = new ConceptStore(reader, adapter);
        store.Initialize(schema);

        var result = store.Get(new NodeKey(100));

        Assert.IsNotNull(result);
        Assert.AreEqual(500, result.Tokens);
    }

    [TestMethod]
    public void Get_NonexistentKey_ReturnsNull()
    {
        var schema = CreateTestSchema();
        var adapter = new TestSchemaAdapter();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEntityRecord("guid-a", 100, 1, "{}"))
        };
        var reader = new FakeBTreeReader(rows);

        var store = new ConceptStore(reader, adapter);
        store.Initialize(schema);

        var result = store.Get(new NodeKey(999));

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Get_FirstRowMatch_ReturnsImmediately()
    {
        var schema = CreateTestSchema();
        var adapter = new TestSchemaAdapter();
        var rows = new List<(long rowId, byte[] payload)>
        {
            (1, BuildEntityRecord("guid-a", 50, 1, "{\"first\":true}")),
            (2, BuildEntityRecord("guid-b", 60, 1, "{\"second\":true}"))
        };
        var reader = new FakeBTreeReader(rows);

        var store = new ConceptStore(reader, adapter);
        store.Initialize(schema);

        var result = store.Get(new NodeKey(50));

        Assert.IsNotNull(result);
        Assert.AreEqual("{\"first\":true}", result.JsonData);
    }

    #region Test Helpers

    private static SharcSchema CreateTestSchema()
    {
        // Table: _concepts with columns id(0), key(1), kind(2), data(3)
        var columns = new List<ColumnInfo>
        {
            new() { Name = "id", Ordinal = 0, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = true },
            new() { Name = "key", Ordinal = 1, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false },
            new() { Name = "kind", Ordinal = 2, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false },
            new() { Name = "data", Ordinal = 3, DeclaredType = "TEXT", IsPrimaryKey = false, IsNotNull = false }
        };

        var table = new TableInfo
        {
            Name = "_concepts",
            RootPage = 2,
            Sql = "CREATE TABLE _concepts (id TEXT NOT NULL, key INTEGER, kind INTEGER, data TEXT)",
            Columns = columns,
            IsWithoutRowId = false
        };

        return new SharcSchema
        {
            Tables = new List<TableInfo> { table },
            Indexes = new List<IndexInfo>(),
            Views = new List<ViewInfo>()
        };
    }

    /// <summary>
    /// Builds a valid SQLite record for the _concepts table:
    /// columns: id TEXT, key INTEGER, kind INTEGER, data TEXT
    /// </summary>
    private static byte[] BuildEntityRecord(string id, long key, int kind, string data)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);

        // Serial types: TEXT=2*len+13, INT (1-byte if -128..127, 2-byte if -32768..32767, etc.)
        long idSerialType = idBytes.Length * 2 + 13;
        (long serialType, byte[] body) keyCol = EncodeInteger(key);
        (long serialType, byte[] body) kindCol = EncodeInteger(kind);
        long dataSerialType = dataBytes.Length * 2 + 13;

        return BuildRecord(
            (idSerialType, idBytes),
            (keyCol.serialType, keyCol.body),
            (kindCol.serialType, kindCol.body),
            (dataSerialType, dataBytes));
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
        if (value >= -8388608 && value <= 8388607)
        {
            var buf = new byte[3];
            buf[0] = (byte)((value >> 16) & 0xFF);
            buf[1] = (byte)((value >> 8) & 0xFF);
            buf[2] = (byte)(value & 0xFF);
            return (3, buf);
        }
        if (value >= int.MinValue && value <= int.MaxValue)
        {
            var buf = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, (int)value);
            return (4, buf);
        }
        {
            var buf = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buf, value);
            return (6, buf);
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

    private static SharcSchema CreateTestSchemaWithTokens()
    {
        var originalTable = CreateTestSchema().Tables[0];
        var columns = new List<ColumnInfo>(originalTable.Columns)
        {
            new() { Name = "tokens", Ordinal = 4, DeclaredType = "INTEGER", IsPrimaryKey = false, IsNotNull = false }
        };

        var tableWithTokens = new TableInfo
        {
            Name = originalTable.Name,
            RootPage = originalTable.RootPage,
            Sql = originalTable.Sql,
            Columns = columns,
            IsWithoutRowId = originalTable.IsWithoutRowId
        };

        return new SharcSchema
        {
            Tables = new List<TableInfo> { tableWithTokens },
            Indexes = new List<IndexInfo>(),
            Views = new List<ViewInfo>()
        };
    }

    private static byte[] BuildEntityRecordWithTokens(string id, long key, int kind, string data, int tokens)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        long idSt = idBytes.Length * 2 + 13;
        var keyCol = EncodeInteger(key);
        var kindCol = EncodeInteger(kind);
        long dataSt = dataBytes.Length * 2 + 13;
        var tokensCol = EncodeInteger(tokens);

        return BuildRecord(
            (idSt, idBytes),
            (keyCol.serialType, keyCol.body),
            (kindCol.serialType, kindCol.body),
            (dataSt, dataBytes),
            (tokensCol.serialType, tokensCol.body));
    }

    #endregion

    #region Test Doubles

    private sealed class TestSchemaAdapterWithTokens : TestSchemaAdapter
    {
        public override string? NodeTokensColumn => "tokens";
    }

    private class TestSchemaAdapter : ISchemaAdapter
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
        public virtual string? NodeTokensColumn => null;
        public string EdgeIdColumn => "id";
        public string EdgeOriginColumn => "origin";
        public string EdgeTargetColumn => "target";
        public string EdgeKindColumn => "kind";
        public string EdgeDataColumn => "data";
        public string? EdgeCvnColumn => null;
        public string? EdgeLvnColumn => null;
        public string? EdgeSyncColumn => null;
        public string? EdgeWeightColumn => null;
        public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>
        {
            [1] = "File",
            [2] = "Class"
        };
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

        public bool MoveNext()
        {
            _index++;
            return _index < _rows.Count;
        }

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

        public bool MoveLast()
        {
            if (_rows.Count == 0) return false;
            _index = _rows.Count - 1;
            return true;
        }

        public void Dispose() { }
    }

    #endregion
}
