// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core.Schema;

namespace Sharc.Views;

/// <summary>
/// Converts a <see cref="IFilterStar"/> expression tree into a
/// <see cref="Func{IRowAccessor, Boolean}"/> predicate for use with
/// <see cref="SharcView"/> row-level filtering.
/// </summary>
/// <remarks>
/// FilterStar operates at the byte level (BakedDelegate) for zero-alloc table scans.
/// Views operate at the decoded value level (IRowAccessor). This bridge translates
/// between the two representations so that JitQuery filters can be applied to view cursors.
/// </remarks>
internal static class ViewFilterBridge
{
    /// <summary>
    /// Converts an IFilterStar expression tree into a Func&lt;IRowAccessor, bool&gt;
    /// using table column metadata for ordinal resolution.
    /// </summary>
    internal static Func<IRowAccessor, bool> Convert(IFilterStar expression, IReadOnlyList<ColumnInfo> columns)
    {
        return ConvertNode(expression, pred => ResolveOrdinal(pred, columns));
    }

    /// <summary>
    /// Converts an IFilterStar expression tree into a Func&lt;IRowAccessor, bool&gt;
    /// using positional column names for ordinal resolution (view path).
    /// </summary>
    internal static Func<IRowAccessor, bool> Convert(IFilterStar expression, string[] columnNames)
    {
        return ConvertNode(expression, pred => ResolveOrdinalByName(pred, columnNames));
    }

    private static Func<IRowAccessor, bool> ConvertNode(IFilterStar expr, Func<PredicateExpression, int> resolver)
    {
        return expr switch
        {
            AndExpression and => ConvertAnd(and, resolver),
            OrExpression or => ConvertOr(or, resolver),
            NotExpression not => ConvertNot(not, resolver),
            PredicateExpression pred => ConvertPredicate(pred, resolver),
            _ => throw new ArgumentException($"Unknown filter expression type: {expr.GetType().Name}")
        };
    }

    private static Func<IRowAccessor, bool> ConvertAnd(AndExpression and, Func<PredicateExpression, int> resolver)
    {
        var predicates = new Func<IRowAccessor, bool>[and.Children.Length];
        for (int i = 0; i < and.Children.Length; i++)
            predicates[i] = ConvertNode(and.Children[i], resolver);

        return row =>
        {
            for (int i = 0; i < predicates.Length; i++)
                if (!predicates[i](row)) return false;
            return true;
        };
    }

    private static Func<IRowAccessor, bool> ConvertOr(OrExpression or, Func<PredicateExpression, int> resolver)
    {
        var predicates = new Func<IRowAccessor, bool>[or.Children.Length];
        for (int i = 0; i < or.Children.Length; i++)
            predicates[i] = ConvertNode(or.Children[i], resolver);

        return row =>
        {
            for (int i = 0; i < predicates.Length; i++)
                if (predicates[i](row)) return true;
            return false;
        };
    }

    private static Func<IRowAccessor, bool> ConvertNot(NotExpression not, Func<PredicateExpression, int> resolver)
    {
        var inner = ConvertNode(not.Inner, resolver);
        return row => !inner(row);
    }

    private static Func<IRowAccessor, bool> ConvertPredicate(PredicateExpression pred, Func<PredicateExpression, int> resolver)
    {
        int ordinal = resolver(pred);
        var op = pred.Operator;
        var value = pred.Value;

        return value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64 => BuildInt64(ordinal, op, value.AsInt64()),
            TypedFilterValue.Tag.Double => BuildDouble(ordinal, op, value.AsDouble()),
            TypedFilterValue.Tag.Utf8 => BuildString(ordinal, op, Encoding.UTF8.GetString(value.AsUtf8())),
            TypedFilterValue.Tag.Null => BuildNull(ordinal, op),
            _ => throw new NotSupportedException($"ViewFilterBridge does not support filter value tag: {value.ValueTag}")
        };
    }

    private static int ResolveOrdinal(PredicateExpression pred, IReadOnlyList<ColumnInfo> columns)
    {
        if (pred.ColumnOrdinal.HasValue)
            return pred.ColumnOrdinal.Value;

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

    private static int ResolveOrdinalByName(PredicateExpression pred, string[] columnNames)
    {
        if (pred.ColumnOrdinal.HasValue)
            return pred.ColumnOrdinal.Value;

        if (pred.ColumnName != null)
        {
            for (int i = 0; i < columnNames.Length; i++)
            {
                if (columnNames[i].Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            throw new ArgumentException($"Filter column '{pred.ColumnName}' not found in view.");
        }

        throw new ArgumentException("Filter predicate must specify either column name or ordinal.");
    }

    private static Func<IRowAccessor, bool> BuildInt64(int ordinal, FilterOp op, long value)
    {
        return op switch
        {
            FilterOp.Eq => row => !row.IsNull(ordinal) && row.GetInt64(ordinal) == value,
            FilterOp.Neq => row => row.IsNull(ordinal) || row.GetInt64(ordinal) != value,
            FilterOp.Lt => row => !row.IsNull(ordinal) && row.GetInt64(ordinal) < value,
            FilterOp.Lte => row => !row.IsNull(ordinal) && row.GetInt64(ordinal) <= value,
            FilterOp.Gt => row => !row.IsNull(ordinal) && row.GetInt64(ordinal) > value,
            FilterOp.Gte => row => !row.IsNull(ordinal) && row.GetInt64(ordinal) >= value,
            _ => throw new NotSupportedException($"ViewFilterBridge does not support operator {op} for Int64.")
        };
    }

    private static Func<IRowAccessor, bool> BuildDouble(int ordinal, FilterOp op, double value)
    {
        return op switch
        {
            FilterOp.Eq => row => !row.IsNull(ordinal) && row.GetDouble(ordinal) == value,
            FilterOp.Neq => row => row.IsNull(ordinal) || row.GetDouble(ordinal) != value,
            FilterOp.Lt => row => !row.IsNull(ordinal) && row.GetDouble(ordinal) < value,
            FilterOp.Lte => row => !row.IsNull(ordinal) && row.GetDouble(ordinal) <= value,
            FilterOp.Gt => row => !row.IsNull(ordinal) && row.GetDouble(ordinal) > value,
            FilterOp.Gte => row => !row.IsNull(ordinal) && row.GetDouble(ordinal) >= value,
            _ => throw new NotSupportedException($"ViewFilterBridge does not support operator {op} for Double.")
        };
    }

    private static Func<IRowAccessor, bool> BuildString(int ordinal, FilterOp op, string value)
    {
        return op switch
        {
            FilterOp.Eq => row => !row.IsNull(ordinal) && string.Equals(row.GetString(ordinal), value, StringComparison.Ordinal),
            FilterOp.Neq => row => row.IsNull(ordinal) || !string.Equals(row.GetString(ordinal), value, StringComparison.Ordinal),
            FilterOp.Lt => row => !row.IsNull(ordinal) && string.Compare(row.GetString(ordinal), value, StringComparison.Ordinal) < 0,
            FilterOp.Lte => row => !row.IsNull(ordinal) && string.Compare(row.GetString(ordinal), value, StringComparison.Ordinal) <= 0,
            FilterOp.Gt => row => !row.IsNull(ordinal) && string.Compare(row.GetString(ordinal), value, StringComparison.Ordinal) > 0,
            FilterOp.Gte => row => !row.IsNull(ordinal) && string.Compare(row.GetString(ordinal), value, StringComparison.Ordinal) >= 0,
            _ => throw new NotSupportedException($"ViewFilterBridge does not support operator {op} for String.")
        };
    }

    private static Func<IRowAccessor, bool> BuildNull(int ordinal, FilterOp op)
    {
        return op switch
        {
            FilterOp.Eq => row => row.IsNull(ordinal),
            FilterOp.Neq => row => !row.IsNull(ordinal),
            _ => throw new NotSupportedException($"ViewFilterBridge does not support operator {op} for Null.")
        };
    }
}
