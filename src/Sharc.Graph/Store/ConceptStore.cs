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
    public IBTreeReader Reader => _reader;
    private readonly ISchemaAdapter _schema;
    private readonly RecordDecoder _decoder = new();
    private int _tableRootPage;
    private int _columnCount;
    private string _tableName = "";

    // Index scan support
    private int _keyIndexRootPage = -1;

    // Column ordinals
    private int _colId = -1;
    private int _colKey = -1;
    private int _colType = -1;
    private int _colData = -1;
    private int _colCvn = -1;
    private int _colLvn = -1;
    private int _colSync = -1;
    private int _colUpdated = -1;
    private int _colTokens = -1;

    // Persistent cursors for reuse
    private IIndexBTreeCursor? _reusableIndexCursor;
    private IBTreeCursor? _reusableTableCursor;
    private IBTreeCursor? _reusableScanCursor;

    // Fixed buffers to avoid stackalloc in loops
    private readonly long[] _serialsBuffer = new long[64];
    private readonly long[] _indexSerialsBuffer = new long[16];

    public ConceptStore(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void Initialize(SharcSchema schemaInfo)
    {
        _tableName = _schema.NodeTableName;
        var table = schemaInfo.GetTable(_tableName);
        _tableRootPage = table.RootPage;
        _columnCount = table.Columns.Count;

        _colId = GetOrdinal(table, _schema.NodeIdColumn);
        _colKey = GetOrdinal(table, _schema.NodeKeyColumn);
        _colType = GetOrdinal(table, _schema.NodeTypeColumn);
        _colData = GetOrdinal(table, _schema.NodeDataColumn);

        _colCvn = _schema.NodeCvnColumn != null ? GetOrdinal(table, _schema.NodeCvnColumn) : -1;
        _colLvn = _schema.NodeLvnColumn != null ? GetOrdinal(table, _schema.NodeLvnColumn) : -1;
        _colSync = _schema.NodeSyncColumn != null ? GetOrdinal(table, _schema.NodeSyncColumn) : -1;
        _colUpdated = _schema.NodeUpdatedColumn != null ? GetOrdinal(table, _schema.NodeUpdatedColumn) : -1;
        _colTokens = _schema.NodeTokensColumn != null ? GetOrdinal(table, _schema.NodeTokensColumn) : -1;

        // Key Index (Integer based)
        var keyIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_tableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.NodeKeyColumn, StringComparison.OrdinalIgnoreCase));
        _keyIndexRootPage = keyIndex?.RootPage ?? -1;
    }

    private static int GetOrdinal(TableInfo table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        if (col == null) return -1;
        return col.Ordinal;
    }

    public GraphRecord? Get(NodeKey key, bool includeData = true)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");

        if (_keyIndexRootPage > 0)
            return GetViaIndex(_keyIndexRootPage, key, includeData);

        return GetViaTableScan(key, includeData);
    }

    private GraphRecord? GetViaIndex(int indexRootPage, NodeKey key, bool includeData)
    {
        _reusableIndexCursor ??= _reader.CreateIndexCursor((uint)indexRootPage);
        _reusableTableCursor ??= _reader.CreateCursor((uint)_tableRootPage);
        
        var indexCursor = _reusableIndexCursor;
        var tableCursor = _reusableTableCursor;

        indexCursor.Reset();
        if (!indexCursor.SeekFirst(key.Value)) 
            return null;

        do
        {
            int indexBodyOffset;
            _decoder.ReadSerialTypes(indexCursor.Payload, _indexSerialsBuffer, out indexBodyOffset);

            long indexValue = _decoder.DecodeInt64Direct(indexCursor.Payload, 0, _indexSerialsBuffer, indexBodyOffset);
            if (indexValue > key.Value) return null;
            if (indexValue != key.Value) continue;

            long rowId = _decoder.DecodeInt64Direct(indexCursor.Payload, _decoder.GetColumnCount(indexCursor.Payload) - 1, _indexSerialsBuffer, indexBodyOffset);
            if (tableCursor.Seek(rowId))
            {
                return MapToRecordSelective(tableCursor.Payload, includeData);
            }
        }
        while (indexCursor.MoveNext());

        return null;
    }

    public GraphRecord? Get(string id)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");
        return GetViaIdTableScan(id);
    }

    private GraphRecord? GetViaIdTableScan(string id)
    {
        _reusableScanCursor ??= _reader.CreateCursor((uint)_tableRootPage);
        var cursor = _reusableScanCursor;
        cursor.Reset();
        
        while (cursor.MoveNext())
        {
            int bodyOffset;
            _decoder.ReadSerialTypes(cursor.Payload, _serialsBuffer, out bodyOffset);
            string rowIdStr = _colId >= 0 && _colId < _columnCount ? _decoder.DecodeStringDirect(cursor.Payload, _colId, _serialsBuffer, bodyOffset) : "";
            if (!rowIdStr.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            return MapToRecordSelective(cursor.Payload, true);
        }
        return null;
    }

    private GraphRecord? GetViaTableScan(NodeKey key, bool includeData)
    {
        _reusableScanCursor ??= _reader.CreateCursor((uint)_tableRootPage);
        var cursor = _reusableScanCursor;
        cursor.Reset();
        
        while (cursor.MoveNext())
        {
            int bodyOffset;
            _decoder.ReadSerialTypes(cursor.Payload, _serialsBuffer, out bodyOffset);
            long barId = _colKey >= 0 && _colKey < _columnCount ? _decoder.DecodeInt64Direct(cursor.Payload, _colKey, _serialsBuffer, bodyOffset) : 0;
            if (barId != key.Value) continue;
            return MapToRecordSelective(cursor.Payload, includeData);
        }
        return null;
    }

    private GraphRecord MapToRecordSelective(ReadOnlySpan<byte> payload, bool includeData)
    {
        int bodyOffset;
        _decoder.ReadSerialTypes(payload, _serialsBuffer, out bodyOffset);

        long keyVal = _colKey >= 0 && _colKey < _columnCount ? _decoder.DecodeInt64Direct(payload, _colKey, _serialsBuffer, bodyOffset) : 0;
        NodeKey key = new NodeKey(keyVal);

        int typeId = _colType >= 0 && _colType < _columnCount ? (int)_decoder.DecodeInt64Direct(payload, _colType, _serialsBuffer, bodyOffset) : 0;
        
        string? id = null;
        string? data = null;

        if (includeData)
        {
            id = _colId >= 0 && _colId < _columnCount ? _decoder.DecodeStringDirect(payload, _colId, _serialsBuffer, bodyOffset) : null;
            data = _colData >= 0 && _colData < _columnCount ? _decoder.DecodeStringDirect(payload, _colData, _serialsBuffer, bodyOffset) : "{}";
        }
        
        string typeStr = _schema.TypeNames.TryGetValue(typeId, out var name) 
            ? name 
            : typeId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        long updatedUnix = _colUpdated >= 0 && _colUpdated < _columnCount ? _decoder.DecodeInt64Direct(payload, _colUpdated, _serialsBuffer, bodyOffset) : 0;
        var updatedAt = updatedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedUnix) : (DateTimeOffset?)null;

        var result = new GraphRecord(new RecordId(typeStr, id, key), key, typeId, data, updatedAt, updatedAt)
        {
            CVN = _colCvn >= 0 && _colCvn < _columnCount ? (int)_decoder.DecodeInt64Direct(payload, _colCvn, _serialsBuffer, bodyOffset) : 0,
            LVN = _colLvn >= 0 && _colLvn < _columnCount ? (int)_decoder.DecodeInt64Direct(payload, _colLvn, _serialsBuffer, bodyOffset) : 0,
            SyncStatus = _colSync >= 0 && _colSync < _columnCount ? (int)_decoder.DecodeInt64Direct(payload, _colSync, _serialsBuffer, bodyOffset) : 0,
            Tokens = _colTokens >= 0 && _colTokens < _columnCount ? (int)_decoder.DecodeInt64Direct(payload, _colTokens, _serialsBuffer, bodyOffset) : 0
        };
        
        return result;
    }

    public void Dispose()
    {
        _reusableIndexCursor?.Dispose();
        _reusableTableCursor?.Dispose();
        _reusableScanCursor?.Dispose();
    }
}