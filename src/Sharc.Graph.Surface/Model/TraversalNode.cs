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
/// A node visited during traversal, including its context.
/// </summary>
public readonly record struct TraversalNode
{
    /// <summary>The graph record at this position.</summary>
    public GraphRecord Record { get; }
    
    /// <summary>Methods/hops from the start node (0-indexed).</summary>
    public int Depth { get; }
    
    /// <summary>The full path of keys taken to reach this node.</summary>
    public IReadOnlyList<NodeKey> Path { get; }

    /// <summary>
    /// Creates a new TraversalNode.
    /// </summary>
    public TraversalNode(GraphRecord record, int depth, IReadOnlyList<NodeKey> path)
    {
        Record = record;
        Depth = depth;
        Path = path;
    }
}
