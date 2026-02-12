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

internal sealed class ConceptStore
{
    private readonly IBTreeReader _reader;
    private readonly ISchemaAdapter _schema;
    private int _tableRootPage;
    
    // Column ordinals
    private int _colId = -1;
    private int _colKey = -1;
    private int _colKind = -1;
    private int _colData = -1;
    private int _colCvn = -1;
    private int _colLvn = -1;
    private int _colSync = -1;
    
    public ConceptStore(IBTreeReader reader, ISchemaAdapter schema)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void Initialize(SharcSchema schemaInfo)
    {
        var table = schemaInfo.GetTable(_schema.NodeTableName);
        _tableRootPage = table.RootPage;
        
        _colId = GetOrdinal(table, _schema.NodeIdColumn);
        _colKey = GetOrdinal(table, _schema.NodeKeyColumn);
        _colKind = GetOrdinal(table, _schema.NodeTypeColumn);
        _colData = GetOrdinal(table, _schema.NodeDataColumn);
        
        _colCvn = _schema.NodeCvnColumn != null ? GetOrdinal(table, _schema.NodeCvnColumn) : -1;
        _colLvn = _schema.NodeLvnColumn != null ? GetOrdinal(table, _schema.NodeLvnColumn) : -1;
        _colSync = _schema.NodeSyncColumn != null ? GetOrdinal(table, _schema.NodeSyncColumn) : -1;
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
        
        using var cursor = _reader.CreateCursor((uint)_tableRootPage);
        if (cursor.Seek(key.Value))
        {
             var decoder = new RecordDecoder();
             var columns = decoder.DecodeRecord(cursor.Payload);
             return MapToRecord(columns, key);
        }
        return null;
    }

    private GraphRecord MapToRecord(ColumnValue[] columns, NodeKey key)
    {
        string id = _colId >= 0 && _colId < columns.Length ? columns[_colId].AsString() : "";
        int kind = _colKind >= 0 && _colKind < columns.Length ? (int)columns[_colKind].AsInt64() : 0;
        string data = _colData >= 0 && _colData < columns.Length ? columns[_colData].AsString() : "{}";
        
        string typeStr = _schema.TypeNames.TryGetValue(kind, out var name) 
            ? name 
            : kind.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var record = new GraphRecord(new RecordId(typeStr, id, key), key, kind, data)
        {
            CVN = _colCvn >= 0 && _colCvn < columns.Length ? (int)columns[_colCvn].AsInt64() : 0,
            LVN = _colLvn >= 0 && _colLvn < columns.Length ? (int)columns[_colLvn].AsInt64() : 0,
            SyncStatus = _colSync >= 0 && _colSync < columns.Length ? (int)columns[_colSync].AsInt64() : 0
        };
        
        return record;
    }
}
