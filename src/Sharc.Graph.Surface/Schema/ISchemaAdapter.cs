// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Schema;

/// <summary>
/// Defines how the graph engine maps to underlying database tables.
/// Supports both native schema (greenfield) and adapted schema (brownfield).
/// </summary>
public interface ISchemaAdapter
{
    /// <summary>Name of the table storing nodes/entities.</summary>
    string NodeTableName { get; }
    
    /// <summary>Name of the table storing edges/links.</summary>
    string EdgeTableName { get; }
    
    /// <summary>Name of the table storing edge history (optional).</summary>
    string? EdgeHistoryTableName { get; }
    
    /// <summary>Name of the table storing metadata (optional).</summary>
    string? MetaTableName { get; }

    // --- Node Columns ---

    /// <summary>Column for the string Primary Key (GUID).</summary>
    string NodeIdColumn { get; }
    
    /// <summary>Column for the integer Key (BarID in Maker.AI).</summary>
    string NodeKeyColumn { get; }
    
    /// <summary>Column for the integer discriminator (TypeID).</summary>
    string NodeTypeColumn { get; }
    
    /// <summary>Column for the JSON payload.</summary>
    string NodeDataColumn { get; }
    
    // --- Optional Node Columns ---
    
    /// <summary>Column for Cloud Version Number.</summary>
    string? NodeCvnColumn { get; }
    
    /// <summary>Column for Local Version Number.</summary>
    string? NodeLvnColumn { get; }
    
    /// <summary>Column for Sync Status.</summary>
    string? NodeSyncColumn { get; }
    
    /// <summary>Column for Last Updated Timestamp.</summary>
    string? NodeUpdatedColumn { get; }

    /// <summary>Column for Node Alias (interned concepts).</summary>
    string? NodeAliasColumn { get; }

    /// <summary>Column for estimated token count (CSE optimization).</summary>
    string? NodeTokensColumn { get; }

    // --- Edge Columns ---

    /// <summary>Column for the edge GUID.</summary>
    string EdgeIdColumn { get; }
    
    /// <summary>Column for the Origin Node's Integer Key.</summary>
    string EdgeOriginColumn { get; }
    
    /// <summary>Column for the Target Node's Integer Key.</summary>
    string EdgeTargetColumn { get; }
    
    /// <summary>Column for the Edge Kind (LinkID).</summary>
    string EdgeKindColumn { get; }
    
    /// <summary>Column for Edge JSON payload.</summary>
    string EdgeDataColumn { get; }
    
    // --- Optional Edge Columns ---
    
    /// <summary>Column for Edge Cloud Version Number.</summary>
    string? EdgeCvnColumn { get; }
    
    /// <summary>Column for Edge Local Version Number.</summary>
    string? EdgeLvnColumn { get; }
    
    /// <summary>Column for Edge Sync Status.</summary>
    string? EdgeSyncColumn { get; }

    /// <summary>Column for edge relevance weight (0.0 - 1.0).</summary>
    string? EdgeWeightColumn { get; }

    /// <summary>
    /// Registry mapping integer TypeIDs to human-readable names.
    /// </summary>
    IReadOnlyDictionary<int, string> TypeNames { get; }

    /// <summary>
    /// List of CREATE INDEX statements required for performance on this schema.
    /// </summary>
    IReadOnlyList<string> RequiredIndexDDL { get; }
}