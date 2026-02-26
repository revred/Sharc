// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using global::Sharc.Core.Query;

namespace Sharc;

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
        // DECISION: SQLite NULL semantics â€” NULL never matches any comparison.
        // To find NULLs, callers would need a dedicated IsNull filter (future enhancement).
        if (column.IsNull)
            return false;

        if (filterValue is null)
            return false;

        if (op is SharcOperator.Equal or SharcOperator.NotEqual &&
            TryMatchNumericEquality(column, filterValue, out bool numericEqual))
        {
            return op == SharcOperator.Equal ? numericEqual : !numericEqual;
        }

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
        // ... (existing implementation) ...
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
            ColumnStorageClass.Blob => filterValue switch
            {
                decimal d
                    when column.AsBytes().Length == Sharc.Core.Primitives.DecimalCodec.ByteCount
                    && Sharc.Core.Primitives.DecimalCodec.TryDecode(column.AsBytes().Span, out decimal colDecimal)
                    => colDecimal.CompareTo(Sharc.Core.Primitives.DecimalCodec.Normalize(d)),
                _ => null
            },
            _ => null
        };
    }

    private static bool TryMatchNumericEquality(ColumnValue column, object filterValue, out bool isEqual)
    {
        switch (column.StorageClass)
        {
            case ColumnStorageClass.Integral:
            {
                long integral = column.AsInt64();
                switch (filterValue)
                {
                    case long l:
                        isEqual = integral == l;
                        return true;
                    case int i:
                        isEqual = integral == i;
                        return true;
                    case double d:
                        isEqual = RawByteComparer.AreClose(integral, d);
                        return true;
                    case float f:
                        isEqual = RawByteComparer.AreClose(integral, f);
                        return true;
                }
                break;
            }
            case ColumnStorageClass.Real:
            {
                double real = column.AsDouble();
                switch (filterValue)
                {
                    case double d:
                        isEqual = RawByteComparer.AreClose(real, d);
                        return true;
                    case float f:
                        isEqual = RawByteComparer.AreClose(real, f);
                        return true;
                    case long l:
                        isEqual = RawByteComparer.AreClose(real, l);
                        return true;
                    case int i:
                        isEqual = RawByteComparer.AreClose(real, i);
                        return true;
                }
                break;
            }
        }

        isEqual = false;
        return false;
    }
}
