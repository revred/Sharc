/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Core.Records;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Schema;

namespace Sharc.Graph.Store;

internal sealed class RelationStore
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private int _tableRootPage;
    
    // Column ordinals
    private int _colSource = -1;
    private int _colKind = -1;
    private int _colTarget = -1;
    private int _colData = -1;
    private int _colId = -1;
    private int _colCvn = -1;
    private int _colLvn = -1;
    private int _colSync = -1;
    
    public RelationStore(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void Initialize(SharcSchema schemaInfo)
    {
        var table = schemaInfo.GetTable(_schema.EdgeTableName);
        _tableRootPage = table.RootPage;
        
        _colSource = GetOrdinal(table, _schema.EdgeOriginColumn);
        _colKind = GetOrdinal(table, _schema.EdgeKindColumn);
        _colTarget = GetOrdinal(table, _schema.EdgeTargetColumn);
        _colData = GetOrdinal(table, _schema.EdgeDataColumn);
        _colId = GetOrdinal(table, _schema.EdgeIdColumn);
        
        _colCvn = _schema.EdgeCvnColumn != null ? GetOrdinal(table, _schema.EdgeCvnColumn) : -1;
        _colLvn = _schema.EdgeLvnColumn != null ? GetOrdinal(table, _schema.EdgeLvnColumn) : -1;
        _colSync = _schema.EdgeSyncColumn != null ? GetOrdinal(table, _schema.EdgeSyncColumn) : -1;
    }
    
    private static int GetOrdinal(TableInfo table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        if (col == null) return -1;
        return col.Ordinal;
    }

    /// <summary>
    /// Gets edges originating from the given node key.
    /// </summary>
    public IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kindFilter = null)
    {
        if (_tableRootPage == 0) throw new InvalidOperationException("Store not initialized.");

        // TODO: Implement Index Scan for O(log M) lookup.
        // Currently doing full table scan (O(M)).
        
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        var decoder = new RecordDecoder();
        
        while (cursor.MoveNext())
        {
            var columns = decoder.DecodeRecord(cursor.Payload);
            
            long sourceKey = _colSource >= 0 && _colSource < columns.Length ? columns[_colSource].AsInt64() : 0;
            if (sourceKey != origin.Value) continue;
            
            long kindVal = _colKind >= 0 && _colKind < columns.Length ? columns[_colKind].AsInt64() : 0;
            if (kindFilter.HasValue && kindVal != (int)kindFilter.Value) continue;
            
            yield return MapToEdge(columns);
        }
    }

    private GraphEdge MapToEdge(ColumnValue[] columns)
    {
        long source = _colSource >= 0 && _colSource < columns.Length ? columns[_colSource].AsInt64() : 0;
        long target = _colTarget >= 0 && _colTarget < columns.Length ? columns[_colTarget].AsInt64() : 0;
        int kind = _colKind >= 0 && _colKind < columns.Length ? (int)columns[_colKind].AsInt64() : 0;
        string data = _colData >= 0 && _colData < columns.Length ? columns[_colData].AsString() : "{}";
        string id = _colId >= 0 && _colId < columns.Length ? columns[_colId].AsString() : "";
        
        var edge = new GraphEdge(
            new RecordId(_schema.EdgeTableName, id),
            new NodeKey(source),
            new NodeKey(target),
            kind,
            data)
        {
            CVN = _colCvn >= 0 && _colCvn < columns.Length ? (int)columns[_colCvn].AsInt64() : 0,
            LVN = _colLvn >= 0 && _colLvn < columns.Length ? (int)columns[_colLvn].AsInt64() : 0,
            SyncStatus = _colSync >= 0 && _colSync < columns.Length ? (int)columns[_colSync].AsInt64() : 0
        };
        
        return edge;
    }
}
