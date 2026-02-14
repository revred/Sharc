// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// The type of modification made to the graph.
/// </summary>
public enum ChangeType
{
    /// <summary>A new record was created.</summary>
    Create,
    
    /// <summary>An existing record was updated.</summary>
    Update,
    
    /// <summary>A record was deleted.</summary>
    Delete
}