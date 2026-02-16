// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using IntentPredicateNode = Sharc.Query.Intent.PredicateNode;

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
        // ─── Lazy CTE: resolve references into real table intents ────
        // Instead of materializing all CTE rows upfront, inline simple CTEs
        // so streaming paths (concat, TopN, index-based set ops) remain available.
        if (plan.HasCtes)
        {
            var cteMap = BuildCteIntentMap(plan.Ctes!);

            if (!plan.IsCompound)
            {
                // CTE → SELECT WHERE: single cursor with merged filters
                if (CanResolveCteSimple(plan.Simple!, cteMap))
                    return ExecuteResolvedSimple(db, plan.Simple!, cteMap, parameters);
            }
            else if (CanResolveCompound(plan.Compound!, cteMap))
            {
                // CTE + compound: resolve references, then use streaming paths
                var resolved = ResolveCompound(plan.Compound!, cteMap);
                return ExecuteCompoundResolved(db, resolved, parameters);
            }
        }

        // ─── Fallback: materialize CTEs for complex cases ────────────
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
            if (CanStreamSetOp(plan.Compound!, cteResults))
                return ExecuteIndexSetOp(db, plan.Compound!, parameters);

            var (rows, columns) = ExecuteCompoundCore(db, plan.Compound!, parameters, cteResults);
            return new SharcDataReader(rows.ToArray(), columns);
        }

        // Simple query with CTEs
        return CteExecutor.ExecuteSimpleWithCtes(db, plan.Simple!, parameters, cteResults!);
    }

    // ─── Lazy CTE resolution ─────────────────────────────────────

    /// <summary>
    /// Builds a lookup of CTE name → query intent for lazy resolution.
    /// </summary>
    private static Dictionary<string, QueryIntent> BuildCteIntentMap(IReadOnlyList<CteIntent> ctes)
    {
        var map = new Dictionary<string, QueryIntent>(ctes.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var cte in ctes)
            map[cte.Name] = cte.Query;
        return map;
    }

    /// <summary>
    /// Returns true when a CTE query is a simple filtered table scan that can be inlined.
    /// No aggregates, DISTINCT, ORDER BY, LIMIT, or inter-CTE references.
    /// </summary>
    private static bool IsCteSimpleTableScan(QueryIntent cteQuery, Dictionary<string, QueryIntent> cteMap)
    {
        if (cteMap.ContainsKey(cteQuery.TableName)) return false; // references another CTE
        if (cteQuery.HasAggregates) return false;
        if (cteQuery.IsDistinct) return false;
        if (cteQuery.OrderBy is { Count: > 0 }) return false;
        if (cteQuery.Limit.HasValue || cteQuery.Offset.HasValue) return false;
        if (cteQuery.GroupBy is { Count: > 0 }) return false;
        if (cteQuery.HavingFilter.HasValue) return false;
        return true;
    }

    /// <summary>
    /// Returns true when a simple CTE query can be resolved to a direct cursor read.
    /// </summary>
    private static bool CanResolveCteSimple(QueryIntent outer, Dictionary<string, QueryIntent> cteMap)
    {
        if (!cteMap.TryGetValue(outer.TableName, out var cteQuery)) return false;
        return IsCteSimpleTableScan(cteQuery, cteMap);
    }

    /// <summary>
    /// Executes a CTE → SELECT WHERE as a single cursor with merged filters.
    /// Avoids materializing the CTE entirely.
    /// </summary>
    private static SharcDataReader ExecuteResolvedSimple(
        SharcDatabase db, QueryIntent outer, Dictionary<string, QueryIntent> cteMap,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var resolved = ResolveSingleIntent(outer, cteMap);
        return ExecuteIntent(db, resolved, parameters);
    }

    /// <summary>
    /// Returns true when all CTE references in a compound plan can be inlined.
    /// Requires a simple two-way plan with no nested compounds.
    /// </summary>
    private static bool CanResolveCompound(CompoundQueryPlan plan, Dictionary<string, QueryIntent> cteMap)
    {
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        bool leftIsCte = cteMap.ContainsKey(plan.Left.TableName);
        bool rightIsCte = cteMap.ContainsKey(plan.RightSimple.TableName);

        if (!leftIsCte && !rightIsCte) return false;
        if (leftIsCte && !IsCteSimpleTableScan(cteMap[plan.Left.TableName], cteMap)) return false;
        if (rightIsCte && !IsCteSimpleTableScan(cteMap[plan.RightSimple.TableName], cteMap)) return false;

        return true;
    }

    /// <summary>
    /// Replaces CTE table references with real table intents, merging filters.
    /// </summary>
    private static CompoundQueryPlan ResolveCompound(
        CompoundQueryPlan plan, Dictionary<string, QueryIntent> cteMap)
    {
        return new CompoundQueryPlan
        {
            Left = ResolveSingleIntent(plan.Left, cteMap),
            Operator = plan.Operator,
            RightSimple = plan.RightSimple != null ? ResolveSingleIntent(plan.RightSimple, cteMap) : null,
            RightCompound = plan.RightCompound,
            FinalOrderBy = plan.FinalOrderBy,
            FinalLimit = plan.FinalLimit,
            FinalOffset = plan.FinalOffset,
        };
    }

    /// <summary>
    /// If the intent references a CTE, returns a new intent targeting the real table
    /// with the CTE filter and outer filter merged via AND.
    /// </summary>
    private static QueryIntent ResolveSingleIntent(QueryIntent intent, Dictionary<string, QueryIntent> cteMap)
    {
        if (!cteMap.TryGetValue(intent.TableName, out var cteQuery))
            return intent;

        return new QueryIntent
        {
            TableName = cteQuery.TableName,
            Columns = intent.Columns ?? cteQuery.Columns,
            Filter = MergeFilters(cteQuery.Filter, intent.Filter),
            OrderBy = intent.OrderBy,
            Limit = intent.Limit,
            Offset = intent.Offset,
            IsDistinct = intent.IsDistinct,
            Aggregates = intent.Aggregates,
            GroupBy = intent.GroupBy,
            HavingFilter = intent.HavingFilter,
        };
    }

    /// <summary>
    /// Combines two predicate filters with AND. Returns null if both are null.
    /// </summary>
    private static PredicateIntent? MergeFilters(PredicateIntent? cteFilter, PredicateIntent? outerFilter)
    {
        if (!cteFilter.HasValue) return outerFilter;
        if (!outerFilter.HasValue) return cteFilter;

        var cteNodes = cteFilter.Value.Nodes;
        var outerNodes = outerFilter.Value.Nodes;
        int offset = cteNodes.Length;

        // Combine: [cte nodes] [outer nodes (shifted)] [AND root]
        var combined = new IntentPredicateNode[cteNodes.Length + outerNodes.Length + 1];
        Array.Copy(cteNodes, 0, combined, 0, cteNodes.Length);

        for (int i = 0; i < outerNodes.Length; i++)
        {
            ref readonly var node = ref outerNodes[i];
            combined[offset + i] = new IntentPredicateNode
            {
                Op = node.Op,
                ColumnName = node.ColumnName,
                Value = node.Value,
                HighValue = node.HighValue,
                LeftIndex = node.LeftIndex >= 0 ? node.LeftIndex + offset : -1,
                RightIndex = node.RightIndex >= 0 ? node.RightIndex + offset : -1,
            };
        }

        combined[^1] = new IntentPredicateNode
        {
            Op = IntentOp.And,
            LeftIndex = cteFilter.Value.RootIndex,
            RightIndex = outerFilter.Value.RootIndex + offset,
        };

        return new PredicateIntent(combined, combined.Length - 1);
    }

    /// <summary>
    /// Executes a resolved compound plan through all streaming paths.
    /// Called after CTE references have been inlined into real table intents.
    /// </summary>
    private static SharcDataReader ExecuteCompoundResolved(
        SharcDatabase db, CompoundQueryPlan resolved,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // All streaming paths are available — no CTE references remain
        if (StreamingUnionExecutor.CanStreamUnionAll(resolved, null))
            return StreamingUnionExecutor.StreamingUnionAll(db, resolved, parameters);

        if (StreamingUnionExecutor.CanStreamUnionAllTopN(resolved, null))
            return StreamingUnionExecutor.StreamingUnionAllTopN(db, resolved, parameters);

        if (CanStreamChainedUnionAll(resolved, null))
            return StreamingChainedUnionAll(db, resolved, parameters);

        if (CanStreamSetOp(resolved, null))
            return ExecuteIndexSetOp(db, resolved, parameters);

        // Fallback: standard compound execution without CTE results
        var (rows, columns) = ExecuteCompoundCore(db, resolved, parameters, null);
        return new SharcDataReader(rows.ToArray(), columns);
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
        if (hasOrderBy && plan.FinalLimit.HasValue)
            return StreamingTopNProcessor.Apply(
                dedupReader, plan.FinalOrderBy!, plan.FinalLimit.Value, plan.FinalOffset ?? 0);

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
