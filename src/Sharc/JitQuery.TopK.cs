// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Views;

namespace Sharc;

public sealed partial class JitQuery
{
    /// <summary>
    /// Executes the query with accumulated filters and returns the K best-scoring rows.
    /// This is a terminal method — it runs the query immediately and returns a reader.
    /// </summary>
    /// <remarks>
    /// <para>The scorer runs on each row after all filters (FilterStar, IRowAccessEvaluator)
    /// have been applied. Rows that score worse than the current worst in the heap are
    /// never materialized, keeping memory bounded to O(K).</para>
    /// <para>Lower scores rank higher (natural for distance-based scoring).</para>
    /// <para>The returned reader contains exactly min(K, matchCount) rows sorted
    /// ascending by score (best first).</para>
    /// </remarks>
    /// <param name="k">Number of top-scoring rows to return. Must be greater than zero.</param>
    /// <param name="scorer">The scoring function. Lower scores are better.</param>
    /// <param name="columns">Column names to project, or empty/null for all columns.</param>
    /// <returns>A <see cref="SharcDataReader"/> containing at most K rows sorted by score ascending.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is zero or negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scorer"/> is null.</exception>
    public SharcDataReader TopK(int k, IRowScorer scorer, params string[]? columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(k, 0);
        ArgumentNullException.ThrowIfNull(scorer);

        var reader = Query(columns);
        return ScoredTopKProcessor.Apply(reader, k, scorer);
    }

    /// <summary>
    /// Executes the query with accumulated filters and returns the K best-scoring rows
    /// using a lambda scoring function.
    /// </summary>
    /// <param name="k">Number of top-scoring rows to return. Must be greater than zero.</param>
    /// <param name="scorer">Lambda that computes a score for each row. Lower scores are better.</param>
    /// <param name="columns">Column names to project, or empty/null for all columns.</param>
    /// <returns>A <see cref="SharcDataReader"/> containing at most K rows sorted by score ascending.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is zero or negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scorer"/> is null.</exception>
    public SharcDataReader TopK(int k, Func<IRowAccessor, double> scorer, params string[]? columns)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        return TopK(k, new DelegateRowScorer(scorer), columns);
    }

    private sealed class DelegateRowScorer(Func<IRowAccessor, double> fn) : IRowScorer
    {
        public double Score(IRowAccessor row) => fn(row);
    }
}
