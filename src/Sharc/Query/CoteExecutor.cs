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
    /// Materializes all Cotes in order, returning a lookup of Cote name → (rows, columns).
    /// </summary>
    internal static Dictionary<string, MaterializedResultSet> MaterializeCotes(
        SharcDatabase db,
        IReadOnlyList<CoteIntent> cotes,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var results = new Dictionary<string, MaterializedResultSet>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var cote in cotes)
        {
            results[cote.Name] = CompoundQueryExecutor.ExecuteAndMaterialize(
                db, cote.Query, parameters, results);
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
        Dictionary<string, MaterializedResultSet> coteResults)
    {
        if (coteResults.TryGetValue(intent.TableName, out var coteData))
        {
            bool needsFilter = intent.Filter.HasValue;
            bool needsSort = intent.OrderBy is { Count: > 0 };
            bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;
            bool needsAggregate = intent.HasAggregates;
            bool needsDistinct = intent.IsDistinct;

            if (!needsFilter && !needsAggregate && !needsDistinct && !needsSort && !needsLimit)
                return new SharcDataReader(coteData.Rows, coteData.Columns);

            var rows = new List<QueryValue[]>(coteData.Rows);
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

        return CompoundQueryExecutor.ExecuteIntent(db, intent, parameters);
    }

    // ─── Predicate evaluation on materialized rows ────────────────

    private static List<QueryValue[]> ApplyPredicateFilter(
        List<QueryValue[]> rows, string[] columnNames,
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

        var result = new List<QueryValue[]>(rows.Count);
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
            (IntentOp.Gt, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() > node.Value.AsFloat64,
            (IntentOp.Gte, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsFloat64,
            (IntentOp.Lt, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() < node.Value.AsFloat64,
            (IntentOp.Lte, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() <= node.Value.AsFloat64,

            // Cross-type: int column vs double filter
            (IntentOp.Gt, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() > node.Value.AsFloat64,
            (IntentOp.Lt, IntentValueKind.Real, QueryValueType.Int64)
                => val.AsInt64() < node.Value.AsFloat64,

            // Text comparisons
            (IntentOp.Eq, IntentValueKind.Text, QueryValueType.Text)
                => string.Equals(val.AsString(), node.Value.AsText, StringComparison.Ordinal),
            (IntentOp.Neq, IntentValueKind.Text, QueryValueType.Text)
                => !string.Equals(val.AsString(), node.Value.AsText, StringComparison.Ordinal),

            // Between
            (IntentOp.Between, IntentValueKind.Signed64, QueryValueType.Int64)
                => val.AsInt64() >= node.Value.AsInt64 && val.AsInt64() <= node.HighValue.AsInt64,
            (IntentOp.Between, IntentValueKind.Real, QueryValueType.Double)
                => val.AsDouble() >= node.Value.AsFloat64 && val.AsDouble() <= node.HighValue.AsFloat64,

            _ => true, // Unknown combination — pass through
        };
    }

    private static bool EvalWithParam(IntentOp op, QueryValue val, object paramVal)
    {
        if (op == IntentOp.IsNull) return val.IsNull;
        if (op == IntentOp.IsNotNull) return !val.IsNull;
        if (val.IsNull) return false;

        return (op, paramVal) switch
        {
            (IntentOp.Eq, long l) => val.Type == QueryValueType.Int64 && val.AsInt64() == l,
            (IntentOp.Gt, long l) => val.Type == QueryValueType.Int64 && val.AsInt64() > l,
            (IntentOp.Gte, long l) => val.Type == QueryValueType.Int64 && val.AsInt64() >= l,
            (IntentOp.Lt, long l) => val.Type == QueryValueType.Int64 && val.AsInt64() < l,
            (IntentOp.Lte, long l) => val.Type == QueryValueType.Int64 && val.AsInt64() <= l,
            (IntentOp.Eq, double d) => val.Type == QueryValueType.Double && val.AsDouble() == d,
            (IntentOp.Gt, double d) => val.Type == QueryValueType.Double && val.AsDouble() > d,
            (IntentOp.Lt, double d) => val.Type == QueryValueType.Double && val.AsDouble() < d,
            (IntentOp.Eq, string s) => val.Type == QueryValueType.Text
                && string.Equals(val.AsString(), s, StringComparison.Ordinal),
            _ => true,
        };
    }
}
