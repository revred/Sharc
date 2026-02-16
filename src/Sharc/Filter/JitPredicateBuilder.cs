using System.Linq.Expressions;
using System.Text;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Specialized builder for Predicate expressions in the FilterStar JIT.
/// Adheres to SRP by focusing only on column-and-field-level logic.
/// </summary>
internal static class JitPredicateBuilder
{
    public static Expression Build(PredicateExpression pred,
                                 IReadOnlyList<ColumnInfo> columns,
                                 int rowidAliasOrdinal,
                                 ParameterExpression payload,
                                 ParameterExpression serialTypes,
                                 ParameterExpression offsets,
                                 ParameterExpression rowId)
    {
        int ordinal = pred.ColumnOrdinal ?? columns.FirstOrDefault(c => c.Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))?.Ordinal ?? -1;

        if (ordinal < 0 || ordinal >= columns.Count)
            throw new ArgumentOutOfRangeException(nameof(pred), $"Column '{pred.ColumnName}' (ordinal {ordinal}) not found.");

        var ordinalExpr = Expression.Constant(ordinal);
        var serialTypeExpr = Expression.Call(JitMethodRegistry.GetSerialType, serialTypes, ordinalExpr);

        // NULL handling (SQLite semantics: NULL is never equal, but matches IS NULL)
        if (pred.Operator == FilterOp.IsNull)
            return Expression.AndAlso(Expression.Equal(serialTypeExpr, Expression.Constant(0L)), Expression.NotEqual(ordinalExpr, Expression.Constant(rowidAliasOrdinal)));

        if (pred.Operator == FilterOp.IsNotNull)
            return Expression.OrElse(Expression.NotEqual(serialTypeExpr, Expression.Constant(0L)), Expression.Equal(ordinalExpr, Expression.Constant(rowidAliasOrdinal)));

        // RowID Alias
        if (ordinal == rowidAliasOrdinal) return BuildRowIdPredicate(pred, rowId);

        // Column Data
        var offsetExpr = Expression.Call(JitMethodRegistry.GetOffset, offsets, ordinalExpr);
        var lengthExpr = Expression.Call(JitMethodRegistry.GetContentSize, serialTypeExpr);
        var dataExpr = Expression.Call(JitMethodRegistry.GetSlice, payload, offsetExpr, lengthExpr);

        return pred.Operator switch
        {
            FilterOp.Eq or FilterOp.Neq or FilterOp.Lt or FilterOp.Lte or FilterOp.Gt or FilterOp.Gte 
                => BuildComparison(pred, serialTypeExpr, dataExpr),
            FilterOp.StartsWith or FilterOp.EndsWith or FilterOp.Contains
                => BuildStringOperation(pred, dataExpr),
            FilterOp.Between 
                => BuildBetween(pred, serialTypeExpr, dataExpr),
            FilterOp.In or FilterOp.NotIn 
                => BuildIn(pred, serialTypeExpr, dataExpr),
            _ => Expression.Constant(false)
        };
    }

    private static MethodCallExpression BuildStringOperation(PredicateExpression pred, Expression data)
    {
        var method = pred.Operator switch
        {
            FilterOp.StartsWith => JitMethodRegistry.Utf8StartsWith,
            FilterOp.EndsWith => JitMethodRegistry.Utf8EndsWith,
            FilterOp.Contains => JitMethodRegistry.Utf8Contains,
            _ => throw new NotSupportedException($"Operator {pred.Operator} is not a string operator.")
        };

        return Expression.Call(method, data, Expression.Constant(pred.Value.AsUtf8().ToArray()));
    }

    private static ConditionalExpression BuildComparison(PredicateExpression pred, Expression serialType, Expression data)
    {
        var isNull = Expression.Equal(serialType, Expression.Constant(0L));
        Expression comparison = pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64 => Expression.Condition(Expression.Call(JitMethodRegistry.IsIntegral, serialType), BuildOp(pred.Operator, Expression.Call(JitMethodRegistry.CompareInt64, data, serialType, Expression.Constant(pred.Value.AsInt64()))), Expression.Constant(false)),
            TypedFilterValue.Tag.Double => Expression.Condition(Expression.Call(JitMethodRegistry.IsReal, serialType), BuildOp(pred.Operator, Expression.Call(JitMethodRegistry.CompareDouble, data, Expression.Constant(pred.Value.AsDouble()))), Expression.Constant(false)),
            TypedFilterValue.Tag.Utf8 => Expression.Condition(Expression.Call(JitMethodRegistry.IsText, serialType), BuildOp(pred.Operator, Expression.Call(JitMethodRegistry.Utf8Compare, data, Expression.Constant(pred.Value.AsUtf8().ToArray()))), Expression.Constant(false)),
            _ => Expression.Constant(false)
        };

        return Expression.Condition(isNull, Expression.Constant(false), comparison);
    }

    private static ConditionalExpression BuildBetween(PredicateExpression pred, Expression serialType, Expression data)
    {
        var isNull = Expression.Equal(serialType, Expression.Constant(0L));
        Expression comparison = pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64Range => Expression.Condition(Expression.Call(JitMethodRegistry.IsIntegral, serialType), BuildRangeCmp(Expression.Call(JitMethodRegistry.DecodeInt64, data, serialType), pred.Value.AsInt64(), pred.Value.AsInt64High()), Expression.Constant(false)),
            TypedFilterValue.Tag.DoubleRange => Expression.Condition(Expression.Call(JitMethodRegistry.IsReal, serialType), BuildRangeCmp(Expression.Call(JitMethodRegistry.DecodeDouble, data), pred.Value.AsDouble(), pred.Value.AsDoubleHigh()), Expression.Constant(false)),
            _ => Expression.Constant(false)
        };

        return Expression.Condition(isNull, Expression.Constant(false), comparison);
    }

    private static ConditionalExpression BuildIn(PredicateExpression pred, Expression serialType, Expression data)
    {
        var isNull = Expression.Equal(serialType, Expression.Constant(0L));
        Expression comparison = pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64Set => Expression.Condition(Expression.Call(JitMethodRegistry.IsIntegral, serialType), Expression.Call(Expression.Constant(new HashSet<long>(pred.Value.AsInt64Set())), JitMethodRegistry.HashSetInt64Contains, Expression.Call(JitMethodRegistry.DecodeInt64, data, serialType)), Expression.Constant(false)),
            TypedFilterValue.Tag.Utf8Set => Expression.Condition(Expression.Call(JitMethodRegistry.IsText, serialType), Expression.Call(JitMethodRegistry.Utf8SetContains, data, Expression.Constant(new HashSet<string>(pred.Value.AsUtf8Set().Select(m => Encoding.UTF8.GetString(m.Span))))), Expression.Constant(false)),
            _ => Expression.Constant(false)
        };

        if (pred.Operator == FilterOp.NotIn) comparison = Expression.Not(comparison);
        return Expression.Condition(isNull, Expression.Constant(false), comparison);
    }

    private static BinaryExpression BuildRangeCmp(Expression val, object low, object high) 
        => Expression.AndAlso(Expression.GreaterThanOrEqual(val, Expression.Constant(low)), Expression.LessThanOrEqual(val, Expression.Constant(high)));

    private static Expression BuildOp(FilterOp op, Expression cmpResult) => op switch
    {
        FilterOp.Eq => Expression.Equal(cmpResult, Expression.Constant(0)),
        FilterOp.Neq => Expression.NotEqual(cmpResult, Expression.Constant(0)),
        FilterOp.Lt => Expression.LessThan(cmpResult, Expression.Constant(0)),
        FilterOp.Lte => Expression.LessThanOrEqual(cmpResult, Expression.Constant(0)),
        FilterOp.Gt => Expression.GreaterThan(cmpResult, Expression.Constant(0)),
        FilterOp.Gte => Expression.GreaterThanOrEqual(cmpResult, Expression.Constant(0)),
        _ => Expression.Constant(false)
    };

    private static Expression BuildRowIdPredicate(PredicateExpression pred, Expression rowId)
    {
        if (pred.Operator == FilterOp.Between && pred.Value.ValueTag == TypedFilterValue.Tag.Int64Range)
            return BuildRangeCmp(rowId, pred.Value.AsInt64(), pred.Value.AsInt64High());

        if (pred.Value.ValueTag != TypedFilterValue.Tag.Int64) return Expression.Constant(false);
        var val = Expression.Constant(pred.Value.AsInt64());
        
        return pred.Operator switch
        {
            FilterOp.Eq => Expression.Equal(rowId, val),
            FilterOp.Neq => Expression.NotEqual(rowId, val),
            FilterOp.Lt => Expression.LessThan(rowId, val),
            FilterOp.Lte => Expression.LessThanOrEqual(rowId, val),
            FilterOp.Gt => Expression.GreaterThan(rowId, val),
            FilterOp.Gte => Expression.GreaterThanOrEqual(rowId, val),
            _ => Expression.Constant(false)
        };
    }
}
