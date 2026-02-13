/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Compiles an IFilterStar expression tree into an IFilterNode evaluation tree.
/// Resolves column names to ordinals and validates the expression.
/// </summary>
internal static class FilterCompiler
{
    private const int MaxDepth = 32;

    /// <summary>
    /// Compiles a filter expression into an evaluation node tree.
    /// </summary>
    /// <param name="expression">The uncompiled filter expression.</param>
    /// <param name="columns">Table column definitions for name → ordinal resolution.</param>
    /// <param name="rowidAliasOrdinal">Ordinal of the INTEGER PRIMARY KEY alias column, or -1.</param>
    /// <returns>A compiled filter node tree ready for per-row evaluation.</returns>
    internal static IFilterNode Compile(IFilterStar expression, IReadOnlyList<ColumnInfo> columns,
                                        int rowidAliasOrdinal = -1)
    {
        return CompileNode(expression, columns, rowidAliasOrdinal, 0);
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

    private static PredicateNode CompilePredicate(PredicateExpression pred,
                                                   IReadOnlyList<ColumnInfo> columns,
                                                   int rowidAliasOrdinal)
    {
        int ordinal;

        if (pred.ColumnOrdinal.HasValue)
        {
            ordinal = pred.ColumnOrdinal.Value;
            if (ordinal < 0 || ordinal >= columns.Count)
                throw new ArgumentOutOfRangeException(nameof(pred),
                    $"Column ordinal {ordinal} is out of range (0..{columns.Count - 1}).");
        }
        else if (pred.ColumnName != null)
        {
            ordinal = -1;
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    ordinal = columns[i].Ordinal;
                    break;
                }
            }
            if (ordinal < 0)
                throw new ArgumentException(
                    $"Filter column '{pred.ColumnName}' not found in table.");
        }
        else
        {
            throw new ArgumentException("Filter predicate must specify either column name or ordinal.");
        }

        return new PredicateNode(ordinal, pred.Operator, pred.Value, rowidAliasOrdinal);
    }
}
