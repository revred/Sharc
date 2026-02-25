// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>
/// A single hybrid search match with fused RRF score, individual ranks, and optional metadata.
/// </summary>
/// <param name="RowId">The row ID from the database.</param>
/// <param name="Score">The fused RRF score (higher = more relevant).</param>
/// <param name="VectorRank">1-based rank from the vector search, or 0 if absent from vector results.</param>
/// <param name="TextRank">1-based rank from the text search, or 0 if absent from text results.</param>
/// <param name="Metadata">Optional metadata columns requested at search time.</param>
public readonly record struct HybridMatch(
    long RowId,
    float Score,
    int VectorRank,
    int TextRank,
    IReadOnlyDictionary<string, object?>? Metadata = null);
