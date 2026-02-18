// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// Represents a node in the path reconstruction history.
/// </summary>
/// <param name="Key">The node key.</param>
/// <param name="ParentIndex">The index of the parent node in the path list, or -1 if start.</param>
internal readonly record struct PathReconstructionNode(
    NodeKey Key,
    int ParentIndex
);

/// <summary>
/// Represents a work item in the graph traversal BFS queue.
/// </summary>
/// <param name="Key">The node key to visit.</param>
/// <param name="Depth">The depth of this node relative to the start node.</param>
/// <param name="PathIndex">The index into the path reconstruction list for this node's history.</param>
internal readonly record struct TraversalQueueItem(
    NodeKey Key,
    int Depth,
    int PathIndex
);
