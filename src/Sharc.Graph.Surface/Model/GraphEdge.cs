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

namespace Sharc.Graph.Model;

/// <summary>
/// A typed, directed edge connecting two graph nodes using integer keys.
/// </summary>
public sealed class GraphEdge
{
    /// <summary>The unique Edge ID.</summary>
    public RecordId Id { get; init; }
    
    /// <summary>The integer key of the origin node.</summary>
    public NodeKey OriginKey { get; init; }
    
    /// <summary>The integer key of the target node.</summary>
    public NodeKey TargetKey { get; init; }
    
    /// <summary>The edge kind/link ID (e.g. 1015).</summary>
    public int Kind { get; init; }
    
    /// <summary>The human-readable edge kind ("has_operation").</summary>
    public string KindName { get; init; } = "";
    
    /// <summary>Edge properties as JSON.</summary>
    public string JsonData { get; init; } = "{}";
    
    /// <summary>Cloud Version Number.</summary>
    public int CVN { get; init; }
    
    /// <summary>Local Version Number.</summary>
    public int LVN { get; init; }
    
    /// <summary>Sync status (0=Synced).</summary>
    public int SyncStatus { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Creates a new GraphEdge connecting origin and target.
    /// </summary>
    public GraphEdge(RecordId id, NodeKey originKey, NodeKey targetKey, int kind, string jsonData = "{}")
    {
        Id = id;
        OriginKey = originKey;
        TargetKey = targetKey;
        Kind = kind;
        JsonData = jsonData;
        CreatedAt = default;
    }
}
