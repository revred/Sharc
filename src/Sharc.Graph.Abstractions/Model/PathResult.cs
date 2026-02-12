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

namespace Sharc.Graph.Model;

/// <summary>
/// A path between two nodes in the graph.
/// </summary>
public sealed class PathResult
{
    /// <summary>The sequence of records from start to end.</summary>
    public IReadOnlyList<GraphRecord> Path { get; }
    
    /// <summary>The total weight or distance (if applicable).</summary>
    public float Weight { get; }

    /// <summary>
    /// Creates a new PathResult.
    /// </summary>
    public PathResult(IReadOnlyList<GraphRecord> path, float weight = 0.0f)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Weight = weight;
    }
}
