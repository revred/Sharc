// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Consistent point-in-time diagnostics for a mutable HNSW index.
/// </summary>
public readonly record struct HnswIndexSnapshot
{
    /// <summary>Monotonic index version. Increments on upsert/delete/merge.</summary>
    public long Version { get; init; }

    /// <summary>Node count in the immutable base graph.</summary>
    public int BaseNodeCount { get; init; }

    /// <summary>Visible row count after applying pending mutations.</summary>
    public int ActiveNodeCount { get; init; }

    /// <summary>Number of pending upsert entries in the mutable delta layer.</summary>
    public int PendingUpsertCount { get; init; }

    /// <summary>Number of pending tombstones in the mutable delete layer.</summary>
    public int PendingDeleteCount { get; init; }

    /// <summary>True when the mutable delta/tombstone layers are not empty.</summary>
    public bool HasPendingMutations { get; init; }

    /// <summary>FNV-1a checksum over base topology + pending mutation state.</summary>
    public uint Checksum { get; init; }
}
