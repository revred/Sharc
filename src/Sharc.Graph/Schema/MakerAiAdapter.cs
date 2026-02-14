// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Schema;

/// <summary>
/// Schema adapter for the Maker.AI graph database format.
/// </summary>
public sealed class MakerAiAdapter : ISchemaAdapter
{
    /// <inheritdoc/>
    public string NodeTableName => "Entity";
    /// <inheritdoc/>
    public string EdgeTableName => "Edge";
    /// <inheritdoc/>
    public string? EdgeHistoryTableName => "EdgeHistory";
    /// <inheritdoc/>
    public string? MetaTableName => null;

    // --- Node Columns ---
    /// <inheritdoc/>
    public string NodeIdColumn => "GUID";
    /// <inheritdoc/>
    public string NodeKeyColumn => "BarID";
    /// <inheritdoc/>
    public string NodeTypeColumn => "TypeID";
    /// <inheritdoc/>
    public string NodeDataColumn => "Details";
    /// <inheritdoc/>
    public string? NodeCvnColumn => "CVN";
    /// <inheritdoc/>
    public string? NodeLvnColumn => "LVN";
    /// <inheritdoc/>
    public string? NodeSyncColumn => "SyncStatus";
    /// <inheritdoc/>
    public string? NodeUpdatedColumn => "LastUpdatedUTC";
    /// <inheritdoc/>
    public string? NodeAliasColumn => null;
    /// <inheritdoc/>
    public string? NodeTokensColumn => null;

    // --- Edge Columns ---
    /// <inheritdoc/>
    public string EdgeIdColumn => "GUID";
    /// <inheritdoc/>
    public string EdgeOriginColumn => "OriginID";
    /// <inheritdoc/>
    public string EdgeTargetColumn => "TargetID";
    /// <inheritdoc/>
    public string EdgeKindColumn => "LinkID";
    /// <inheritdoc/>
    public string EdgeDataColumn => "Details";
    /// <inheritdoc/>
    public string? EdgeCvnColumn => "CVN";
    /// <inheritdoc/>
    public string? EdgeLvnColumn => "LVN";
    /// <inheritdoc/>
    public string? EdgeSyncColumn => "SyncStatus";
    /// <inheritdoc/>
    public string? EdgeWeightColumn => null;

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>
    {
        [1] = "project", [2] = "shopjob", [3] = "operation",
        [6] = "tooltype", [7] = "machine", [9] = "masterroute",
        [11] = "masterpart", [13] = "capability", [14] = "order",
        [17] = "location", [25] = "task", [26] = "quotation",
        [27] = "quotationitem", [28] = "company", [51] = "stocktype",
        [86] = "workitem", [90] = "toolitem", [99] = "recipe",
        [101] = "billofmaterial", [102] = "opmapper", [107] = "digitalasset",
        [112] = "subcontract", [115] = "estimator", [124] = "optemplate",
    };

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredIndexDDL => new[]
    {
        "CREATE INDEX IF NOT EXISTS idx_entity_barid    ON Entity(BarID)",
        "CREATE INDEX IF NOT EXISTS idx_entity_typeid   ON Entity(TypeID)",
        "CREATE INDEX IF NOT EXISTS idx_edge_origin     ON Edge(OriginID, LinkID)",
        "CREATE INDEX IF NOT EXISTS idx_edge_target     ON Edge(TargetID, LinkID)",
        "CREATE INDEX IF NOT EXISTS idx_edge_linkid     ON Edge(LinkID)",
        "CREATE INDEX IF NOT EXISTS idx_edgehist_link   ON EdgeHistory(LinkGUID)",
    };
}