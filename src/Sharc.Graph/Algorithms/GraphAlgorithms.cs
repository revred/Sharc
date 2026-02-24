// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Algorithms;

/// <summary>
/// Graph computation algorithms: PageRank, DegreeCentrality, TopologicalSort.
/// All methods are stateless and operate via edge cursor delegates — cursors
/// are created on-demand and disposed per-node (lazy, pay-for-what-you-use).
/// </summary>
public static class GraphAlgorithms
{
    /// <summary>
    /// Computes PageRank scores for all nodes using the iterative power method.
    /// Returns scores sorted descending.
    /// </summary>
    /// <param name="nodes">All node keys in the graph.</param>
    /// <param name="outgoingEdges">Factory that creates an outgoing edge cursor for a given node.</param>
    /// <param name="options">Algorithm configuration (damping, epsilon, max iterations, kind filter).</param>
    public static IReadOnlyList<NodeScore> PageRank(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        PageRankOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(outgoingEdges);

        if (nodes.Count == 0)
            return Array.Empty<NodeScore>();

        return PageRankComputer.Compute(nodes, outgoingEdges, options);
    }

    /// <summary>
    /// Computes degree centrality for all nodes. Returns results sorted by total degree descending.
    /// </summary>
    /// <param name="nodes">All node keys in the graph.</param>
    /// <param name="outgoingEdges">Factory that creates an outgoing edge cursor for a given node.</param>
    /// <param name="incomingEdges">Factory that creates an incoming edge cursor for a given node.</param>
    /// <param name="kind">Optional edge kind filter.</param>
    public static IReadOnlyList<DegreeResult> DegreeCentrality(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        Func<NodeKey, IEdgeCursor> incomingEdges,
        RelationKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(outgoingEdges);
        ArgumentNullException.ThrowIfNull(incomingEdges);

        if (nodes.Count == 0)
            return Array.Empty<DegreeResult>();

        return DegreeCentralityComputer.Compute(nodes, outgoingEdges, incomingEdges, kind);
    }

    /// <summary>
    /// Returns nodes in topological order (dependency-first). Throws if a cycle is detected.
    /// </summary>
    /// <param name="nodes">All node keys in the graph.</param>
    /// <param name="outgoingEdges">Factory that creates an outgoing edge cursor for a given node.</param>
    /// <param name="kind">Optional edge kind filter.</param>
    /// <exception cref="InvalidOperationException">The graph contains a cycle.</exception>
    public static IReadOnlyList<NodeKey> TopologicalSort(
        IReadOnlyList<NodeKey> nodes,
        Func<NodeKey, IEdgeCursor> outgoingEdges,
        RelationKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(outgoingEdges);

        if (nodes.Count == 0)
            return Array.Empty<NodeKey>();

        return TopologicalSortComputer.Compute(nodes, outgoingEdges, kind);
    }
}
