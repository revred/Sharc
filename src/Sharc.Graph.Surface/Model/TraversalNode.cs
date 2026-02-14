// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
    public IReadOnlyList<NodeKey>? Path { get; }

    /// <summary>
    /// Creates a new TraversalNode.
    /// </summary>
    public TraversalNode(GraphRecord record, int depth, IReadOnlyList<NodeKey>? path)
    {
        Record = record;
        Depth = depth;
        Path = path;
    }
}