// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// The final output of a graph traversal operation.
/// </summary>
public readonly record struct GraphResult
{
    /// <summary>The ordered list of visited nodes.</summary>
    public IReadOnlyList<TraversalNode> Nodes { get; }

    /// <summary>
    /// Creates a new GraphResult.
    /// </summary>
    public GraphResult(IReadOnlyList<TraversalNode> nodes)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }
}