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
    /// <param name="columns">Table column definitions for name â†’ ordinal resolution.</param>
    /// <param name="rowidAliasOrdinal">Ordinal of the INTEGER PRIMARY KEY alias column, or -1.</param>
    /// <returns>A compiled filter node tree ready for per-row evaluation.</returns>
    internal static IFilterNode Compile(IFilterStar expression, IReadOnlyList<ColumnInfo> columns,
                                        int rowidAliasOrdinal = -1)
    {
        return CompileNode(expression, columns, rowidAliasOrdinal, 0);
    }

    /// <summary>
    /// Compiles a filter expression into a closure-composed "Baked" evaluation node (Tier 2).
    /// Uses direct delegate composition — AOT-safe, no expression trees or dynamic code generation.
    /// </summary>
    internal static IFilterNode CompileBaked(IFilterStar expression, IReadOnlyList<ColumnInfo> columns,
                                             int rowidAliasOrdinal = -1)
    {
        var ordinals = GetReferencedColumns(expression, columns);
        var compiled = FilterStarCompiler.Compile(expression, columns, rowidAliasOrdinal);
        return new FilterNode(compiled, ordinals);
    }

    private static IFilterNode CompileNode(IFilterStar expr, IReadOnlyList<ColumnInfo> columns,
                                            int rowidAliasOrdinal, int depth)
    {
        if (depth > MaxDepth)
            throw new ArgumentException($"Filter expression tree exceeds maximum depth of {MaxDepth}.");

        return expr switch
        {
            AndExpression and => new AndNode(CompileChildren(and.Children, columns, rowidAliasOrdinal, depth)),
            OrExpression or => new OrNode(CompileChildren(or.Children, columns, rowidAliasOrdinal, depth)),
            NotExpression not => new NotNode(CompileNode(not.Inner, columns, rowidAliasOrdinal, depth + 1)),
            PredicateExpression pred => CompilePredicate(pred, columns, rowidAliasOrdinal),
            _ => throw new ArgumentException($"Unknown filter expression type: {expr.GetType().Name}")
        };
    }

    private static IFilterNode[] CompileChildren(IFilterStar[] children, IReadOnlyList<ColumnInfo> columns,
                                                  int rowidAliasOrdinal, int depth)
    {
        var nodes = new IFilterNode[children.Length];
        for (int i = 0; i < children.Length; i++)
            nodes[i] = CompileNode(children[i], columns, rowidAliasOrdinal, depth + 1);
        return nodes;
    }

    private static int ResolveOrdinal(PredicateExpression pred, IReadOnlyList<ColumnInfo> columns)
    {
        if (pred.ColumnOrdinal.HasValue)
        {
            int ordinal = pred.ColumnOrdinal.Value;
            if (ordinal < 0 || ordinal >= columns.Count)
                throw new ArgumentOutOfRangeException(nameof(pred), $"Column ordinal {ordinal} is out of range (0..{columns.Count - 1}).");
            
            return columns[ordinal].MergedPhysicalOrdinals?[0] ?? columns[ordinal].Ordinal;
        }

        if (pred.ColumnName != null)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return columns[i].MergedPhysicalOrdinals?[0] ?? columns[i].Ordinal;
                }
            }
            throw new ArgumentException($"Filter column '{pred.ColumnName}' not found in table.");
        }

        throw new ArgumentException("Filter predicate must specify either column name or ordinal.");
    }

    private static IFilterNode CompilePredicate(PredicateExpression pred, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        ColumnInfo? col = pred.ColumnOrdinal.HasValue
            ? (pred.ColumnOrdinal < columns.Count ? columns[pred.ColumnOrdinal.Value] : null)
            : columns.FirstOrDefault(c => c.Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase));

        if (col == null)
            throw new ArgumentException($"Filter column '{pred.ColumnName}' not found.");

        // GUID expansion for merged columns
        if (pred.Value.ValueTag == TypedFilterValue.Tag.Guid && col.MergedPhysicalOrdinals?.Length == 2)
        {
            var hiOrdinal = col.MergedPhysicalOrdinals[0];
            var loOrdinal = col.MergedPhysicalOrdinals[1];
            var hiValue = TypedFilterValue.FromInt64(pred.Value.AsInt64());
            var loValue = TypedFilterValue.FromInt64(pred.Value.AsInt64High());

            if (pred.Operator == FilterOp.Eq)
            {
                return new AndNode([
                    new PredicateNode(hiOrdinal, FilterOp.Eq, hiValue, rowidAliasOrdinal),
                    new PredicateNode(loOrdinal, FilterOp.Eq, loValue, rowidAliasOrdinal)
                ]);
            }
            if (pred.Operator == FilterOp.Neq)
            {
                return new OrNode([
                    new PredicateNode(hiOrdinal, FilterOp.Neq, hiValue, rowidAliasOrdinal),
                    new PredicateNode(loOrdinal, FilterOp.Neq, loValue, rowidAliasOrdinal)
                ]);
            }
        }

        int ordinal = col.MergedPhysicalOrdinals?[0] ?? col.Ordinal;
        return new PredicateNode(ordinal, pred.Operator, pred.Value, rowidAliasOrdinal);
    }

    internal static HashSet<int> GetReferencedColumns(IFilterStar expression, IReadOnlyList<ColumnInfo> columns)
    {
        var ordinals = new HashSet<int>();
        CollectOrdinals(expression, columns, ordinals);
        return ordinals;
    }

    private static void CollectOrdinals(IFilterStar expr, IReadOnlyList<ColumnInfo> columns, HashSet<int> ordinals)
    {
        switch (expr)
        {
            case AndExpression and:
                foreach (var child in and.Children) CollectOrdinals(child, columns, ordinals);
                break;
            case OrExpression or:
                foreach (var child in or.Children) CollectOrdinals(child, columns, ordinals);
                break;
            case NotExpression not:
                CollectOrdinals(not.Inner, columns, ordinals);
                break;
            case PredicateExpression pred:
                var col = pred.ColumnOrdinal.HasValue
                    ? (pred.ColumnOrdinal < columns.Count ? columns[pred.ColumnOrdinal.Value] : null)
                    : columns.FirstOrDefault(c => c.Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (col != null)
                {
                    if (col.MergedPhysicalOrdinals != null)
                        foreach (var o in col.MergedPhysicalOrdinals) ordinals.Add(o);
                    else
                        ordinals.Add(col.Ordinal);
                }
                break;
        }
    }
}