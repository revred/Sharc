// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// Configuration for graph traversal behaviors.
/// </summary>
public readonly record struct TraversalPolicy
{
    /// <summary>Limit the number of edges to follow per node (hub capping).</summary>
    public int? MaxFanOut { get; init; }
    
    /// <summary>Only traverse to nodes of this type ID.</summary>
    public int? TargetTypeFilter { get; init; }
    
    /// <summary>Stop traversal if this node key is reached.</summary>
    public NodeKey StopAtKey { get; init; }
    
    /// <summary>Direction to follow.</summary>
    public TraversalDirection Direction { get; init; } = TraversalDirection.Outgoing;

    /// <summary>Maximum processing time or token count (future).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Maximum tokens to retrieve in this traversal.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Maximum search depth (hops).</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Minimum edge weight to follow.</summary>
    public float? MinWeight { get; init; }

    /// <summary>Default constructor.</summary>
    public TraversalPolicy() { }
}