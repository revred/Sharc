// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using IntentPredicateNode = Sharc.Query.Intent.PredicateNode;

namespace Sharc.Query;

/// <summary>
/// Executes compound (UNION/INTERSECT/EXCEPT) and Cote queries by materializing
/// sub-queries into unboxed <see cref="QueryValue"/> rows and applying set operations.
/// </summary>
internal static class CompoundQueryExecutor
{
    /// <summary>
    /// Executes a <see cref="QueryPlan"/> that is compound and/or has Cotes.
    /// </summary>
    internal static SharcDataReader Execute(
        SharcDatabase db,
        QueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // ─── Lazy Cote: resolve references into real table intents ────
        // Instead of materializing all Cote rows upfront, inline simple Cotes
        // so streaming paths (concat, TopN, index-based set ops) remain available.
        // Resolved intents are cached on the QueryPlan so the same object reference
        // is reused across calls — this is critical for CreateReaderFromIntent's
        // CachedReaderInfo cache (keyed by QueryIntent reference equality).
        if (plan.HasCotes)
        {
            // Fast path: reuse previously resolved intents (reader cache will hit)
            if (!plan.IsCompound && plan.ResolvedSimple != null)
                return ExecuteIntent(db, plan.ResolvedSimple, parameters);
            if (plan.IsCompound && plan.ResolvedCompound != null)
                return ExecuteCompoundResolved(db, plan.ResolvedCompound, parameters);

            var coteMap = BuildCoteIntentMap(plan.Cotes!);

            if (!plan.IsCompound)
            {
                // Cote → SELECT WHERE: single cursor with merged filters
                if (CanResolveCoteSimple(plan.Simple!, coteMap))
                {
                    var resolved = ResolveSingleIntent(plan.Simple!, coteMap);
                    plan.ResolvedSimple = resolved; // Cache for subsequent calls
                    return ExecuteIntent(db, resolved, parameters);
                }
            }
            else if (CanResolveCompound(plan.Compound!, coteMap))
            {
                // Cote + compound: resolve references, then use streaming paths
                var resolved = ResolveCompound(plan.Compound!, coteMap);
                plan.ResolvedCompound = resolved; // Cache for subsequent calls
                return ExecuteCompoundResolved(db, resolved, parameters);
            }
        }

        // ─── Fallback: materialize Cotes for complex cases ────────────
        Dictionary<string, MaterializedResultSet>? coteResults = null;
        if (plan.HasCotes)
            coteResults = CoteExecutor.MaterializeCotes(db, plan.Cotes!, parameters);

        if (plan.IsCompound)
        {
            // Streaming UNION ALL (2-way): zero-materialization concatenation
            if (StreamingUnionExecutor.CanStreamUnionAll(plan.Compound!, coteResults))
                return StreamingUnionExecutor.StreamingUnionAll(db, plan.Compound!, parameters);

            // Streaming UNION ALL + ORDER BY + LIMIT: concat reader → TopN heap
            if (StreamingUnionExecutor.CanStreamUnionAllTopN(plan.Compound!, coteResults))
                return StreamingUnionExecutor.StreamingUnionAllTopN(db, plan.Compound!, parameters);

            // Streaming chained UNION ALL (N-way): flatten to streaming concat chain
            if (CanStreamChainedUnionAll(plan.Compound!, coteResults))
                return StreamingChainedUnionAll(db, plan.Compound!, parameters);

            // Index-based streaming: UNION/INTERSECT/EXCEPT without string materialization.
            if (CanStreamSetOp(plan.Compound!, coteResults))
                return ExecuteIndexSetOp(db, plan.Compound!, parameters);

            var (rows, columns) = ExecuteCompoundCore(db, plan.Compound!, parameters, coteResults);
            return new SharcDataReader(rows, columns);
        }

        // Simple query with Cotes
        return CoteExecutor.ExecuteSimpleWithCotes(db, plan.Simple!, parameters, coteResults!);
    }

    // ─── Lazy Cote resolution ─────────────────────────────────────

    /// <summary>
    /// Builds a lookup of Cote name → query intent for lazy resolution.
    /// </summary>
    private static Dictionary<string, QueryIntent> BuildCoteIntentMap(IReadOnlyList<CoteIntent> cotes)
    {
        var map = new Dictionary<string, QueryIntent>(cotes.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var cote in cotes)
            map[cote.Name] = cote.Query;
        return map;
    }

    /// <summary>
    /// Returns true when a Cote query is a simple filtered table scan that can be inlined.
    /// No aggregates, DISTINCT, ORDER BY, LIMIT, or inter-Cote references.
    /// </summary>
    private static bool IsCoteSimpleTableScan(QueryIntent coteQuery, Dictionary<string, QueryIntent> coteMap)
    {
        if (coteMap.ContainsKey(coteQuery.TableName)) return false; // references another Cote
        if (coteQuery.HasAggregates) return false;
        if (coteQuery.IsDistinct) return false;
        if (coteQuery.OrderBy is { Count: > 0 }) return false;
        if (coteQuery.Limit.HasValue || coteQuery.Offset.HasValue) return false;
        if (coteQuery.GroupBy is { Count: > 0 }) return false;
        if (coteQuery.HavingFilter.HasValue) return false;
        return true;
    }

    /// <summary>
    /// Returns true when a simple Cote query can be resolved to a direct cursor read.
    /// </summary>
    private static bool CanResolveCoteSimple(QueryIntent outer, Dictionary<string, QueryIntent> coteMap)
    {
        if (!coteMap.TryGetValue(outer.TableName, out var coteQuery)) return false;
        return IsCoteSimpleTableScan(coteQuery, coteMap);
    }

    /// <summary>
    /// Executes a Cote → SELECT WHERE as a single cursor with merged filters.
    /// Avoids materializing the Cote entirely.
    /// </summary>
    private static SharcDataReader ExecuteResolvedSimple(
        SharcDatabase db, QueryIntent outer, Dictionary<string, QueryIntent> coteMap,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var resolved = ResolveSingleIntent(outer, coteMap);
        return ExecuteIntent(db, resolved, parameters);
    }

    /// <summary>
    /// Returns true when all Cote references in a compound plan can be inlined.
    /// Requires a simple two-way plan with no nested compounds.
    /// </summary>
    private static bool CanResolveCompound(CompoundQueryPlan plan, Dictionary<string, QueryIntent> coteMap)
    {
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        bool leftIsCte = coteMap.ContainsKey(plan.Left.TableName);
        bool rightIsCte = coteMap.ContainsKey(plan.RightSimple.TableName);

        if (!leftIsCte && !rightIsCte) return false;
        if (leftIsCte && !IsCoteSimpleTableScan(coteMap[plan.Left.TableName], coteMap)) return false;
        if (rightIsCte && !IsCoteSimpleTableScan(coteMap[plan.RightSimple.TableName], coteMap)) return false;

        return true;
    }

    /// <summary>
    /// Replaces Cote table references with real table intents, merging filters.
    /// </summary>
    private static CompoundQueryPlan ResolveCompound(
        CompoundQueryPlan plan, Dictionary<string, QueryIntent> coteMap)
    {
        return new CompoundQueryPlan
        {
            Left = ResolveSingleIntent(plan.Left, coteMap),
            Operator = plan.Operator,
            RightSimple = plan.RightSimple != null ? ResolveSingleIntent(plan.RightSimple, coteMap) : null,
            RightCompound = plan.RightCompound,
            FinalOrderBy = plan.FinalOrderBy,
            FinalLimit = plan.FinalLimit,
            FinalOffset = plan.FinalOffset,
        };
    }

    /// <summary>
    /// If the intent references a Cote, returns a new intent targeting the real table
    /// with the Cote filter and outer filter merged via AND.
    /// </summary>
    private static QueryIntent ResolveSingleIntent(QueryIntent intent, Dictionary<string, QueryIntent> coteMap)
    {
        if (!coteMap.TryGetValue(intent.TableName, out var coteQuery))
            return intent;

        return new QueryIntent
        {
            TableName = coteQuery.TableName,
            Columns = intent.Columns ?? coteQuery.Columns,
            Filter = MergeFilters(coteQuery.Filter, intent.Filter),
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
    private static PredicateIntent? MergeFilters(PredicateIntent? coteFilter, PredicateIntent? outerFilter)
    {
        if (!coteFilter.HasValue) return outerFilter;
        if (!outerFilter.HasValue) return coteFilter;

        var coteNodes = coteFilter.Value.Nodes;
        var outerNodes = outerFilter.Value.Nodes;
        int offset = coteNodes.Length;

        // Combine: [cote nodes] [outer nodes (shifted)] [AND root]
        var combined = new IntentPredicateNode[coteNodes.Length + outerNodes.Length + 1];
        Array.Copy(coteNodes, 0, combined, 0, coteNodes.Length);

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
            LeftIndex = coteFilter.Value.RootIndex,
            RightIndex = outerFilter.Value.RootIndex + offset,
        };

        return new PredicateIntent(combined, combined.Length - 1);
    }

    /// <summary>
    /// Executes a resolved compound plan through all streaming paths.
    /// Called after Cote references have been inlined into real table intents.
    /// </summary>
    private static SharcDataReader ExecuteCompoundResolved(
        SharcDatabase db, CompoundQueryPlan resolved,
        IReadOnlyDictionary<string, object>? parameters)
    {
        // All streaming paths are available — no Cote references remain
        if (StreamingUnionExecutor.CanStreamUnionAll(resolved, null))
            return StreamingUnionExecutor.StreamingUnionAll(db, resolved, parameters);

        if (StreamingUnionExecutor.CanStreamUnionAllTopN(resolved, null))
            return StreamingUnionExecutor.StreamingUnionAllTopN(db, resolved, parameters);

        if (CanStreamChainedUnionAll(resolved, null))
            return StreamingChainedUnionAll(db, resolved, parameters);

        if (CanStreamSetOp(resolved, null))
            return ExecuteIndexSetOp(db, resolved, parameters);

        // Fallback: standard compound execution without Cote results
        var (rows, columns) = ExecuteCompoundCore(db, resolved, parameters, null);
        return new SharcDataReader(rows, columns);
    }

    // ─── Compound execution ──────────────────────────────────────

    private static MaterializedResultSet ExecuteCompoundCore(
        SharcDatabase db,
        CompoundQueryPlan plan,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, MaterializedResultSet>? coteResults)
    {
        // Streaming path: UNION/INTERSECT/EXCEPT with two simple sides, no Cote references.
        // Avoids full materialization of both sides — reads directly from B-tree cursors.
        if (CanStreamSetOp(plan, coteResults))
        {
            var (rows, columns) = ExecuteStreamingSetOp(db, plan, parameters);

            if (plan.FinalOrderBy is { Count: > 0 })
                QueryPostProcessor.ApplyOrderBy(rows, plan.FinalOrderBy, columns);

            if (plan.FinalLimit.HasValue || plan.FinalOffset.HasValue)
                rows = QueryPostProcessor.ApplyLimitOffset(rows, plan.FinalLimit, plan.FinalOffset);

            return new MaterializedResultSet(rows, columns);
        }

        // Materialized path: complex compounds, Cote references, or UNION ALL.
        var (leftRows, leftColumns) = ExecuteAndMaterialize(db, plan.Left, parameters, coteResults);

        List<QueryValue[]> rightRows;
        if (plan.RightCompound != null)
        {
            (rightRows, _) = ExecuteCompoundCore(db, plan.RightCompound, parameters, coteResults);
        }
        else
        {
            (rightRows, _) = ExecuteAndMaterialize(db, plan.RightSimple!, parameters, coteResults);
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

        return new MaterializedResultSet(combined, leftColumns);
    }

    /// <summary>
    /// Returns true when a compound plan can use streaming set operations:
    /// UNION/INTERSECT/EXCEPT, simple two-way, no Cote references on either side.
    /// </summary>
    private static bool CanStreamSetOp(
        CompoundQueryPlan plan,
        Dictionary<string, MaterializedResultSet>? coteResults)
    {
        if (plan.Operator == CompoundOperator.UnionAll) return false;
        if (plan.RightCompound != null) return false;
        if (plan.RightSimple == null) return false;

        if (coteResults != null)
        {
            if (coteResults.ContainsKey(plan.Left.TableName)) return false;
            if (coteResults.ContainsKey(plan.RightSimple.TableName)) return false;
        }

        return true;
    }

    /// <summary>
    /// Executes a streaming set operation using <see cref="StreamingSetOpProcessor"/>.
    /// </summary>
    private static MaterializedResultSet ExecuteStreamingSetOp(
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
    /// Returns true when an entire compound chain is UNION ALL with no Cotes,
    /// no final ORDER BY / LIMIT / OFFSET, enabling N-way streaming concat.
    /// </summary>
    private static bool CanStreamChainedUnionAll(
        CompoundQueryPlan plan,
        Dictionary<string, MaterializedResultSet>? coteResults)
    {
        var current = plan;
        while (current != null)
        {
            if (current.Operator != CompoundOperator.UnionAll) return false;
            if (current.FinalOrderBy is { Count: > 0 }) return false;
            if (current.FinalLimit.HasValue || current.FinalOffset.HasValue) return false;

            if (coteResults != null && coteResults.ContainsKey(current.Left.TableName))
                return false;

            if (current.RightSimple != null)
            {
                if (coteResults != null && coteResults.ContainsKey(current.RightSimple.TableName))
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

        return new SharcDataReader(rows, columns);
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

    internal static MaterializedResultSet ExecuteAndMaterialize(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters,
        Dictionary<string, MaterializedResultSet>? coteResults)
    {
        if (coteResults != null && coteResults.TryGetValue(intent.TableName, out var coteData))
        {
            var rows = new List<QueryValue[]>(coteData.Rows);
            var columnNames = coteData.Columns;

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

            return new MaterializedResultSet(rows, columnNames);
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
    internal static List<TableReference> CollectTableReferences(QueryPlan plan)
        => TableReferenceCollector.Collect(plan);
}
