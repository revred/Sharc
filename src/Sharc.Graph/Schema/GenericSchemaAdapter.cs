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
    public IReadOnlyDictionary<int, string> TypeNames { get; set; } = new Dictionary<int, string>();

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredIndexDDL { get; set; } = Array.Empty<string>();
}
