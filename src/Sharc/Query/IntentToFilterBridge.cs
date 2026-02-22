// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;
using IntentPredicateNode = Sharc.Query.Intent.PredicateNode;

namespace Sharc.Query;

/// <summary>
/// Converts a <see cref="PredicateIntent"/> into an <see cref="IFilterStar"/> filter tree.
/// </summary>
internal static class IntentToFilterBridge
{
    /// <summary>
    /// Builds an <see cref="IFilterStar"/> from the flat predicate intent.
    /// </summary>
    internal static IFilterStar Build(PredicateIntent intent,
        IReadOnlyDictionary<string, object>? parameters = null,
        string? tableAlias = null)
    {
        var cache = new IFilterStar[intent.Nodes.Length];
        return BuildNode(intent.Nodes, intent.RootIndex, cache, parameters, tableAlias);
    }

    private static IFilterStar BuildNode(IntentPredicateNode[] nodes, int index,
        IFilterStar[] cache, IReadOnlyDictionary<string, object>? parameters, string? tableAlias)
    {
        if (cache[index] is not null)
            return cache[index];

        ref readonly var node = ref nodes[index];
        IFilterStar result = node.Op switch
        {
            // Logical
            IntentOp.And => FilterStar.And(
                BuildNode(nodes, node.LeftIndex, cache, parameters, tableAlias),
                BuildNode(nodes, node.RightIndex, cache, parameters, tableAlias)),
            IntentOp.Or => FilterStar.Or(
                BuildNode(nodes, node.LeftIndex, cache, parameters, tableAlias),
                BuildNode(nodes, node.RightIndex, cache, parameters, tableAlias)),
            IntentOp.Not => FilterStar.Not(
                BuildNode(nodes, node.LeftIndex, cache, parameters, tableAlias)),

            // Leaf operations
            _ => BuildLeaf(node, parameters, tableAlias),
        };

        cache[index] = result;
        return result;
    }

    private static IFilterStar BuildLeaf(in IntentPredicateNode node,
        IReadOnlyDictionary<string, object>? parameters, string? tableAlias)
    {
        var colName = node.ColumnName!;
        if (!string.IsNullOrEmpty(tableAlias) &&
            colName.Length > tableAlias.Length + 1 &&
            colName[tableAlias.Length] == '.' &&
            colName.AsSpan(0, tableAlias.Length).Equals(tableAlias.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            colName = colName[(tableAlias.Length + 1)..];
        }

        var col = FilterStar.Column(colName);

        // Resolve parameter to concrete value if needed
        if (node.Value.Kind == IntentValueKind.Parameter)
            return BuildParameterLeaf(col, node, parameters);

        return node.Op switch
        {
            // Comparison - Long
            IntentOp.Eq when node.Value.Kind == IntentValueKind.Signed64
                => col.Eq(node.Value.AsInt64),
            IntentOp.Neq when node.Value.Kind == IntentValueKind.Signed64
                => col.Neq(node.Value.AsInt64),
            IntentOp.Lt when node.Value.Kind == IntentValueKind.Signed64
                => col.Lt(node.Value.AsInt64),
            IntentOp.Lte when node.Value.Kind == IntentValueKind.Signed64
                => col.Lte(node.Value.AsInt64),
            IntentOp.Gt when node.Value.Kind == IntentValueKind.Signed64
                => col.Gt(node.Value.AsInt64),
            IntentOp.Gte when node.Value.Kind == IntentValueKind.Signed64
                => col.Gte(node.Value.AsInt64),

            // Comparison - Double
            IntentOp.Eq when node.Value.Kind == IntentValueKind.Real
                => col.Eq(node.Value.AsFloat64),
            IntentOp.Neq when node.Value.Kind == IntentValueKind.Real
                => col.Neq(node.Value.AsFloat64),
            IntentOp.Lt when node.Value.Kind == IntentValueKind.Real
                => col.Lt(node.Value.AsFloat64),
            IntentOp.Lte when node.Value.Kind == IntentValueKind.Real
                => col.Lte(node.Value.AsFloat64),
            IntentOp.Gt when node.Value.Kind == IntentValueKind.Real
                => col.Gt(node.Value.AsFloat64),
            IntentOp.Gte when node.Value.Kind == IntentValueKind.Real
                => col.Gte(node.Value.AsFloat64),

            // Comparison - String
            IntentOp.Eq when node.Value.Kind == IntentValueKind.Text
                => col.Eq(node.Value.AsText!),
            IntentOp.Neq when node.Value.Kind == IntentValueKind.Text
                => col.Neq(node.Value.AsText!),

            // Between
            IntentOp.Between when node.Value.Kind == IntentValueKind.Signed64
                => col.Between(node.Value.AsInt64, node.HighValue.AsInt64),
            IntentOp.Between when node.Value.Kind == IntentValueKind.Real
                => col.Between(node.Value.AsFloat64, node.HighValue.AsFloat64),

            // NULL
            IntentOp.IsNull => col.IsNull(),
            IntentOp.IsNotNull => col.IsNotNull(),

            // String operations
            IntentOp.StartsWith => col.StartsWith(node.Value.AsText!),
            IntentOp.EndsWith => col.EndsWith(node.Value.AsText!),
            IntentOp.Contains => col.Contains(node.Value.AsText!),

            // IN
            IntentOp.In when node.Value.Kind == IntentValueKind.Signed64Set
                => col.In(node.Value.AsInt64Set!),
            IntentOp.In when node.Value.Kind == IntentValueKind.TextSet
                => col.In(node.Value.AsTextSet!),
            IntentOp.NotIn when node.Value.Kind == IntentValueKind.Signed64Set
                => col.NotIn(node.Value.AsInt64Set!),
            IntentOp.NotIn when node.Value.Kind == IntentValueKind.TextSet
                => col.NotIn(node.Value.AsTextSet!),

            _ => throw new NotSupportedException(
                $"Unsupported intent operation: {node.Op} with value kind {node.Value.Kind}"),
        };
    }

    private static IFilterStar BuildParameterLeaf(
        ColumnRef col,
        in IntentPredicateNode node,
        IReadOnlyDictionary<string, object>? parameters)
    {
        string paramName = node.Value.AsText!;

        if (parameters == null || !parameters.TryGetValue(paramName, out var value))
            throw new ArgumentException($"Parameter '${paramName}' was not provided.");

        return (node.Op, value) switch
        {
            (IntentOp.Eq, long l) => col.Eq(l),
            (IntentOp.Eq, int i) => col.Eq((long)i),
            (IntentOp.Eq, double d) => col.Eq(d),
            (IntentOp.Eq, string s) => col.Eq(s),
            (IntentOp.Neq, long l) => col.Neq(l),
            (IntentOp.Neq, string s) => col.Neq(s),
            (IntentOp.Lt, long l) => col.Lt(l),
            (IntentOp.Lt, double d) => col.Lt(d),
            (IntentOp.Lte, long l) => col.Lte(l),
            (IntentOp.Lte, double d) => col.Lte(d),
            (IntentOp.Gt, long l) => col.Gt(l),
            (IntentOp.Gt, int i) => col.Gt((long)i),
            (IntentOp.Gt, double d) => col.Gt(d),
            (IntentOp.Gte, long l) => col.Gte(l),
            (IntentOp.Gte, double d) => col.Gte(d),
            _ => throw new NotSupportedException(
                $"Unsupported parameter type {value?.GetType().Name} for operation {node.Op}"),
        };
    }
}
