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
/// Configuration for graph traversal behaviors.
/// </summary>
public readonly record struct TraversalPolicy
{
    /// <summary>Limit the number of edges to follow per node (hub capping).</summary>
    public int? MaxFanOut { get; init; }
    
    /// <summary>Only traverse to nodes of this type ID.</summary>
    public int? TargetTypeFilter { get; init; }
    
    /// <summary>Stop traversal if this node key is reached.</summary>
    public NodeKey StopAtKey { get; init; }
    
    /// <summary>Direction to follow.</summary>
    public TraversalDirection Direction { get; init; } = TraversalDirection.Outgoing;

    /// <summary>Maximum processing time or token count (future).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Maximum tokens to retrieve in this traversal.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Maximum search depth (hops).</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Minimum edge weight to follow.</summary>
    public float? MinWeight { get; init; }

    /// <summary>Default constructor.</summary>
    public TraversalPolicy() { }
}
