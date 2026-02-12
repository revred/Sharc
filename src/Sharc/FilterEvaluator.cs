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

using Sharc.Core;

namespace Sharc;

/// <summary>
/// A filter with its column ordinal pre-resolved for efficient per-row evaluation.
/// </summary>
internal readonly struct ResolvedFilter
{
    public required int ColumnOrdinal { get; init; }
    public required SharcOperator Operator { get; init; }
    public required object? Value { get; init; }
}

/// <summary>
/// Evaluates filter conditions against decoded column values.
/// All comparisons follow SQLite affinity and NULL semantics.
/// </summary>
internal static class FilterEvaluator
{
    /// <summary>
    /// Evaluates all filters against the current row (AND semantics).
    /// Returns true only if every filter matches.
    /// </summary>
    public static bool MatchesAll(ResolvedFilter[] filters, ColumnValue[] row)
    {
        for (int i = 0; i < filters.Length; i++)
        {
            ref readonly var f = ref filters[i];
            if (!Matches(row[f.ColumnOrdinal], f.Operator, f.Value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Evaluates a single filter condition against a column value.
    /// </summary>
    internal static bool Matches(ColumnValue column, SharcOperator op, object? filterValue)
    {
        // DECISION: SQLite NULL semantics — NULL never matches any comparison.
        // To find NULLs, callers would need a dedicated IsNull filter (future enhancement).
        if (column.IsNull)
            return false;

        if (filterValue is null)
            return false;

        int? cmp = CompareColumnValue(column, filterValue);
        if (cmp is null)
            return false;

        return op switch
        {
            SharcOperator.Equal => cmp == 0,
            SharcOperator.NotEqual => cmp != 0,
            SharcOperator.LessThan => cmp < 0,
            SharcOperator.GreaterThan => cmp > 0,
            SharcOperator.LessOrEqual => cmp <= 0,
            SharcOperator.GreaterOrEqual => cmp >= 0,
            _ => false
        };
    }

    /// <summary>
    /// Compares a ColumnValue to a CLR object. Returns negative, zero, or positive.
    /// Returns null if comparison is undefined (incompatible types).
    /// </summary>
    internal static int? CompareColumnValue(ColumnValue column, object filterValue)
    {
        return column.StorageClass switch
        {
            ColumnStorageClass.Integral => filterValue switch
            {
                long l => column.AsInt64().CompareTo(l),
                int i => column.AsInt64().CompareTo((long)i),
                double d => ((double)column.AsInt64()).CompareTo(d),
                _ => null
            },
            ColumnStorageClass.Real => filterValue switch
            {
                double d => column.AsDouble().CompareTo(d),
                float f => column.AsDouble().CompareTo((double)f),
                long l => column.AsDouble().CompareTo((double)l),
                int i => column.AsDouble().CompareTo((double)i),
                _ => null
            },
            ColumnStorageClass.Text => filterValue switch
            {
                string s => string.Compare(column.AsString(), s, StringComparison.Ordinal),
                _ => null
            },
            _ => null
        };
    }
}
