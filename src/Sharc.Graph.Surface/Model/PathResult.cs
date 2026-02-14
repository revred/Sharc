// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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