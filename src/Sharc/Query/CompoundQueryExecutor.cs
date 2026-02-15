// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Executes compound (UNION/INTERSECT/EXCEPT) and CTE queries by materializing
/// sub-queries into unboxed <see cref="QueryValue"/> rows and applying set operations.
/// </summary>
internal static class CompoundQueryExecutor
{
    /// <summary>
    /// Executes a <see cref="QueryPlan"/> that is compound and/or has CTEs.
    /// </summary>
    internal static SharcDataReader Execute(
        SharcDatabase db,
        QueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // Materialize CTEs
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults = null;
        if (plan.HasCtes)
            cteResults = CteExecutor.MaterializeCtes(db, plan.Ctes!, parameters);

        if (plan.IsCompound)
        {
            // Streaming UNION ALL: zero-materialization concatenation when possible
            if (StreamingUnionExecutor.CanStreamUnionAll(plan.Compound!, cteResults))
                return StreamingUnionExecutor.StreamingUnionAll(db, plan.Compound!, parameters);

            // Streaming UNION ALL + ORDER BY + LIMIT: concat reader → TopN heap
            if (StreamingUnionExecutor.CanStreamUnionAllTopN(plan.Compound!, cteResults))
                return StreamingUnionExecutor.StreamingUnionAllTopN(db, plan.Compound!, parameters);

            var (rows, columns) = ExecuteCompoundCore(db, plan.Compound!, parameters, cteResults);
            return new SharcDataReader(rows.ToArray(), columns);
        }

        // Simple query with CTEs
        return CteExecutor.ExecuteSimpleWithCtes(db, plan.Simple!, parameters, cteResults!);
    }

    // ─── Compound execution ──────────────────────────────────────

    private static (List<QueryValue[]> rows, string[] columns) ExecuteCompoundCore(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        var (leftRows, leftColumns) = ExecuteAndMaterialize(db, plan.Left, parameters, cteResults);

        List<QueryValue[]> rightRows;
        if (plan.RightCompound != null)
        {
            (rightRows, _) = ExecuteCompoundCore(db, plan.RightCompound, parameters, cteResults);
        }
        else
        {
            (rightRows, _) = ExecuteAndMaterialize(db, plan.RightSimple!, parameters, cteResults);
        }

        int leftCount = leftColumns.Length;
        int rightCount = rightRows.Count > 0 ? rightRows[0].Length : leftCount;
        if (rightRows.Count > 0 && leftCount != rightCount)
            throw new ArgumentException(
                $"Compound query requires both sides to have the same number of columns " +
                $"(left: {leftCount}, right: {rightCount}).");

        var combined = SetOperationProcessor.Apply(plan.Operator, leftRows, rightRows, leftCount);

        if (plan.FinalOrderBy is { Count: > 0 })
            QueryPostProcessor.ApplyOrderBy(combined, plan.FinalOrderBy, leftColumns);

        if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue)
            combined = QueryPostProcessor.ApplyLimitOffset(combined, plan.FinalLimit, plan.FinalOffset);

        return (combined, leftColumns);
    }

    // ─── Sub-query execution ─────────────────────────────────────

    internal static (List<QueryValue[]> rows, string[] columns) ExecuteAndMaterialize(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        if (cteResults != null && cteResults.TryGetValue(intent.TableName, out var cteData))
        {
            var rows = new List<QueryValue[]>(cteData.rows);
            var columnNames = cteData.columns;

            bool needsAggregate = intent.HasAggregates;
            bool needsDistinct = intent.IsDistinct;
            bool needsSort = intent.OrderBy is { Count: > 0 };
            bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;

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

            return (rows, columnNames);
        }

        using var reader = ExecuteIntent(db, intent, parameters);
        return QueryPostProcessor.Materialize(reader);
    }

    internal static SharcDataReader ExecuteIntent(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        string[]? columns = intent.HasAggregates
            ? AggregateProjection.Compute(intent)
            : intent.ColumnsArray;

        IFilterStar? filter = intent.Filter.HasValue
            ? IntentToFilterBridge.Build(intent.Filter.Value, parameters)
            : null;

        var reader = db.CreateReader(intent.TableName, columns, null, filter);
        return QueryPostProcessor.Apply(reader, intent);
    }

    // Forwarder — kept for backward compatibility with SharcDatabase.
    internal static List<(string table, string[]? columns)> CollectTableReferences(QueryPlan plan)
        => TableReferenceCollector.Collect(plan);
}
