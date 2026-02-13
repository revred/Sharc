/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;

namespace Sharc.Graph.Store;

internal sealed class ConceptStore
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private readonly RecordDecoder _decoder = new();
    private int _tableRootPage;
    private int _columnCount;

    // Index scan support
    private int _keyIndexRootPage = -1;
    private int _idIndexRootPage = -1;

    // Column ordinals
    private int _colId = -1;
    private int _colKey = -1;
    private int _colKind = -1;
    private int _colData = -1;
    private int _colCvn = -1;
    private int _colLvn = -1;
    private int _colSync = -1;
    private int _colUpdated = -1;
    private int _colTokens = -1;

    public ConceptStore(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void Initialize(SharcSchema schemaInfo)
    {
        var table = schemaInfo.GetTable(_schema.NodeTableName);
        _tableRootPage = table.RootPage;
        _columnCount = table.Columns.Count;

        _colId = GetOrdinal(table, _schema.NodeIdColumn);
        _colKey = GetOrdinal(table, _schema.NodeKeyColumn);
        _colKind = GetOrdinal(table, _schema.NodeTypeColumn);
        _colData = GetOrdinal(table, _schema.NodeDataColumn);

        _colCvn = _schema.NodeCvnColumn != null ? GetOrdinal(table, _schema.NodeCvnColumn) : -1;
        _colLvn = _schema.NodeLvnColumn != null ? GetOrdinal(table, _schema.NodeLvnColumn) : -1;
        _colSync = _schema.NodeSyncColumn != null ? GetOrdinal(table, _schema.NodeSyncColumn) : -1;
        _colUpdated = _schema.NodeUpdatedColumn != null ? GetOrdinal(table, _schema.NodeUpdatedColumn) : -1;
        _colTokens = _schema.NodeTokensColumn != null ? GetOrdinal(table, _schema.NodeTokensColumn) : -1;

        // Look for an index whose first column matches the node key column
        var keyIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_schema.NodeTableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.NodeKeyColumn, StringComparison.OrdinalIgnoreCase));
        _keyIndexRootPage = keyIndex?.RootPage ?? -1;

        // Look for an index on the string ID column
        var idIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_schema.NodeTableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.NodeIdColumn, StringComparison.OrdinalIgnoreCase));
        _idIndexRootPage = idIndex?.RootPage ?? -1;
    }
    
    private static int GetOrdinal(TableInfo table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        if (col == null) return -1;
        return col.Ordinal;
    }

    public GraphRecord? Get(NodeKey key)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");

        // Use index scan if an index on the key column exists, otherwise fall back to table scan.
        if (_keyIndexRootPage > 0)
            return GetViaIndex(key);

        return GetViaTableScan(key);
    }

    private GraphRecord? GetViaIndex(NodeKey key)
    {
        using var indexCursor = _reader.CreateIndexCursor((uint)_keyIndexRootPage);
        while (indexCursor.MoveNext())
        {
            var indexRecord = _decoder.DecodeRecord(indexCursor.Payload);
            if (indexRecord.Length < 2) continue;

            long indexKeyValue = indexRecord[0].AsInt64();
            if (indexKeyValue == key.Value)
            {
                // Last column is the table rowid
                long rowId = indexRecord[^1].AsInt64();
                using var tableCursor = _reader.CreateCursor((uint)_tableRootPage);
                if (tableCursor.Seek(rowId))
                {
                    var buffer = new ColumnValue[_columnCount];
                    _decoder.DecodeRecord(tableCursor.Payload, buffer);
                    return MapToRecord(buffer, key);
                }
                return null;
            }

            // Early exit: index is sorted, so if we've passed the target value, stop
            if (indexKeyValue > key.Value)
                return null;
        }
        return null;
    }

    public GraphRecord? Get(string id)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");

        if (_idIndexRootPage > 0)
            return GetViaIdIndex(id);

        return GetViaIdTableScan(id);
    }

    private GraphRecord? GetViaIdIndex(string id)
    {
        using var indexCursor = _reader.CreateIndexCursor((uint)_idIndexRootPage);
        while (indexCursor.MoveNext())
        {
            var indexRecord = _decoder.DecodeRecord(indexCursor.Payload);
            if (indexRecord.Length < 2) continue;

            string indexIdValue = indexRecord[0].AsString();
            if (indexIdValue.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                long rowId = indexRecord[^1].AsInt64();
                using var tableCursor = _reader.CreateCursor((uint)_tableRootPage);
                if (tableCursor.Seek(rowId))
                {
                    var buffer = new ColumnValue[_columnCount];
                    _decoder.DecodeRecord(tableCursor.Payload, buffer);
                    return MapToRecord(buffer);
                }
                return null;
            }

            // GUID/String indices might not be sorted in a way easy to early-exit with Equals
        }
        return null;
    }

    private GraphRecord? GetViaIdTableScan(string id)
    {
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        var buffer = new ColumnValue[_columnCount];
        while (cursor.MoveNext())
        {
            _decoder.DecodeRecord(cursor.Payload, buffer);
            string rowIdStr = _colId >= 0 && _colId < buffer.Length ? buffer[_colId].AsString() : "";
            if (!rowIdStr.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            return MapToRecord(buffer);
        }
        return null;
    }

    private GraphRecord? GetViaTableScan(NodeKey key)
    {
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        var buffer = new ColumnValue[_columnCount];
        while (cursor.MoveNext())
        {
            _decoder.DecodeRecord(cursor.Payload, buffer);
            long barId = _colKey >= 0 && _colKey < buffer.Length ? buffer[_colKey].AsInt64() : 0;
            if (barId != key.Value) continue;
            return MapToRecord(buffer, key);
        }
        return null;
    }

    private GraphRecord MapToRecord(ColumnValue[] columns, NodeKey? keyOverride = null)
    {
        string id = _colId >= 0 && _colId < columns.Length ? columns[_colId].AsString() : "";
        long keyVal = keyOverride?.Value ?? (_colKey >= 0 && _colKey < columns.Length ? columns[_colKey].AsInt64() : 0);
        NodeKey key = new NodeKey(keyVal);

        int kind = _colKind >= 0 && _colKind < columns.Length ? (int)columns[_colKind].AsInt64() : 0;
        string data = _colData >= 0 && _colData < columns.Length ? columns[_colData].AsString() : "{}";
        
        string typeStr = _schema.TypeNames.TryGetValue(kind, out var name) 
            ? name 
            : kind.ToString(System.Globalization.CultureInfo.InvariantCulture);

        long updatedUnix = _colUpdated >= 0 && _colUpdated < columns.Length ? columns[_colUpdated].AsInt64() : 0;
        var updatedAt = updatedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedUnix) : (DateTimeOffset?)null;

        var record = new GraphRecord(new RecordId(typeStr, id, key), key, kind, data, updatedAt, updatedAt)
        {
            CVN = _colCvn >= 0 && _colCvn < columns.Length ? (int)columns[_colCvn].AsInt64() : 0,
            LVN = _colLvn >= 0 && _colLvn < columns.Length ? (int)columns[_colLvn].AsInt64() : 0,
            SyncStatus = _colSync >= 0 && _colSync < columns.Length ? (int)columns[_colSync].AsInt64() : 0,
            Tokens = _colTokens >= 0 && _colTokens < columns.Length ? (int)columns[_colTokens].AsInt64() : 0
        };
        
        return record;
    }
}
