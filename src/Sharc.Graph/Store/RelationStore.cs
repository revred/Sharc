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

using System.Buffers;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;

namespace Sharc.Graph.Store;

internal sealed class RelationStore
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private readonly RecordDecoder _decoder = new();
    private int _tableRootPage;
    private int _columnCount;

    // Index scan support
    private int _originIndexRootPage = -1;

    // Column ordinals
    private int _colSource = -1;
    private int _colKind = -1;
    private int _colTarget = -1;
    private int _colData = -1;
    private int _colId = -1;
    private int _colCvn = -1;
    private int _colLvn = -1;
    private int _colSync = -1;
    private int _colWeight = -1;

    public RelationStore(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void Initialize(SharcSchema schemaInfo)
    {
        var table = schemaInfo.GetTable(_schema.EdgeTableName);
        _tableRootPage = table.RootPage;
        _columnCount = table.Columns.Count;

        _colSource = GetOrdinal(table, _schema.EdgeOriginColumn);
        _colKind = GetOrdinal(table, _schema.EdgeKindColumn);
        _colTarget = GetOrdinal(table, _schema.EdgeTargetColumn);
        _colData = GetOrdinal(table, _schema.EdgeDataColumn);
        _colId = GetOrdinal(table, _schema.EdgeIdColumn);

        _colCvn = _schema.EdgeCvnColumn != null ? GetOrdinal(table, _schema.EdgeCvnColumn) : -1;
        _colLvn = _schema.EdgeLvnColumn != null ? GetOrdinal(table, _schema.EdgeLvnColumn) : -1;
        _colSync = _schema.EdgeSyncColumn != null ? GetOrdinal(table, _schema.EdgeSyncColumn) : -1;
        _colWeight = _schema.EdgeWeightColumn != null ? GetOrdinal(table, _schema.EdgeWeightColumn) : -1;

        // Look for an index whose first column matches the edge origin column
        var originIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_schema.EdgeTableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.EdgeOriginColumn, StringComparison.OrdinalIgnoreCase));
        _originIndexRootPage = originIndex?.RootPage ?? -1;
    }
    
    private static int GetOrdinal(TableInfo table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        if (col == null) return -1;
        return col.Ordinal;
    }

    /// <summary>
    /// Gets edges originating from the given node key.
    /// Uses index scan if an index on the origin column exists, otherwise falls back to table scan.
    /// </summary>
    public IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kindFilter = null)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");

        if (_originIndexRootPage > 0)
            return GetEdgesViaIndex(origin, kindFilter);

        return GetEdgesViaTableScan(origin, kindFilter);
    }

    private IEnumerable<GraphEdge> GetEdgesViaIndex(NodeKey origin, RelationKind? kindFilter)
    {
        using var indexCursor = _reader.CreateIndexCursor((uint)_originIndexRootPage);
        var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
        try
        {
            // Use SeekFirst for O(log n) initial positioning instead of linear scan
            if (!indexCursor.SeekFirst(origin.Value))
                yield break; // No entries with this origin key

            // Reuse a single table cursor for all row lookups
            using var tableCursor = _reader.CreateCursor((uint)_tableRootPage);

            // Process the first entry (SeekFirst already positioned us)
            do
            {
                var indexRecord = _decoder.DecodeRecord(indexCursor.Payload);
                if (indexRecord.Length < 2) continue;

                long indexOriginValue = indexRecord[0].AsInt64();

                // Early exit: index is sorted, so if we've passed the target value, stop
                if (indexOriginValue > origin.Value)
                    yield break;

                if (indexOriginValue != origin.Value) continue;

                // Match found — seek the table row by rowid
                long rowId = indexRecord[^1].AsInt64();
                if (!tableCursor.Seek(rowId)) continue;

                _decoder.DecodeRecord(tableCursor.Payload, buffer);

                long kindVal = _colKind >= 0 && _colKind < _columnCount ? buffer[_colKind].AsInt64() : 0;
                if (kindFilter.HasValue && kindVal != (int)kindFilter.Value) continue;

                yield return MapToEdge(buffer);
            }
            while (indexCursor.MoveNext());
        }
        finally
        {
            ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
        }
    }

    private IEnumerable<GraphEdge> GetEdgesViaTableScan(NodeKey origin, RelationKind? kindFilter)
    {
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        var buffer = ArrayPool<ColumnValue>.Shared.Rent(_columnCount);
        try
        {
            while (cursor.MoveNext())
            {
                _decoder.DecodeRecord(cursor.Payload, buffer);

                long sourceKey = _colSource >= 0 && _colSource < _columnCount ? buffer[_colSource].AsInt64() : 0;
                if (sourceKey != origin.Value) continue;

                long kindVal = _colKind >= 0 && _colKind < _columnCount ? buffer[_colKind].AsInt64() : 0;
                if (kindFilter.HasValue && kindVal != (int)kindFilter.Value) continue;

                yield return MapToEdge(buffer);
            }
        }
        finally
        {
            ArrayPool<ColumnValue>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Creates a zero-allocation edge cursor for the given origin node.
    /// Avoids GraphEdge allocation per row — caller reads typed properties directly.
    /// </summary>
    internal IEdgeCursor CreateEdgeCursor(NodeKey origin, RelationKind? kindFilter = null)
    {
        if (_originIndexRootPage > 0)
        {
            return new IndexEdgeCursor(_reader, _decoder, _originIndexRootPage,
                _tableRootPage, _columnCount, origin.Value, kindFilter,
                _colSource, _colKind, _colTarget, _colData, _colWeight);
        }
        return new TableScanEdgeCursor(_reader, _decoder, _tableRootPage,
            _columnCount, origin.Value, kindFilter,
            _colSource, _colKind, _colTarget, _colData, _colWeight);
    }

    private GraphEdge MapToEdge(ColumnValue[] columns)
    {
        long source = _colSource >= 0 && _colSource < _columnCount ? columns[_colSource].AsInt64() : 0;
        long target = _colTarget >= 0 && _colTarget < _columnCount ? columns[_colTarget].AsInt64() : 0;
        int kind = _colKind >= 0 && _colKind < _columnCount ? (int)columns[_colKind].AsInt64() : 0;
        string data = _colData >= 0 && _colData < _columnCount ? columns[_colData].AsString() : "{}";
        string id = _colId >= 0 && _colId < _columnCount ? columns[_colId].AsString() : "";
        
        var edge = new GraphEdge(
            new RecordId(_schema.EdgeTableName, id),
            new NodeKey(source),
            new NodeKey(target),
            kind,
            data)
        {
            CVN = _colCvn >= 0 && _colCvn < _columnCount ? (int)columns[_colCvn].AsInt64() : 0,
            LVN = _colLvn >= 0 && _colLvn < _columnCount ? (int)columns[_colLvn].AsInt64() : 0,
            SyncStatus = _colSync >= 0 && _colSync < _columnCount ? (int)columns[_colSync].AsInt64() : 0,
            Weight = _colWeight >= 0 && _colWeight < _columnCount ? (float)columns[_colWeight].AsDouble() : 1.0f
        };
        
        return edge;
    }
}
