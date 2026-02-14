// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// Direction to traverse the graph relative to the start node.
/// </summary>
public enum TraversalDirection
{
    /// <summary>Follow edges from Origin to Target.</summary>
    Outgoing,
    
    /// <summary>Follow edges from Target to Origin.</summary>
    Incoming,
    
    /// <summary>Follow edges in both directions.</summary>
    Both
}