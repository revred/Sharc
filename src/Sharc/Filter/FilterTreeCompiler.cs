// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Compiles an IFilterStar expression tree into an IFilterNode evaluation tree.
/// Resolves column names to ordinals and validates the expression.
/// </summary>
internal static class FilterTreeCompiler
{
    private const int MaxDepth = 32;

    /// <summary>
    /// Compiles a filter expression into an evaluation node tree.
    /// </summary>
    /// <param name="expression">The uncompiled filter expression.</param>
    /// <param name="columns">Table column definitions for name -> ordinal resolution.</param>
    /// <param name="rowidAliasOrdinal">Ordinal of the INTEGER PRIMARY KEY alias column, or -1.</param>
    /// <returns>A compiled filter node tree ready for per-row evaluation.</returns>
    internal static IFilterNode Compile(
        IFilterStar expression,
        IReadOnlyList<ColumnInfo> columns,
        int rowidAliasOrdinal = -1)
    {
        var columnMap = BuildColumnMap(columns);
        return CompileNode(expression, columns, columnMap, rowidAliasOrdinal, 0);
    }

    /// <summary>
    /// Compiles a filter expression into a closure-composed "Baked" evaluation node (Tier 2).
    /// Uses direct delegate composition — AOT-safe, no expression trees or dynamic code generation.
    /// </summary>
    internal static IFilterNode CompileBaked(
        IFilterStar expression,
        IReadOnlyList<ColumnInfo> columns,
        int rowidAliasOrdinal = -1)
    {
        var columnMap = BuildColumnMap(columns);
        var ordinals = GetReferencedColumns(expression, columns, columnMap);
        var compiled = FilterStarCompiler.Compile(expression, columns, columnMap, rowidAliasOrdinal);
        return new FilterNode(compiled, ordinals);
    }

    private static IFilterNode CompileNode(
        IFilterStar expr,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal,
        int depth)
    {
        if (depth > MaxDepth)
            throw new ArgumentException($"Filter expression tree exceeds maximum depth of {MaxDepth}.");

        return expr switch
        {
            AndExpression and => new AndNode(CompileChildren(and.Children, columns, columnMap, rowidAliasOrdinal, depth)),
            OrExpression or => new OrNode(CompileChildren(or.Children, columns, columnMap, rowidAliasOrdinal, depth)),
            NotExpression not => new NotNode(CompileNode(not.Inner, columns, columnMap, rowidAliasOrdinal, depth + 1)),
            PredicateExpression pred => CompilePredicate(pred, columns, columnMap, rowidAliasOrdinal),
            _ => throw new ArgumentException($"Unknown filter expression type: {expr.GetType().Name}")
        };
    }

    private static IFilterNode[] CompileChildren(
        IFilterStar[] children,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal,
        int depth)
    {
        var nodes = new IFilterNode[children.Length];
        for (int i = 0; i < children.Length; i++)
            nodes[i] = CompileNode(children[i], columns, columnMap, rowidAliasOrdinal, depth + 1);
        return nodes;
    }

    private static IFilterNode CompilePredicate(
        PredicateExpression pred,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        ColumnInfo col = ResolveColumn(pred, columns, columnMap);

        // GUID expansion for merged columns
        if (pred.Value.ValueTag == TypedFilterValue.Tag.Guid &&
            col.IsMergedGuidColumn &&
            col.MergedPhysicalOrdinals is { Length: 2 } guidOrdinals)
        {
            var hiOrdinal = guidOrdinals[0];
            var loOrdinal = guidOrdinals[1];
            return new GuidPredicateNode(hiOrdinal, loOrdinal, pred.Operator, pred.Value);
        }

        if (pred.Value.ValueTag == TypedFilterValue.Tag.Decimal &&
            col.IsMergedDecimalColumn &&
            col.MergedPhysicalOrdinals is { Length: 2 } decimalOrdinals)
        {
            var hiOrdinal = decimalOrdinals[0];
            var loOrdinal = decimalOrdinals[1];
            return new MergedDecimalPredicateNode(hiOrdinal, loOrdinal, pred.Operator, pred.Value);
        }

        int ordinal = col.MergedPhysicalOrdinals?[0] ?? col.Ordinal;
        return new PredicateNode(ordinal, pred.Operator, pred.Value, rowidAliasOrdinal);
    }

    internal static HashSet<int> GetReferencedColumns(IFilterStar expression, IReadOnlyList<ColumnInfo> columns)
    {
        var columnMap = BuildColumnMap(columns);
        return GetReferencedColumns(expression, columns, columnMap);
    }

    private static HashSet<int> GetReferencedColumns(
        IFilterStar expression,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap)
    {
        var ordinals = new HashSet<int>();
        CollectOrdinals(expression, columns, columnMap, ordinals);
        return ordinals;
    }

    private static void CollectOrdinals(
        IFilterStar expr,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        HashSet<int> ordinals)
    {
        switch (expr)
        {
            case AndExpression and:
                foreach (var child in and.Children) CollectOrdinals(child, columns, columnMap, ordinals);
                break;
            case OrExpression or:
                foreach (var child in or.Children) CollectOrdinals(child, columns, columnMap, ordinals);
                break;
            case NotExpression not:
                CollectOrdinals(not.Inner, columns, columnMap, ordinals);
                break;
            case PredicateExpression pred:
                if (!TryResolveColumn(pred, columns, columnMap, out var col))
                    return;

                if (col.MergedPhysicalOrdinals != null)
                {
                    foreach (var o in col.MergedPhysicalOrdinals)
                        ordinals.Add(o);
                }
                else
                {
                    ordinals.Add(col.Ordinal);
                }
                break;
        }
    }

    private static ColumnInfo ResolveColumn(
        PredicateExpression pred,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap)
    {
        if (TryResolveColumn(pred, columns, columnMap, out var col))
            return col;

        if (pred.ColumnOrdinal is int ordinal)
            throw new ArgumentException($"Filter column ordinal {ordinal} is out of range.");

        throw new ArgumentException($"Filter column '{pred.ColumnName}' not found.");
    }

    private static bool TryResolveColumn(
        PredicateExpression pred,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        out ColumnInfo col)
    {
        if (pred.ColumnOrdinal is int ordinal)
        {
            if ((uint)ordinal < (uint)columns.Count)
            {
                col = columns[ordinal];
                return true;
            }

            col = default!;
            return false;
        }

        if (pred.ColumnName != null
            && columnMap.TryGetValue(pred.ColumnName, out var resolved)
            && resolved != null)
        {
            col = resolved;
            return true;
        }

        col = default!;
        return false;
    }

    private static Dictionary<string, ColumnInfo> BuildColumnMap(IReadOnlyList<ColumnInfo> columns)
    {
        var map = new Dictionary<string, ColumnInfo>(columns.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
            map[columns[i].Name] = columns[i];
        return map;
    }
}

