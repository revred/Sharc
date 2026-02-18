// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph;

/// <summary>
/// Represents a node in the path reconstruction history.
/// Replaces ValueTuple (NodeKey, int).
/// </summary>
/// <param name="Key">The node key.</param>
/// <param name="ParentIndex">The index of the parent node in the path list, or -1 if start.</param>
internal readonly record struct PathReconstructionNode(
    NodeKey Key,
    int ParentIndex
);
