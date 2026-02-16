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
            // Streaming UNION ALL (2-way): zero-materialization concatenation
            if (StreamingUnionExecutor.CanStreamUnionAll(plan.Compound!, cteResults))
                return StreamingUnionExecutor.StreamingUnionAll(db, plan.Compound!, parameters);

            // Streaming UNION ALL + ORDER BY + LIMIT: concat reader → TopN heap
            if (StreamingUnionExecutor.CanStreamUnionAllTopN(plan.Compound!, cteResults))
                return StreamingUnionExecutor.StreamingUnionAllTopN(db, plan.Compound!, parameters);

            // Streaming chained UNION ALL (N-way): flatten to streaming concat chain
            if (CanStreamChainedUnionAll(plan.Compound!, cteResults))
                return StreamingChainedUnionAll(db, plan.Compound!, parameters);

            // Index-based streaming: UNION/INTERSECT/EXCEPT without string materialization.
            // Indexes raw cursor bytes (FNV-1a 128-bit) to build dedup sets, then returns a
            // streaming reader that only materializes columns when the caller reads them.
            if (CanStreamSetOp(plan.Compound!, cteResults))
                return ExecuteIndexSetOp(db, plan.Compound!, parameters);

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
        // Streaming path: UNION/INTERSECT/EXCEPT with two simple sides, no CTE references.
        // Avoids full materialization of both sides — reads directly from B-tree cursors.
        if (CanStreamSetOp(plan, cteResults))
        {
            var (rows, columns) = ExecuteStreamingSetOp(db, plan, parameters);

            if (plan.FinalOrderBy is { Count: > 0 })
                QueryPostProcessor.ApplyOrderBy(rows, plan.FinalOrderBy, columns);

            if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue)
                rows = QueryPostProcessor.ApplyLimitOffset(rows, plan.FinalLimit, plan.FinalOffset);

            return (rows, columns);
        }

        // Materialized path: complex compounds, CTE references, or UNION ALL.
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

    /// <summary>
    /// Returns true when a compound plan can use streaming set operations:
    /// UNION/INTERSECT/EXCEPT, simple two-way, no CTE references on either side.
    /// </summary>
    private static bool CanStreamSetOp(
        CompoundQueryPlan plan,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        if (plan.Operator == CompoundOperator.UnionAll) return false;
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
    /// Executes a streaming set operation using <see cref="StreamingSetOpProcessor"/>.
    /// </summary>
    private static (List<QueryValue[]> rows, string[] columns) ExecuteStreamingSetOp(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        using var leftReader = ExecuteIntent(db, plan.Left, parameters);
        using var rightReader = ExecuteIntent(db, plan.RightSimple!, parameters);

        return plan.Operator switch
        {
            CompoundOperator.Union => StreamingSetOpProcessor.StreamingUnion(leftReader, rightReader),
            CompoundOperator.Intersect => StreamingSetOpProcessor.StreamingIntersect(leftReader, rightReader),
            CompoundOperator.Except => StreamingSetOpProcessor.StreamingExcept(leftReader, rightReader),
            _ => throw new NotSupportedException($"Streaming not supported for {plan.Operator}"),
        };
    }

    // ─── Chained UNION ALL streaming ────────────────────────────

    /// <summary>
    /// Returns true when an entire compound chain is UNION ALL with no CTEs,
    /// no final ORDER BY / LIMIT / OFFSET, enabling N-way streaming concat.
    /// </summary>
    private static bool CanStreamChainedUnionAll(
        CompoundQueryPlan plan,
        Dictionary<string, (QueryValue[][] rows, string[] columns)>? cteResults)
    {
        var current = plan;
        while (current != null)
        {
            if (current.Operator != CompoundOperator.UnionAll) return false;
            if (current.FinalOrderBy is { Count: > 0 }) return false;
            if (current.FinalLimit.HasValue || current.FinalOffset.HasValue) return false;

            if (cteResults != null && cteResults.ContainsKey(current.Left.TableName))
                return false;

            if (current.RightSimple != null)
            {
                if (cteResults != null && cteResults.ContainsKey(current.RightSimple.TableName))
                    return false;
                return true; // reached the leaf
            }

            current = current.RightCompound;
        }
        return false;
    }

    /// <summary>
    /// Flattens a chained UNION ALL into a streaming concat of N readers.
    /// Zero materialization — all readers stream directly from B-tree cursors.
    /// </summary>
    private static SharcDataReader StreamingChainedUnionAll(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // Collect all leaf intents
        var intents = new List<QueryIntent>();
        var current = plan;
        while (current != null)
        {
            intents.Add(current.Left);
            if (current.RightSimple != null)
            {
                intents.Add(current.RightSimple);
                break;
            }
            current = current.RightCompound;
        }

        // Build streaming concat chain: ((A, B), C), D)...
        var first = ExecuteIntent(db, intents[0], parameters);
        var columnNames = first.GetColumnNames();
        SharcDataReader result = first;

        for (int i = 1; i < intents.Count; i++)
        {
            var next = ExecuteIntent(db, intents[i], parameters);
            result = new SharcDataReader(result, next, columnNames);
        }

        return result;
    }

    // ─── Index-based streaming set ops ──────────────────────────

    /// <summary>
    /// Executes a set operation (UNION/INTERSECT/EXCEPT) using raw-byte indexing.
    /// Returns a streaming dedup reader — zero string allocation in the pipeline.
    /// Strings are only materialized when the caller calls GetString().
    /// </summary>
    private static SharcDataReader ExecuteIndexSetOp(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        SharcDataReader dedupReader;

        if (plan.Operator == CompoundOperator.Union)
        {
            // UNION: concat both readers, wrap in dedup-as-you-go
            var leftR = ExecuteIntent(db, plan.Left, parameters);
            var rightR = ExecuteIntent(db, plan.RightSimple!, parameters);
            var concat = new SharcDataReader(leftR, rightR, leftR.GetColumnNames());
            dedupReader = new SharcDataReader(concat, SetDedupMode.Union);
        }
        else
        {
            // INTERSECT / EXCEPT: build right-side index set first (zero string alloc),
            // then stream left side through dedup filter
            var rightReader = ExecuteIntent(db, plan.RightSimple!, parameters);
            var rightIndex = BuildIndexSet(rightReader);
            rightReader.Dispose();

            var leftReader = ExecuteIntent(db, plan.Left, parameters);
            var mode = plan.Operator == CompoundOperator.Intersect
                ? SetDedupMode.Intersect : SetDedupMode.Except;
            dedupReader = new SharcDataReader(leftReader, mode, rightIndex);
        }

        // Apply ORDER BY + LIMIT if present
        bool hasOrderBy = plan.FinalOrderBy is { Count: > 0 };
        bool hasLimit = plan.FinalLimit.HasValue || plan.FinalOffset.HasValue;

        if (!hasOrderBy && !hasLimit)
            return dedupReader;

        // Streaming TopN: ORDER BY + LIMIT without full materialization
        if (hasOrderBy && plan.FinalLimit.HasValue && !plan.FinalOffset.HasValue)
            return StreamingTopNProcessor.Apply(dedupReader, plan.FinalOrderBy!, plan.FinalLimit.Value);

        // Materialize for complex cases (ORDER BY without LIMIT, OFFSET, etc.)
        var (rows, columns) = QueryPostProcessor.Materialize(dedupReader);
        dedupReader.Dispose();

        if (hasOrderBy)
            QueryPostProcessor.ApplyOrderBy(rows, plan.FinalOrderBy!, columns);
        if (hasLimit)
            rows = QueryPostProcessor.ApplyLimitOffset(rows, plan.FinalLimit, plan.FinalOffset);

        return new SharcDataReader(rows.ToArray(), columns);
    }

    /// <summary>
    /// Scans all rows in a reader and builds a pooled index set.
    /// Zero string allocation — indexes raw cursor payload bytes via FNV-1a 128-bit.
    /// ArrayPool-backed: zero net allocation after warmup.
    /// </summary>
    private static IndexSet BuildIndexSet(SharcDataReader reader)
    {
        var fset = IndexSet.Rent();
        while (reader.Read())
            fset.Add(reader.GetRowFingerprint());
        return fset;
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
        var reader = db.CreateReaderFromIntent(intent, parameters);
        return QueryPostProcessor.Apply(reader, intent);
    }

    // Forwarder — kept for backward compatibility with SharcDatabase.
    internal static List<(string table, string[]? columns)> CollectTableReferences(QueryPlan plan)
        => TableReferenceCollector.Collect(plan);
}
