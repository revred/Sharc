// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Optional execution controls for vector query execution paths.
/// </summary>
public readonly record struct VectorSearchOptions
{
    /// <summary>
    /// If true, bypasses HNSW/indexed paths and forces flat scan execution.
    /// Useful for deterministic validation and planner A/B checks.
    /// </summary>
    public bool ForceFlatScan { get; init; }

    /// <summary>
    /// Optional override for HNSW ef-search beam width.
    /// Null uses the index default.
    /// </summary>
    public int? EfSearch { get; init; }

    /// <summary>Default options (planner chooses strategy, index default ef).</summary>
    public static VectorSearchOptions Default => default;
}
