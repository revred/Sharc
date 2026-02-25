// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Views;

namespace Sharc;

/// <summary>
/// Computes a score for a row during streaming scans.
/// Used with JitQuery.TopK to select the K best-scoring rows
/// without materializing the entire result set.
/// </summary>
/// <remarks>
/// Lower scores are considered better (natural for distance-based scoring).
/// The scorer receives an <see cref="IRowAccessor"/> that provides lazy,
/// zero-allocation access to column values — only accessed columns are decoded.
/// </remarks>
public interface IRowScorer
{
    /// <summary>
    /// Computes a score for the current row. Lower values rank higher.
    /// </summary>
    /// <param name="row">Accessor for the current row's column values.</param>
    /// <returns>A score where lower values are better (e.g., distance).</returns>
    double Score(IRowAccessor row);
}
