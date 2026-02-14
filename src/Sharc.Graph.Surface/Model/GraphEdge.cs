// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// A typed, directed edge connecting two graph nodes using integer keys.
/// </summary>
public sealed class GraphEdge
{
    /// <summary>The unique Edge ID.</summary>
    public RecordId Id { get; init; }
    
    /// <summary>The integer key of the origin node.</summary>
    public NodeKey OriginKey { get; init; }
    
    /// <summary>The integer key of the target node.</summary>
    public NodeKey TargetKey { get; init; }
    
    /// <summary>The edge kind/link ID (e.g. 1015).</summary>
    public int Kind { get; init; }
    
    /// <summary>The human-readable edge kind ("has_operation").</summary>
    public string KindName { get; init; } = "";
    
    /// <summary>Edge properties as JSON.</summary>
    public string JsonData { get; init; } = "{}";
    
    /// <summary>Cloud Version Number.</summary>
    public int CVN { get; init; }
    
    /// <summary>Local Version Number.</summary>
    public int LVN { get; init; }
    
    /// <summary>Sync status (0=Synced).</summary>
    public int SyncStatus { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Edge relevance weight (0.0 to 1.0).</summary>
    public float Weight { get; init; } = 1.0f;

    /// <summary>
    /// Creates a new GraphEdge connecting origin and target.
    /// </summary>
    public GraphEdge(RecordId id, NodeKey originKey, NodeKey targetKey, int kind, string jsonData = "{}")
    {
        Id = id;
        OriginKey = originKey;
        TargetKey = targetKey;
        Kind = kind;
        JsonData = jsonData;
        CreatedAt = default;
    }
}