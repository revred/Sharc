// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using IntentPredicateNode = Sharc.Query.Intent.PredicateNode;

namespace Sharc.Query;

/// <summary>
/// Executes queries that reference Cotes (Common Table Expressions).
/// Materializes Cote results first, then executes the main query against them.
/// </summary>
internal static class CoteExecutor
{
    /// <summary>
    /// Materializes all Cotes in order, returning a lookup of Cote name to materialized result set.
    /// Cotes whose names appear in <paramref name="skip"/> are not re-materialized.
    /// </summary>
    internal static CoteMap MaterializeCotes(
        SharcDatabase db,
        IReadOnlyList<CoteIntent> cotes,
        IReadOnlyDictionary<string, object>? parameters,
        CoteMap? skip = null)
    {
        var results = new CoteMap(
            StringComparer.OrdinalIgnoreCase);

        foreach (var cote in cotes)
        {
            // Skip Cotes that are already pre-materialized (e.g., filtered registered views).
            // This avoids a redundant full-table scan for the fallback Cote SQL.
            if (skip != null && skip.ContainsKey(cote.Name))
                continue;

            // DECISION: Cotes are materialized eagerly in dependency order. Each Cote's
            // query is executed with all previously materialized Cotes available, enabling
            // forward references (Cote B can reference Cote A if A appears first).
            using var reader = CompoundQueryExecutor.ExecuteWithCotes(
                db, cote.Query, parameters, results);
            results[cote.Name] = QueryPostProcessor.Materialize(reader);
        }

        return results;
    }
    /// <summary>
    /// Executes a simple query that may reference Cote results.
    /// If the query's table name matches a Cote, uses the pre-materialized Cote data.
    /// </summary>
    internal static SharcDataReader ExecuteSimpleWithCotes(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters,
        CoteMap coteResults)
    {
        if (coteResults.TryGetValue(intent.TableName, out var coteData))
        {
            // JOINs cannot be resolved against materialized Cote data inline —
            // delegate to JoinExecutor which can read pre-materialized tables.
            if (intent.HasJoins)
                return Execution.JoinExecutor.Execute(db, intent, parameters, coteResults);

            bool needsFilter = intent.Filter.HasValue;
            bool needsSort = intent.OrderBy is { Count: > 0 };
            bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;
            bool needsAggregate = intent.HasAggregates;
            bool needsDistinct = intent.IsDistinct;

            if (!needsFilter && !needsAggregate && !needsDistinct && !needsSort && !needsLimit)
                return new SharcDataReader(coteData.Rows, coteData.Columns);

            var rows = new RowSet(coteData.Rows);
            var columnNames = coteData.Columns;

            // Apply outer WHERE filter to Cote data
            if (needsFilter)
                rows = ApplyPredicateFilter(rows, columnNames, intent.Filter!.Value, parameters);

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

            return new SharcDataReader(rows, columnNames);
        }

        if (intent.Joins != null && intent.Joins.Count > 0)
        {
            return Execution.JoinExecutor.Execute(db, intent, parameters);
        }

        return CompoundQueryExecutor.ExecuteIntent(db, intent, parameters);
    }

    // ─── Predicate evaluation on materialized rows ────────────────

    private static RowSet ApplyPredicateFilter(
        RowSet rows, string[] columnNames,
        PredicateIntent predicate, IReadOnlyDictionary<string, object>? parameters)
    {
        // Pre-resolve column ordinals for all leaf nodes
        var ordinals = new int[predicate.Nodes.Length];
        for (int i = 0; i < predicate.Nodes.Length; i++)
        {
            ref readonly var node = ref predicate.Nodes[i];
            ordinals[i] = -1;
            if (node.ColumnName != null)
            {
                for (int c = 0; c < columnNames.Length; c++)
                {
                    if (columnNames[c].Equals(node.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        ordinals[i] = c;
                        break;
                    }
                }
            }
        }

        var result = new RowSet(rows.Count);
        foreach (var row in rows)
        {
            if (EvalNode(predicate.Nodes, predicate.RootIndex, row, ordinals, parameters))
                result.Add(row);
        }
        return result;
    }

    private static bool EvalNode(
        IntentPredicateNode[] nodes, int index, QueryValue[] row,
        int[] ordinals, IReadOnlyDictionary<string, object>? parameters)
    {
        ref readonly var node = ref nodes[index];
        return node.Op switch
        {
            IntentOp.And => EvalNode(nodes, node.LeftIndex, row, ordinals, parameters)
                         && EvalNode(nodes, node.RightIndex, row, ordinals, parameters),
            IntentOp.Or => EvalNode(nodes, node.LeftIndex, row, ordinals, parameters)
                        || EvalNode(nodes, node.RightIndex, row, ordinals, parameters),
            IntentOp.Not => !EvalNode(nodes, node.LeftIndex, row, ordinals, parameters),
            _ => EvalLeaf(node, row, ordinals[index], parameters),
        };
    }

    private static bool EvalLeaf(
        in IntentPredicateNode node, QueryValue[] row, int ordinal,
        IReadOnlyDictionary<string, object>? parameters)
    {
        if (ordinal < 0 || ordinal >= row.Length) return true;
        var val = row[ordinal];

        // Resolve parameter value
        if (node.Value.Kind == IntentValueKind.Parameter && parameters != null
            && parameters.TryGetValue(node.Value.AsText!, out var paramVal))
        {
            return EvalWithParam(node.Op, val, paramVal);
        }

        if (node.Op == IntentOp.IsNull) return val.IsNull;
        if (node.Op == IntentOp.IsNotNull) return !val.IsNull;
        if (val.IsNull) return false;

        return (node.Op, node.Value.Kind, val.Type) switch
        {
            // Int64 comparisons
            (IntentOp.Eq, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() == node.Value.AsInt64,
            (IntentOp.Neq, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() != node.Value.AsInt64,
            (IntentOp.Gt, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() > node.Value.AsInt64,
            (IntentOp.Gte, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() >= node.Value.AsInt64,
            (IntentOp.Lt, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() < node.Value.AsInt64,
            (IntentOp.Lte, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() <= node.Value.AsInt64,

            // Double comparisons
            (IntentOp.Eq, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() == node.Value.AsFloat64,
            (IntentOp.Neq, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() != node.Value.AsFloat64,
            (IntentOp.Gt, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() > node.Value.AsFloat64,
            (IntentOp.Gte, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsFloat64,
            (IntentOp.Lt, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() < node.Value.AsFloat64,
            (IntentOp.Lte, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() <= node.Value.AsFloat64,

            // Cross-type: int column vs double filter
            (IntentOp.Eq, IntentValueKind.Real, QueryValueType.Int64)
                => (double)val.AsInt64() == node.Value.AsFloat64,
            (IntentOp.Neq, IntentValueKind.Real, QueryValueType.Int64)
                => (double)val.AsInt64() != node.Value.AsFloat64,
            (IntentOp.Gt, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() > node.Value.AsFloat64,
            (IntentOp.Gte, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() >= node.Value.AsFloat64,
            (IntentOp.Lt, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() < node.Value.AsFloat64,
            (IntentOp.Lte, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() <= node.Value.AsFloat64,

            // Cross-type: double column vs int filter
            (IntentOp.Eq, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() == node.Value.AsInt64,
            (IntentOp.Neq, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() != node.Value.AsInt64,
            (IntentOp.Gt, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() > node.Value.AsInt64,
            (IntentOp.Gte, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsInt64,
            (IntentOp.Lt, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() < node.Value.AsInt64,
            (IntentOp.Lte, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() <= node.Value.AsInt64,

            // Text comparisons
            (IntentOp.Eq, IntentValueKind.Text, QueryValueType.Text)
                => string.Equals(val.AsString(), node.Value.AsText, StringComparison.Ordinal),
            (IntentOp.Neq, IntentValueKind.Text, QueryValueType.Text)
                => !string.Equals(val.AsString(), node.Value.AsText, StringComparison.Ordinal),

            // Between — same type
            (IntentOp.Between, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() >= node.Value.AsInt64 && val.AsInt64() <= node.HighValue.AsInt64,
            (IntentOp.Between, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsFloat64 && val.AsDouble() <= node.HighValue.AsFloat64,

            // Between — cross-type
            (IntentOp.Between, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() >= node.Value.AsFloat64 && val.AsInt64() <= node.HighValue.AsFloat64,
            (IntentOp.Between, IntentValueKind.Signed64, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsInt64 && val.AsDouble() <= node.HighValue.AsInt64,

            // DECISION: unknown (op, valueKind, columnType) triples defensively reject.
            // This ensures new types or operators fail closed rather than silently matching.
            _ => false,
        };
    }

    /// <summary>
    /// Evaluates a predicate leaf against a parameterized value.
    /// Converts the boxed <c>object</c> to a typed <see cref="QueryValue"/> immediately,
    /// then uses the same typed comparison infrastructure as <see cref="EvalLeaf"/>.
    /// </summary>
    private static bool EvalWithParam(IntentOp op, QueryValue val, object paramVal)
    {
        if (op == IntentOp.IsNull) return val.IsNull;
        if (op == IntentOp.IsNotNull) return !val.IsNull;
        if (val.IsNull) return false;

        // Convert boxed object → typed QueryValue at the boundary
        var param = QueryValue.FromObject(paramVal);
        if (param.IsNull) return false;

        return (op, param.Type, val.Type) switch
        {
            // Same type: Int64
            (IntentOp.Eq, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() == param.AsInt64(),
            (IntentOp.Neq, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() != param.AsInt64(),
            (IntentOp.Gt, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() > param.AsInt64(),
            (IntentOp.Gte, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() >= param.AsInt64(),
            (IntentOp.Lt, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() < param.AsInt64(),
            (IntentOp.Lte, QueryValueType.Int64, QueryValueType.Int64) => val.AsInt64() <= param.AsInt64(),

            // Same type: Double
            (IntentOp.Eq, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() == param.AsDouble(),
            (IntentOp.Neq, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() != param.AsDouble(),
            (IntentOp.Gt, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() > param.AsDouble(),
            (IntentOp.Gte, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() >= param.AsDouble(),
            (IntentOp.Lt, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() < param.AsDouble(),
            (IntentOp.Lte, QueryValueType.Double, QueryValueType.Double) => val.AsDouble() <= param.AsDouble(),

            // Cross-type: Int64 param vs Double column
            (IntentOp.Eq, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() == param.AsInt64(),
            (IntentOp.Neq, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() != param.AsInt64(),
            (IntentOp.Gt, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() > param.AsInt64(),
            (IntentOp.Gte, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() >= param.AsInt64(),
            (IntentOp.Lt, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() < param.AsInt64(),
            (IntentOp.Lte, QueryValueType.Int64, QueryValueType.Double) => val.AsDouble() <= param.AsInt64(),

            // Cross-type: Double param vs Int64 column
            (IntentOp.Eq, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() == param.AsDouble(),
            (IntentOp.Neq, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() != param.AsDouble(),
            (IntentOp.Gt, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() > param.AsDouble(),
            (IntentOp.Gte, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() >= param.AsDouble(),
            (IntentOp.Lt, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() < param.AsDouble(),
            (IntentOp.Lte, QueryValueType.Double, QueryValueType.Int64) => val.AsInt64() <= param.AsDouble(),

            // Same type: Text
            (IntentOp.Eq, QueryValueType.Text, QueryValueType.Text)
                => string.Equals(val.AsString(), param.AsString(), StringComparison.Ordinal),
            (IntentOp.Neq, QueryValueType.Text, QueryValueType.Text)
                => !string.Equals(val.AsString(), param.AsString(), StringComparison.Ordinal),

            _ => false, // Unknown combination — defensively reject
        };
    }
}
