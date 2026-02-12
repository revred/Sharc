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

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// The primary entry point for graph traversal and context retrieval.
/// </summary>
public interface IContextGraph
{
    /// <summary>
    /// Traverses the graph starting from a specific node using the defined policy.
    /// </summary>
    /// <param name="startKey">The starting node key.</param>
    /// <param name="policy">Traversal configuration.</param>
    /// <returns>A result set containing visited nodes and metadata.</returns>
    GraphResult Traverse(NodeKey startKey, TraversalPolicy policy);

    /// <summary>
    /// Retrieves a single node record by its integer key.
    /// </summary>
    GraphRecord? GetNode(NodeKey key);

    /// <summary>
    /// Retrieves a single node record by its ID.
    /// </summary>
    GraphRecord? GetNode(RecordId id);

    /// <summary>
    /// Retrieves edges originating from the specified node.
    /// </summary>
    IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kind = null);
}
