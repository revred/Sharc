// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// Execution strategy used for the most recent vector query call.
/// </summary>
public enum VectorExecutionStrategy
{
    /// <summary>Brute-force vector scan over filtered rows.</summary>
    FlatScan = 0,
    /// <summary>Direct HNSW nearest-neighbor search without metadata enrichment.</summary>
    HnswDirect = 1,
    /// <summary>HNSW search followed by row seek for projected metadata columns.</summary>
    HnswMetadataEnrichment = 2,
    /// <summary>Filter-aware HNSW widening over a precomputed allow-list.</summary>
    HnswPostFilterWidening = 3,
    /// <summary>Radius search via HNSW widening with optional exact fallback.</summary>
    HnswWithinDistanceWidening = 4,
    /// <summary>HNSW candidate generation followed by exact reranking via custom scorer.</summary>
    HnswReranked = 5
}

/// <summary>
/// Lightweight execution diagnostics for vector query planning and benchmarking.
/// <see cref="ElapsedMs"/> captures wall-clock elapsed time in milliseconds (0 if not measured).
/// </summary>
public readonly record struct VectorExecutionInfo(
    VectorExecutionStrategy Strategy,
    int CandidateCount,
    int RequestedK,
    int ReturnedCount,
    bool UsedFallbackScan,
    double ElapsedMs = 0)
{
    /// <summary>Default diagnostics before any search is executed.</summary>
    public static VectorExecutionInfo None => new(
        Strategy: VectorExecutionStrategy.FlatScan,
        CandidateCount: 0,
        RequestedK: 0,
        ReturnedCount: 0,
        UsedFallbackScan: false,
        ElapsedMs: 0);
}
