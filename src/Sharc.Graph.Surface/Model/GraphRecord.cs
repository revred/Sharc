/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Graph.Model;

/// <summary>
/// A document record stored in the _records table or adapted Entity table.
/// Supports both string and integer key addressing.
/// </summary>
public sealed class GraphRecord
{
    /// <summary>The dual-key identity (String ID + Integer Key).</summary>
    public RecordId Id { get; init; }
    
    /// <summary>The integer key (convenience property, same as Id.Key).</summary>
    public NodeKey Key { get; init; }
    
    /// <summary>The numeric type ID (e.g., 1 for Project, 2 for ShopJob).</summary>
    public int TypeId { get; init; }
    
    /// <summary>The JSON document body.</summary>
    public string JsonData { get; init; }
    
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

    /// <summary>
    /// Creates a new GraphRecord.
    /// </summary>
    public GraphRecord(RecordId id, NodeKey key, int typeId, string jsonData, 
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        Id = id;
        Key = key;
        TypeId = typeId;
        JsonData = jsonData;
        CreatedAt = createdAt ?? default;
        UpdatedAt = updatedAt ?? CreatedAt;
    }

    // Methods for JSON extraction will be added later
}
