// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Computes the minimal column set needed for aggregate queries:
/// GROUP BY columns + aggregate source columns.
/// </summary>
internal static class AggregateProjection
{
    /// <summary>
    /// Returns the minimal column set needed for aggregate evaluation,
    /// or null if no specific projection is needed (e.g. COUNT(*) only).
    /// </summary>
    internal static string[]? Compute(QueryIntent intent)
    {
        if (!intent.HasAggregates) return null;

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (intent.GroupBy is { Count: > 0 })
        {
            foreach (var col in intent.GroupBy)
                needed.Add(col);
        }

        foreach (var agg in intent.Aggregates!)
        {
            if (agg.ColumnName != null)
                needed.Add(agg.ColumnName);
        }

        return needed.Count > 0 ? needed.ToArray() : null;
    }
}
