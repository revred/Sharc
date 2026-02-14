// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// A notification payload for a data mutation (write path).
/// </summary>
public readonly record struct ChangeEvent
{
    /// <summary>The type of change (Create, Update, Delete).</summary>
    public ChangeType Type { get; }
    
    /// <summary>The ID of the record that changed.</summary>
    public RecordId Id { get; }
    
    /// <summary>The state before the change (null for Create).</summary>
    public GraphRecord? Before { get; }
    
    /// <summary>The state after the change (null for Delete).</summary>
    public GraphRecord? After { get; }

    /// <summary>
    /// Creates a new ChangeEvent.
    /// </summary>
    public ChangeEvent(ChangeType type, RecordId id, GraphRecord? before, GraphRecord? after)
    {
        Type = type;
        Id = id;
        Before = before;
        After = after;
    }
}