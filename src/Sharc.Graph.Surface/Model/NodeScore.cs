// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Graph.Model;

/// <summary>
/// A node with an associated score from a graph algorithm (e.g., PageRank).
/// </summary>
/// <param name="Key">The node key.</param>
/// <param name="Score">The computed score.</param>
public readonly record struct NodeScore(NodeKey Key, double Score);
