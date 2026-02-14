// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
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
        
        // O(log N) seek
        if (!indexCursor.SeekFirst(key.Value))
            return null;

        do
        {
            var indexRecord = _decoder.DecodeRecord(indexCursor.Payload);
            if (indexRecord.Length < 2) continue;

            long indexKeyValue = indexRecord[0].AsInt64();
            
            // Exact match check
            if (indexKeyValue == key.Value)
            {
                // Last column is the table rowid
                long rowId = indexRecord[^1].AsInt64();
                using var tableCursor = _reader.CreateCursor((uint)_tableRootPage);
                if (tableCursor.Seek(rowId))
                {
                    var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
                    try
                    {
                        _decoder.DecodeRecord(tableCursor.Payload, buffer);
                        return MapToRecord(buffer, key);
                    }
                    finally
                    {
                        ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
                    }
                }
                return null;
            }

            // Early exit: index is sorted
            if (indexKeyValue > key.Value)
                return null;
                
        } while (indexCursor.MoveNext());
        
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
                    var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
                    try
                    {
                        _decoder.DecodeRecord(tableCursor.Payload, buffer);
                        return MapToRecord(buffer);
                    }
                    finally
                    {
                        ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
                    }
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
        var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
        try
        {
            while (cursor.MoveNext())
            {
                _decoder.DecodeRecord(cursor.Payload, buffer);
                string rowIdStr = _colId >= 0 && _colId < _columnCount ? buffer[_colId].AsString() : "";
                if (!rowIdStr.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                return MapToRecord(buffer);
            }
            return null;
        }
        finally
        {
            ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
        }
    }

    private GraphRecord? GetViaTableScan(NodeKey key)
    {
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
        try
        {
            while (cursor.MoveNext())
            {
                _decoder.DecodeRecord(cursor.Payload, buffer);
                long barId = _colKey >= 0 && _colKey < _columnCount ? buffer[_colKey].AsInt64() : 0;
                if (barId != key.Value) continue;
                return MapToRecord(buffer, key);
            }
            return null;
        }
        finally
        {
            ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
        }
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