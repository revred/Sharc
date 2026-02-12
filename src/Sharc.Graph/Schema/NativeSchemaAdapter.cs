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
/// Default schema for new Sharc Graph databases.
/// Matches the specification in documentation.
/// </summary>
public sealed class NativeSchemaAdapter : ISchemaAdapter
{
    // --- Table Names ---
    /// <inheritdoc/>
    public string NodeTableName => "_concepts";
    
    /// <inheritdoc/>
    public string EdgeTableName => "_relations";
    
    /// <inheritdoc/>
    public string? EdgeHistoryTableName => "_relations_history"; // Optional
    
    /// <inheritdoc/>
    public string? MetaTableName => "_meta";

    // --- Node Columns ---
    /// <inheritdoc/>
    public string NodeIdColumn => "id";
    /// <inheritdoc/>
    public string NodeKeyColumn => "key";
    /// <inheritdoc/>
    public string NodeTypeColumn => "kind";
    /// <inheritdoc/>
    public string NodeDataColumn => "data";
    /// <inheritdoc/>
    public string? NodeCvnColumn => "cvn";
    /// <inheritdoc/>
    public string? NodeLvnColumn => "lvn";
    /// <inheritdoc/>
    public string? NodeSyncColumn => "sync_status";
    /// <inheritdoc/>
    public string? NodeUpdatedColumn => "updated_at";

    // --- Edge Columns ---
    /// <inheritdoc/>
    public string EdgeIdColumn => "id";
    /// <inheritdoc/>
    public string EdgeOriginColumn => "source_key";
    /// <inheritdoc/>
    public string EdgeTargetColumn => "target_key";
    /// <inheritdoc/>
    public string EdgeKindColumn => "kind";
    /// <inheritdoc/>
    public string EdgeDataColumn => "data";
    /// <inheritdoc/>
    public string? EdgeCvnColumn => "cvn";
    /// <inheritdoc/>
    public string? EdgeLvnColumn => "lvn";
    /// <inheritdoc/>
    public string? EdgeSyncColumn => "sync_status";

    // --- Type Registry ---
    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> TypeNames { get; } = new Dictionary<int, string>();

    // --- Index Definitions ---
    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredIndexDDL => new[]
    {
        // Ensure Alias uniqueness for interned concepts
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_concepts_alias ON _concepts(alias) WHERE alias IS NOT NULL",
        
        // Ensure Key uniqueness (even though it's likely INTEGER PRIMARY KEY or similar, but spec says `id` is PK (GUID))
        // `id` is PK (GUID). `key` is INTEGER UNIQUE.
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_concepts_key ON _concepts(key)",
        
        // Fast outgoing traversal
        "CREATE INDEX IF NOT EXISTS idx_relations_source_kind ON _relations(source_key, kind, target_key)",
        
        // Fast incoming traversal
        "CREATE INDEX IF NOT EXISTS idx_relations_target_kind ON _relations(target_key, kind, source_key)"
    };
}
