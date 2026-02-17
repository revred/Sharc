// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Graph.Model;

/// <summary>
/// A document record stored in the _records table or adapted Entity table.
/// Supports both string and integer key addressing.
/// </summary>
public readonly record struct GraphRecord
{
    /// <summary>The dual-key identity (String ID + Integer Key).</summary>
    public RecordId Id { get; init; }
    
    /// <summary>The integer key (convenience property, same as Id.Key).</summary>
    public NodeKey Key { get; init; }
    
    /// <summary>The numeric type ID (e.g., 1 for Project, 2 for ShopJob).</summary>
    public int TypeId { get; init; }
    
    private readonly string? _jsonData;

    /// <summary>The JSON document body.</summary>
    public string JsonData => _jsonData ?? "{}";
    
    /// <summary>Cloud Version Number (for sync).</summary>
    public int CVN { get; init; }
    
    /// <summary>Local Version Number (for sync).</summary>
    public int LVN { get; init; }
    
    /// <summary>0=Synced, 1=Pending Upload.</summary>
    public int SyncStatus { get; init; }
    
    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    
    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Estimated token count (for CSE budgeting).</summary>
    public int Tokens { get; init; }

    /// <summary>Alternative identifier (e.g., username, slug).</summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Creates a new GraphRecord.
    /// </summary>
    public GraphRecord(RecordId id, NodeKey key, int typeId, string? jsonData = null, 
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null, string? alias = null)
    {
        Id = id;
        Key = key;
        TypeId = typeId;
        _jsonData = jsonData;
        CreatedAt = createdAt ?? default;
        UpdatedAt = updatedAt ?? CreatedAt;
        CVN = 0;
        LVN = 0;
        SyncStatus = 0;
        Tokens = 0;
        Alias = alias;
    }

    // Methods for JSON extraction will be added later
}