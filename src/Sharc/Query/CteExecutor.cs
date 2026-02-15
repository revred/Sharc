// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Executes queries that reference Common Table Expressions (CTEs).
/// Materializes CTE results first, then executes the main query against them.
/// </summary>
internal static class CteExecutor
{
    /// <summary>
    /// Materializes all CTEs in order, returning a lookup of CTE name → (rows, columns).
    /// </summary>
    internal static Dictionary<string, (QueryValue[][] rows, string[] columns)> MaterializeCtes(
        SharcDatabase db,
        IReadOnlyList<CteIntent> ctes,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var results = new Dictionary<string, (QueryValue[][] rows, string[] columns)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var cte in ctes)
        {
            var (rows, columns) = CompoundQueryExecutor.ExecuteAndMaterialize(
                db, cte.Query, parameters, results);
            results[cte.Name] = (rows.ToArray(), columns);
        }

        return results;
    }

    /// <summary>
    /// Executes a simple query that may reference CTE results.
    /// If the query's table name matches a CTE, uses the pre-materialized CTE data.
    /// </summary>
    internal static SharcDataReader ExecuteSimpleWithCtes(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, (QueryValue[][] rows, string[] columns)> cteResults)
    {
        if (cteResults.TryGetValue(intent.TableName, out var cteData))
        {
            bool needsSort = intent.OrderBy is { Count: > 0 };
            bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;
            bool needsAggregate = intent.HasAggregates;
            bool needsDistinct = intent.IsDistinct;

            if (!needsAggregate && !needsDistinct && !needsSort && !needsLimit)
                return new SharcDataReader(cteData.rows, cteData.columns);

            var rows = new List<QueryValue[]>(cteData.rows);
            var columnNames = cteData.columns;

            if (needsAggregate)
            {
                (rows, columnNames) = AggregateProcessor.Apply(
                    rows, columnNames,
                    intent.Aggregates!,
                    intent.GroupBy,
                    intent.Columns);
            }

            if (needsDistinct)
                rows = SetOperationProcessor.ApplyDistinct(rows, columnNames.Length);

            if (needsSort)
                QueryPostProcessor.ApplyOrderBy(rows, intent.OrderBy!, columnNames);

            if (needsLimit)
                rows = QueryPostProcessor.ApplyLimitOffset(rows, intent.Limit, intent.Offset);

            return new SharcDataReader(rows.ToArray(), columnNames);
        }

        return CompoundQueryExecutor.ExecuteIntent(db, intent, parameters);
    }
}
