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
            cteResults = MaterializeCtes(db, plan.Ctes!, parameters);

        if (plan.IsCompound)
        {
            // Streaming UNION ALL: zero-materialization concatenation when possible
            if (CanStreamUnionAll(plan.Compound!, cteResults))
                return StreamingUnionAll(db, plan.Compound!, parameters, cteResults);

            // Streaming UNION ALL + ORDER BY + LIMIT: concat reader → TopN heap
            if (CanStreamUnionAllTopN(plan.Compound!, cteResults))
                return StreamingUnionAllTopN(db, plan.Compound!, parameters, cteResults);

            var (rows, columns) = ExecuteCompoundCore(db, plan.Compound!, parameters, cteResults);
            return new SharcDataReader(rows.ToArray(), columns);
        }

        // Simple query with CTEs
        return ExecuteSimpleWithCtes(db, plan.Simple!, parameters, cteResults!);
    }

    // ─── Streaming UNION ALL ──────────────────────────────────────

    /// <summary>
    /// Returns true when a compound plan can use zero-materialization streaming:
    /// UNION ALL, no final ORDER BY / LIMIT / OFFSET, simple two-way, no CTE references.
    /// </summary>
    private static bool CanStreamUnionAll(
        CompoundQueryPlan plan,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        if (plan.Operator != CompoundOperator.UnionAll) return false;
        if (plan.FinalOrderBy is { Count: > 0 }) return false;
        if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue) return false;
        if (plan.RightCompound != null) return false; // chained compound — fall back to materialized
        if (plan.RightSimple == null) return false;

        // If either side references a CTE, we can't stream (CTE data is pre-materialized)
        if (cteResults != null)
        {
            if (cteResults.ContainsKey(plan.Left.TableName)) return false;
            if (cteResults.ContainsKey(plan.RightSimple.TableName)) return false;
        }

        return true;
    }

    /// <summary>
    /// Executes a two-way UNION ALL by concatenating two cursor-mode readers.
    /// Zero materialization — rows stream directly from the underlying B-tree cursors.
    /// </summary>
    private static SharcDataReader StreamingUnionAll(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        var leftReader = ExecuteIntentAsReader(db, plan.Left, parameters);
        var rightReader = ExecuteIntentAsReader(db, plan.RightSimple!, parameters);

        // Column names come from the left side (SQL standard)
        int fieldCount = leftReader.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = leftReader.GetColumnName(i);

        return new SharcDataReader(leftReader, rightReader, columnNames);
    }

    /// <summary>
    /// Returns true when a compound plan can use streaming UNION ALL + TopN:
    /// UNION ALL with ORDER BY + LIMIT (no OFFSET), simple two-way, no CTE references.
    /// </summary>
    private static bool CanStreamUnionAllTopN(
        CompoundQueryPlan plan,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        if (plan.Operator != CompoundOperator.UnionAll) return false;
        if (plan.FinalOrderBy is not { Count: > 0 }) return false;
        if (!plan.FinalLimit.HasValue) return false;
        if (plan.FinalOffset.HasValue) return false;
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        if (cteResults != null)
        {
            if (cteResults.ContainsKey(plan.Left.TableName)) return false;
            if (cteResults.ContainsKey(plan.RightSimple.TableName)) return false;
        }

        return true;
    }

    /// <summary>
    /// Executes UNION ALL + ORDER BY + LIMIT by concatenating two readers
    /// and feeding through the streaming TopN heap with fast rejection.
    /// </summary>
    private static SharcDataReader StreamingUnionAllTopN(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        var leftReader = ExecuteIntentAsReader(db, plan.Left, parameters);
        var rightReader = ExecuteIntentAsReader(db, plan.RightSimple!, parameters);

        int fieldCount = leftReader.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = leftReader.GetColumnName(i);

        var concatReader = new SharcDataReader(leftReader, rightReader, columnNames);
        return QueryPostProcessor.ApplyStreamingTopN(
            concatReader, plan.FinalOrderBy!, plan.FinalLimit!.Value);
    }

    /// <summary>
    /// Executes a single <see cref="QueryIntent"/> and returns the reader directly
    /// (with post-processing applied). Used by the streaming UNION ALL path.
    /// </summary>
    private static SharcDataReader ExecuteIntentAsReader(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        string[]? columns = intent.HasAggregates
            ? QueryPostProcessor.ComputeAggregateProjection(intent)
            : intent.Columns is { Count: > 0 } ? [.. intent.Columns] : null;

        IFilterStar? filter = intent.Filter.HasValue
            ? IntentToFilterBridge.Build(intent.Filter.Value, parameters)
            : null;

        var reader = db.CreateReader(intent.TableName, columns, null, filter);
        return QueryPostProcessor.Apply(reader, intent);
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

        var combined = ApplySetOperation(plan.Operator, leftRows, rightRows, leftCount);

        if (plan.FinalOrderBy is { Count: > 0 })
            QueryPostProcessor.ApplyOrderBy(combined, plan.FinalOrderBy, leftColumns);

        if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue)
            combined = QueryPostProcessor.ApplyLimitOffset(combined, plan.FinalLimit, plan.FinalOffset);

        return (combined, leftColumns);
    }

    // ─── CTE execution ──────────────────────────────────────────

    private static Dictionary<string, (QueryValue[][] rows, string[] columns)> MaterializeCtes(
        SharcDatabase db,
        IReadOnlyList<CteIntent> ctes,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var results = new Dictionary<string, (QueryValue[][] rows, string[] columns)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var cte in ctes)
        {
            var (rows, columns) = ExecuteAndMaterialize(db, cte.Query, parameters, results);
            results[cte.Name] = (rows.ToArray(), columns);
        }

        return results;
    }

    private static SharcDataReader ExecuteSimpleWithCtes(
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
                rows = QueryPostProcessor.ApplyDistinct(rows, columnNames.Length);

            if (needsSort)
                QueryPostProcessor.ApplyOrderBy(rows, intent.OrderBy!, columnNames);

            if (needsLimit)
                rows = QueryPostProcessor.ApplyLimitOffset(rows, intent.Limit, intent.Offset);

            return new SharcDataReader(rows.ToArray(), columnNames);
        }

        return ExecuteIntent(db, intent, parameters);
    }

    // ─── Sub-query execution ─────────────────────────────────────

    private static (List<QueryValue[]> rows, string[] columns) ExecuteAndMaterialize(
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
                rows = QueryPostProcessor.ApplyDistinct(rows, columnNames.Length);

            if (needsSort)
                QueryPostProcessor.ApplyOrderBy(rows, intent.OrderBy!, columnNames);

            if (needsLimit)
                rows = QueryPostProcessor.ApplyLimitOffset(rows, intent.Limit, intent.Offset);

            return (rows, columnNames);
        }

        using var reader = ExecuteIntent(db, intent, parameters);
        return Materialize(reader);
    }

    private static SharcDataReader ExecuteIntent(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        string[]? columns = intent.HasAggregates
            ? QueryPostProcessor.ComputeAggregateProjection(intent)
            : intent.Columns is { Count: > 0 } ? [.. intent.Columns] : null;

        IFilterStar? filter = intent.Filter.HasValue
            ? IntentToFilterBridge.Build(intent.Filter.Value, parameters)
            : null;

        var reader = db.CreateReader(intent.TableName, columns, null, filter);
        return QueryPostProcessor.Apply(reader, intent);
    }

    // ─── Set operations ──────────────────────────────────────────

    internal static List<QueryValue[]> ApplySetOperation(
        CompoundOperator op,
        List<QueryValue[]> left,
        List<QueryValue[]> right,
        int columnCount)
    {
        return op switch
        {
            CompoundOperator.UnionAll => UnionAll(left, right),
            CompoundOperator.Union => Union(left, right, columnCount),
            CompoundOperator.Intersect => Intersect(left, right, columnCount),
            CompoundOperator.Except => Except(left, right, columnCount),
            _ => throw new NotSupportedException($"Unknown compound operator: {op}"),
        };
    }

    private static List<QueryValue[]> UnionAll(List<QueryValue[]> left, List<QueryValue[]> right)
    {
        var result = new List<QueryValue[]>(left.Count + right.Count);
        result.AddRange(left);
        result.AddRange(right);
        return result;
    }

    private static List<QueryValue[]> Union(List<QueryValue[]> left, List<QueryValue[]> right, int colCount)
    {
        var comparer = new QueryPostProcessor.QvRowEqualityComparer(colCount);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new List<QueryValue[]>(left.Count + right.Count);

        foreach (var row in left)
        {
            if (seen.Add(row))
                result.Add(row);
        }
        foreach (var row in right)
        {
            if (seen.Add(row))
                result.Add(row);
        }
        return result;
    }

    private static List<QueryValue[]> Intersect(List<QueryValue[]> left, List<QueryValue[]> right, int colCount)
    {
        var comparer = new QueryPostProcessor.QvRowEqualityComparer(colCount);
        var rightSet = new HashSet<QueryValue[]>(right, comparer);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new List<QueryValue[]>();

        foreach (var row in left)
        {
            if (rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
        }
        return result;
    }

    private static List<QueryValue[]> Except(List<QueryValue[]> left, List<QueryValue[]> right, int colCount)
    {
        var comparer = new QueryPostProcessor.QvRowEqualityComparer(colCount);
        var rightSet = new HashSet<QueryValue[]>(right, comparer);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new List<QueryValue[]>();

        foreach (var row in left)
        {
            if (!rightSet.Contains(row) && seen.Add(row))
                result.Add(row);
        }
        return result;
    }

    // ─── Materialization ─────────────────────────────────────────

    private static (List<QueryValue[]> rows, string[] columns) Materialize(SharcDataReader reader)
    {
        int fieldCount = reader.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = reader.GetColumnName(i);

        var rows = new List<QueryValue[]>();
        while (reader.Read())
        {
            var row = new QueryValue[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                var type = reader.GetColumnType(i);
                row[i] = type switch
                {
                    SharcColumnType.Integral => QueryValue.FromInt64(reader.GetInt64(i)),
                    SharcColumnType.Real => QueryValue.FromDouble(reader.GetDouble(i)),
                    SharcColumnType.Text => QueryValue.FromString(reader.GetString(i)),
                    SharcColumnType.Blob => QueryValue.FromBlob(reader.GetBlob(i)),
                    _ => QueryValue.Null,
                };
            }
            rows.Add(row);
        }

        return (rows, columnNames);
    }

    // ─── Table reference collection ──────────────────────────────

    /// <summary>
    /// Collects all table references from a <see cref="QueryPlan"/> for entitlement enforcement.
    /// </summary>
    internal static List<(string table, string[]? columns)> CollectTableReferences(QueryPlan plan)
    {
        var tables = new List<(string, string[]?)>();

        if (plan.HasCtes)
        {
            foreach (var cte in plan.Ctes!)
                tables.Add((cte.Query.TableName, cte.Query.Columns is { Count: > 0 } ? [.. cte.Query.Columns] : null));
        }

        if (plan.IsCompound)
            CollectFromCompound(plan.Compound!, tables);
        else if (plan.Simple is not null)
            tables.Add((plan.Simple.TableName, plan.Simple.Columns is { Count: > 0 } ? [.. plan.Simple.Columns] : null));

        return tables;
    }

    private static void CollectFromCompound(CompoundQueryPlan plan, List<(string, string[]?)> tables)
    {
        tables.Add((plan.Left.TableName, plan.Left.Columns is { Count: > 0 } ? [.. plan.Left.Columns] : null));

        if (plan.RightCompound != null)
            CollectFromCompound(plan.RightCompound, tables);
        else if (plan.RightSimple != null)
            tables.Add((plan.RightSimple.TableName, plan.RightSimple.Columns is { Count: > 0 } ? [.. plan.RightSimple.Columns] : null));
    }
}
