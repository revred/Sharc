// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Graph.Model;

namespace Sharc.Graph.Store;

/// <summary>
/// Zero-allocation edge cursor using index scan with O(log N) positioning.
/// Rents ColumnValue[] from ArrayPool and returns on dispose.
/// </summary>
/// <summary>
/// Zero-allocation edge cursor using index scan with O(log N) positioning.
/// Rents ColumnValue[] from ArrayPool and returns on dispose.
/// </summary>
internal sealed class IndexEdgeCursor : IEdgeCursor
{
    private readonly IIndexBTreeCursor _indexCursor;
    private readonly IBTreeCursor _tableCursor;
    private readonly RecordDecoder _decoder;
    private readonly ColumnValue[] _tableBuffer;
    private readonly int _columnCount;
    private readonly long _lookupKey;
    private readonly RelationKind? _kindFilter;
    private readonly int _colSource, _colKind, _colTarget, _colData, _colWeight;
    private bool _positioned;
    private bool _exhausted;

    public long OriginKey { get; private set; }
    public long TargetKey { get; private set; }
    public int Kind { get; private set; }
    public float Weight { get; private set; }
    public ReadOnlyMemory<byte> JsonDataUtf8 { get; private set; }

    internal IndexEdgeCursor(IBTreeReader reader, RecordDecoder decoder,
        int indexRootPage, int tableRootPage, int columnCount,
        long lookupKey, RelationKind? kindFilter,
        int colSource, int colKind, int colTarget, int colData, int colWeight)
    {
        _decoder = decoder;
        _columnCount = columnCount;
        _lookupKey = lookupKey;
        _kindFilter = kindFilter;
        _colSource = colSource;
        _colKind = colKind;
        _colTarget = colTarget;
        _colData = colData;
        _colWeight = colWeight;

        _indexCursor = reader.CreateIndexCursor((uint)indexRootPage);
        _tableCursor = reader.CreateCursor((uint)tableRootPage);
        _tableBuffer = ArrayPool<ColumnValue>.Shared.Rent(columnCount);
    }

    public bool MoveNext()
    {
        if (_exhausted) return false;

        while (true)
        {
            bool hasNext;
            if (!_positioned)
            {
                hasNext = _indexCursor.SeekFirst(_lookupKey);
                _positioned = true;
            }
            else
            {
                hasNext = _indexCursor.MoveNext();
            }

            if (!hasNext)
            {
                _exhausted = true;
                return false;
            }

            var indexRecord = _decoder.DecodeRecord(_indexCursor.Payload);
            if (indexRecord.Length < 2) continue;

            long indexValue = indexRecord[0].AsInt64();
            if (indexValue > _lookupKey)
            {
                _exhausted = true;
                return false;
            }
            if (indexValue != _lookupKey) continue;

            long rowId = indexRecord[^1].AsInt64();
            if (!_tableCursor.Seek(rowId)) continue;

            _decoder.DecodeRecord(_tableCursor.Payload, _tableBuffer);

            int kindVal = _colKind >= 0 && _colKind < _columnCount
                ? (int)_tableBuffer[_colKind].AsInt64() : 0;
            if (_kindFilter.HasValue && kindVal != (int)_kindFilter.Value) continue;

            OriginKey = _colSource >= 0 && _colSource < _columnCount ? _tableBuffer[_colSource].AsInt64() : 0;
            TargetKey = _colTarget >= 0 && _colTarget < _columnCount ? _tableBuffer[_colTarget].AsInt64() : 0;
            Kind = kindVal;
            Weight = _colWeight >= 0 && _colWeight < _columnCount ? (float)_tableBuffer[_colWeight].AsDouble() : 1.0f;
            JsonDataUtf8 = _colData >= 0 && _colData < _columnCount ? _tableBuffer[_colData].AsBytes() : default;
            return true;
        }
    }

    public void Dispose()
    {
        _indexCursor.Dispose();
        _tableCursor.Dispose();
        ArrayPool<ColumnValue>.Shared.Return(_tableBuffer, clearArray: true);
    }
}

/// <summary>
/// Zero-allocation edge cursor using full table scan (fallback when no index exists).
/// </summary>
internal sealed class TableScanEdgeCursor : IEdgeCursor
{
    private readonly IBTreeCursor _cursor;
    private readonly RecordDecoder _decoder;
    private readonly ColumnValue[] _buffer;
    private readonly int _columnCount;
    private readonly long _lookupKey;
    private readonly int _lookupColOrdinal;
    private readonly RelationKind? _kindFilter;
    private readonly int _colSource, _colKind, _colTarget, _colData, _colWeight;

    public long OriginKey { get; private set; }
    public long TargetKey { get; private set; }
    public int Kind { get; private set; }
    public float Weight { get; private set; }
    public ReadOnlyMemory<byte> JsonDataUtf8 { get; private set; }

    internal TableScanEdgeCursor(IBTreeReader reader, RecordDecoder decoder,
        int tableRootPage, int columnCount,
        long lookupKey, int lookupColOrdinal, RelationKind? kindFilter,
        int colSource, int colKind, int colTarget, int colData, int colWeight)
    {
        _decoder = decoder;
        _columnCount = columnCount;
        _lookupKey = lookupKey;
        _lookupColOrdinal = lookupColOrdinal;
        _kindFilter = kindFilter;
        _colSource = colSource;
        _colKind = colKind;
        _colTarget = colTarget;
        _colData = colData;
        _colWeight = colWeight;

        _cursor = reader.CreateCursor((uint)tableRootPage);
        _buffer = ArrayPool<ColumnValue>.Shared.Rent(columnCount);
    }

    public bool MoveNext()
    {
        while (_cursor.MoveNext())
        {
            _decoder.DecodeRecord(_cursor.Payload, _buffer);

            long recordKey = _lookupColOrdinal >= 0 && _lookupColOrdinal < _columnCount ? _buffer[_lookupColOrdinal].AsInt64() : 0;
            if (recordKey != _lookupKey) continue;

            int kindVal = _colKind >= 0 && _colKind < _columnCount ? (int)_buffer[_colKind].AsInt64() : 0;
            if (_kindFilter.HasValue && kindVal != (int)_kindFilter.Value) continue;

            OriginKey = _colSource >= 0 && _colSource < _columnCount ? _buffer[_colSource].AsInt64() : 0;
            TargetKey = _colTarget >= 0 && _colTarget < _columnCount ? _buffer[_colTarget].AsInt64() : 0;
            Kind = kindVal;
            Weight = _colWeight >= 0 && _colWeight < _columnCount ? (float)_buffer[_colWeight].AsDouble() : 1.0f;
            JsonDataUtf8 = _colData >= 0 && _colData < _columnCount ? _buffer[_colData].AsBytes() : default;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        _cursor.Dispose();
        ArrayPool<ColumnValue>.Shared.Return(_buffer, clearArray: true);
    }
}
