// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Graph.Model;

namespace Sharc.Graph.Cypher;

/// <summary>
/// Compiled Cypher execution plan. Compiles down to existing Traverse() or ShortestPath() calls.
/// Designed for closure capture — bind parameters at compile time, execute many times.
/// </summary>
internal sealed class CypherPlan
{
    /// <summary>Start node key (from WHERE constraint).</summary>
    public NodeKey? StartKey { get; set; }

    /// <summary>End node key (for shortestPath).</summary>
    public NodeKey? EndKey { get; set; }

    /// <summary>Whether this is a shortestPath query.</summary>
    public bool IsShortestPath { get; set; }

    /// <summary>Traversal policy built from the Cypher pattern.</summary>
    public TraversalPolicy Policy { get; set; }

    /// <summary>Variables to return from the RETURN clause.</summary>
    public List<string> ReturnVariables { get; set; } = new();
}
