/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

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

    /// <summary>
    /// Registry mapping integer TypeIDs to human-readable names.
    /// </summary>
    IReadOnlyDictionary<int, string> TypeNames { get; }

    /// <summary>
    /// List of CREATE INDEX statements required for performance on this schema.
    /// </summary>
    IReadOnlyList<string> RequiredIndexDDL { get; }
}
