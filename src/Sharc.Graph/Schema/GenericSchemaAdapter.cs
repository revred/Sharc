// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Schema;

/// <summary>
/// A configurable schema adapter for arbitrary SQLite schemas.
/// </summary>
public sealed class GenericSchemaAdapter : ISchemaAdapter
{
    /// <inheritdoc/>
    public string NodeTableName { get; set; } = "Nodes";
    
    /// <inheritdoc/>
    public string EdgeTableName { get; set; } = "Edges";
    
    /// <inheritdoc/>
    public string? EdgeHistoryTableName { get; set; }
    
    /// <inheritdoc/>
    public string? MetaTableName { get; set; }

    /// <inheritdoc/>
    public string NodeIdColumn { get; set; } = "Id";
    
    /// <inheritdoc/>
    public string NodeKeyColumn { get; set; } = "Key";
    
    /// <inheritdoc/>
    public string NodeTypeColumn { get; set; } = "TypeId";
    
    /// <inheritdoc/>
    public string NodeDataColumn { get; set; } = "Data";
    
    /// <inheritdoc/>
    public string? NodeCvnColumn { get; set; }
    
    /// <inheritdoc/>
    public string? NodeLvnColumn { get; set; }
    
    /// <inheritdoc/>
    public string? NodeSyncColumn { get; set; }
    
    /// <inheritdoc/>
    public string? NodeUpdatedColumn { get; set; }

    /// <inheritdoc/>
    public string? NodeAliasColumn { get; set; }
    /// <inheritdoc/>
    public string? NodeTokensColumn { get; set; }

    /// <inheritdoc/>
    public string EdgeIdColumn { get; set; } = "Id";
    
    /// <inheritdoc/>
    public string EdgeOriginColumn { get; set; } = "Origin";
    
    /// <inheritdoc/>
    public string EdgeTargetColumn { get; set; } = "Target";
    
    /// <inheritdoc/>
    public string EdgeKindColumn { get; set; } = "Kind";
    
    /// <inheritdoc/>
    public string EdgeDataColumn { get; set; } = "Data";
    
    /// <inheritdoc/>
    public string? EdgeCvnColumn { get; set; }
    
    /// <inheritdoc/>
    public string? EdgeLvnColumn { get; set; }
    /// <inheritdoc/>
    public string? EdgeSyncColumn { get; set; }
    /// <inheritdoc/>
    public string? EdgeWeightColumn { get; set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> TypeNames { get; set; } = new Dictionary<int, string>();

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredIndexDDL { get; set; } = Array.Empty<string>();
}