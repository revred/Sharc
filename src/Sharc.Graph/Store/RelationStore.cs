// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;
using Sharc.Core.Schema;

namespace Sharc.Graph.Store;

internal sealed class RelationStore
{
    private readonly IBTreeReader _reader;
    public IBTreeReader Reader => _reader;
    private readonly ISchemaAdapter _schema;
    private readonly RecordDecoder _decoder = new();
    private int _tableRootPage;
    private int _columnCount;
    private string _tableName = "";

    // Indices
    private int _originIndexRootPage = -1;
    private int _targetIndexRootPage = -1;

    // Ordinals
    private int _colId = -1;
    private int _colOrigin = -1;
    private int _colTarget = -1;
    private int _colKind = -1;
    private int _colData = -1;
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
        _tableName = _schema.EdgeTableName;
        var table = schemaInfo.GetTable(_tableName);
        _tableRootPage = table.RootPage;
        _columnCount = table.Columns.Count;

        _colId = GetOrdinal(table, _schema.EdgeIdColumn);
        _colOrigin = GetOrdinal(table, _schema.EdgeOriginColumn);
        _colTarget = GetOrdinal(table, _schema.EdgeTargetColumn);
        _colKind = GetOrdinal(table, _schema.EdgeKindColumn);
        _colData = GetOrdinal(table, _schema.EdgeDataColumn);

        _colCvn = _schema.EdgeCvnColumn != null ? GetOrdinal(table, _schema.EdgeCvnColumn) : -1;
        _colLvn = _schema.EdgeLvnColumn != null ? GetOrdinal(table, _schema.EdgeLvnColumn) : -1;
        _colSync = _schema.EdgeSyncColumn != null ? GetOrdinal(table, _schema.EdgeSyncColumn) : -1;
        _colWeight = _schema.EdgeWeightColumn != null ? GetOrdinal(table, _schema.EdgeWeightColumn) : -1;

        // Origin Index
        var originIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_tableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.EdgeOriginColumn, StringComparison.OrdinalIgnoreCase));
        _originIndexRootPage = originIndex?.RootPage ?? -1;

        // Target Index
        var targetIndex = schemaInfo.Indexes.FirstOrDefault(idx =>
            idx.TableName.Equals(_tableName, StringComparison.OrdinalIgnoreCase) &&
            idx.Columns.Count > 0 &&
            idx.Columns[0].Name.Equals(_schema.EdgeTargetColumn, StringComparison.OrdinalIgnoreCase));
        _targetIndexRootPage = targetIndex?.RootPage ?? -1;
    }

    private static int GetOrdinal(TableInfo table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        if (col == null) return -1;
        return col.Ordinal;
    }

    public IEdgeCursor CreateEdgeCursor(NodeKey origin, RelationKind? kind = null)
    {
        int? kindVal = (int?)kind;
        if (_originIndexRootPage > 0)
            return new IndexEdgeCursor(_reader, (uint)_originIndexRootPage, origin.Value, kindVal, _decoder, _columnCount, true, 
                _colOrigin, _colTarget, _colKind, _colData, _colCvn, _colLvn, _colSync, _colWeight, (uint)_tableRootPage);
        
        return new TableScanEdgeCursor(_reader, (uint)_tableRootPage, origin.Value, kindVal, true, _decoder, _columnCount, 
            _colOrigin, _colTarget, _colKind, _colData, _colCvn, _colLvn, _colSync, _colWeight);
    }

    public IEdgeCursor CreateIncomingEdgeCursor(NodeKey target, RelationKind? kind = null)
    {
        int? kindVal = (int?)kind;
        if (_targetIndexRootPage > 0)
            return new IndexEdgeCursor(_reader, (uint)_targetIndexRootPage, target.Value, kindVal, _decoder, _columnCount, false, 
                _colOrigin, _colTarget, _colKind, _colData, _colCvn, _colLvn, _colSync, _colWeight, (uint)_tableRootPage);

        return new TableScanEdgeCursor(_reader, (uint)_tableRootPage, target.Value, kindVal, false, _decoder, _columnCount, 
            _colOrigin, _colTarget, _colKind, _colData, _colCvn, _colLvn, _colSync, _colWeight);
    }
}
