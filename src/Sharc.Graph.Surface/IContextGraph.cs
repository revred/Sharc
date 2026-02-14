// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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

    /// <summary>
    /// Retrieves edges targeting the specified node (Incoming).
    /// </summary>
    IEnumerable<GraphEdge> GetIncomingEdges(NodeKey target, RelationKind? kind = null);

    /// <summary>
    /// Creates a zero-allocation edge cursor for the specified origin node.
    /// Avoids <see cref="GraphEdge"/> allocation per row â€” callers read typed properties directly.
    /// Caller must dispose the cursor when done.
    /// </summary>
    IEdgeCursor GetEdgeCursor(NodeKey origin, RelationKind? kind = null);
}