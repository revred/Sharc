// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// Represents a work item in the graph traversal BFS queue.
/// Replaces ValueTuple (NodeKey, int, int).
/// </summary>
/// <param name="Key">The node key to visit.</param>
/// <param name="Depth">The depth of this node relative to the start node.</param>
/// <param name="PathIndex">The index into the path reconstruction list for this node's history.</param>
internal readonly record struct TraversalQueueItem(
    NodeKey Key,
    int Depth,
    int PathIndex
);
