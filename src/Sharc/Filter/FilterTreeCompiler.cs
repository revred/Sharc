// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Runtime.CompilerServices;
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
    /// Compiles a filter expression into a JIT-optimized "Baked" evaluation node (Tier 2).
    /// Falls back to Tier 1 interpreted evaluation when dynamic code generation is unavailable (AOT/WASM).
    /// </summary>
    internal static IFilterNode CompileBaked(IFilterStar expression, IReadOnlyList<ColumnInfo> columns,
                                             int rowidAliasOrdinal = -1)
    {
        // AOT/WASM: dynamic code generation unavailable — fall back to Tier 1 interpreter
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return CompileNode(expression, columns, rowidAliasOrdinal, 0);

        var ordinals = GetReferencedColumns(expression, columns);

        try
        {
            var compiled = FilterStarCompiler.Compile(expression, columns, rowidAliasOrdinal);
            return new FilterNode(compiled, ordinals);
        }
        catch (PlatformNotSupportedException)
        {
            // Safety net: fall back to Tier 1 if JIT fails at runtime
            return CompileNode(expression, columns, rowidAliasOrdinal, 0);
        }
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
            return ordinal;
        }

        if (pred.ColumnName != null)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))
                    return columns[i].Ordinal;
            }
            throw new ArgumentException($"Filter column '{pred.ColumnName}' not found in table.");
        }

        throw new ArgumentException("Filter predicate must specify either column name or ordinal.");
    }

    private static PredicateNode CompilePredicate(PredicateExpression pred, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        int ordinal = ResolveOrdinal(pred, columns);
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
                ordinals.Add(ResolveOrdinal(pred, columns));
                break;
        }
    }
}