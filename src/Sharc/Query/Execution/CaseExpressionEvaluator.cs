// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Sharq.Ast;

namespace Sharc.Query.Execution;

/// <summary>
/// Evaluates CASE expression AST nodes against a materialized row.
/// Produces a <see cref="QueryValue"/> result per row.
/// </summary>
internal static class CaseExpressionEvaluator
{
    /// <summary>
    /// Pre-resolves all ColumnRefStar nodes in a CASE expression tree to their ordinals.
    /// Call once before the row loop, then pass the result to the cached Evaluate overload.
    /// </summary>
    internal static Dictionary<ColumnRefStar, int> PreResolveColumnRefs(
        CaseStar caseExpr, string[] columnNames)
    {
        var cache = new Dictionary<ColumnRefStar, int>(ReferenceEqualityComparer.Instance);
        WalkAndResolve(caseExpr, columnNames, cache);
        return cache;
    }

    private static void WalkAndResolve(
        SharqStar node, string[] columnNames, Dictionary<ColumnRefStar, int> cache)
    {
        switch (node)
        {
            case ColumnRefStar col:
                if (!cache.ContainsKey(col))
                {
                    string name = col.TableAlias != null
                        ? $"{col.TableAlias}.{col.Name}" : col.Name;
                    int ordinal = QueryValueOps.TryResolveOrdinal(columnNames, name);
                    if (ordinal < 0 && col.TableAlias != null)
                        ordinal = QueryValueOps.TryResolveOrdinal(columnNames, col.Name);
                    cache[col] = ordinal;
                }
                break;
            case BinaryStar bin:
                WalkAndResolve(bin.Left, columnNames, cache);
                WalkAndResolve(bin.Right, columnNames, cache);
                break;
            case UnaryStar un:
                WalkAndResolve(un.Operand, columnNames, cache);
                break;
            case IsNullStar isNull:
                WalkAndResolve(isNull.Operand, columnNames, cache);
                break;
            case CaseStar nested:
                foreach (var w in nested.Whens)
                {
                    WalkAndResolve(w.Condition, columnNames, cache);
                    WalkAndResolve(w.Result, columnNames, cache);
                }
                if (nested.ElseExpr != null)
                    WalkAndResolve(nested.ElseExpr, columnNames, cache);
                break;
        }
    }

    /// <summary>
    /// Evaluates a CASE expression against the given row using the column name map.
    /// </summary>
    internal static QueryValue Evaluate(CaseStar caseExpr, QueryValue[] row, string[] columnNames)
    {
        foreach (var when in caseExpr.Whens)
        {
            var condResult = EvalExpr(when.Condition, row, columnNames);
            if (IsTruthy(condResult))
                return EvalExpr(when.Result, row, columnNames);
        }

        return caseExpr.ElseExpr != null
            ? EvalExpr(caseExpr.ElseExpr, row, columnNames)
            : QueryValue.Null;
    }

    /// <summary>
    /// Evaluates with pre-resolved ordinal cache (zero per-row string allocation).
    /// </summary>
    internal static QueryValue Evaluate(
        CaseStar caseExpr, QueryValue[] row,
        Dictionary<ColumnRefStar, int> ordinalCache)
    {
        foreach (var when in caseExpr.Whens)
        {
            var condResult = EvalExprCached(when.Condition, row, ordinalCache);
            if (IsTruthy(condResult))
                return EvalExprCached(when.Result, row, ordinalCache);
        }

        return caseExpr.ElseExpr != null
            ? EvalExprCached(caseExpr.ElseExpr, row, ordinalCache)
            : QueryValue.Null;
    }

    private static QueryValue EvalExprCached(
        SharqStar expr, QueryValue[] row, Dictionary<ColumnRefStar, int> ordinalCache)
    {
        switch (expr)
        {
            case LiteralStar lit:
                return EvalLiteral(lit);
            case ColumnRefStar col:
                return ordinalCache.TryGetValue(col, out int ord) && ord >= 0
                    ? row[ord] : QueryValue.Null;
            case BinaryStar bin:
                return EvalBinaryCached(bin, row, ordinalCache);
            case UnaryStar un:
                var val = EvalExprCached(un.Operand, row, ordinalCache);
                if (val.IsNull) return QueryValue.Null;
                return un.Op switch
                {
                    UnaryOp.Not => QueryValue.FromInt64(IsTruthy(val) ? 0 : 1),
                    UnaryOp.Negate when val.Type == QueryValueType.Int64 => QueryValue.FromInt64(-val.AsInt64()),
                    UnaryOp.Negate when val.Type == QueryValueType.Double => QueryValue.FromDouble(-val.AsDouble()),
                    _ => QueryValue.Null,
                };
            case IsNullStar isNull:
                var isNullVal = EvalExprCached(isNull.Operand, row, ordinalCache);
                bool result = isNull.Negated ? !isNullVal.IsNull : isNullVal.IsNull;
                return QueryValue.FromInt64(result ? 1 : 0);
            case CaseStar nested:
                return Evaluate(nested, row, ordinalCache);
            default:
                return QueryValue.Null;
        }
    }

    private static QueryValue EvalBinaryCached(
        BinaryStar bin, QueryValue[] row, Dictionary<ColumnRefStar, int> ordinalCache)
    {
        var left = EvalExprCached(bin.Left, row, ordinalCache);
        var right = EvalExprCached(bin.Right, row, ordinalCache);

        if (left.IsNull || right.IsNull)
        {
            if (bin.Op == BinaryOp.And)
            {
                if (!left.IsNull && !IsTruthy(left)) return QueryValue.FromInt64(0);
                if (!right.IsNull && !IsTruthy(right)) return QueryValue.FromInt64(0);
            }
            if (bin.Op == BinaryOp.Or)
            {
                if (!left.IsNull && IsTruthy(left)) return QueryValue.FromInt64(1);
                if (!right.IsNull && IsTruthy(right)) return QueryValue.FromInt64(1);
            }
            return QueryValue.Null;
        }

        return bin.Op switch
        {
            BinaryOp.Equal => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) == 0 ? 1 : 0),
            BinaryOp.NotEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) != 0 ? 1 : 0),
            BinaryOp.LessThan => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) < 0 ? 1 : 0),
            BinaryOp.GreaterThan => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) > 0 ? 1 : 0),
            BinaryOp.LessOrEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) <= 0 ? 1 : 0),
            BinaryOp.GreaterOrEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) >= 0 ? 1 : 0),
            BinaryOp.And => QueryValue.FromInt64(IsTruthy(left) && IsTruthy(right) ? 1 : 0),
            BinaryOp.Or => QueryValue.FromInt64(IsTruthy(left) || IsTruthy(right) ? 1 : 0),
            BinaryOp.Add => EvalArithmetic(left, right, static (a, b) => a + b, static (a, b) => a + b),
            BinaryOp.Subtract => EvalArithmetic(left, right, static (a, b) => a - b, static (a, b) => a - b),
            BinaryOp.Multiply => EvalArithmetic(left, right, static (a, b) => a * b, static (a, b) => a * b),
            BinaryOp.Divide => EvalArithmetic(left, right, static (a, b) => b != 0 ? a / b : 0, static (a, b) => b != 0.0 ? a / b : 0.0),
            BinaryOp.Modulo => EvalArithmetic(left, right, static (a, b) => b != 0 ? a % b : 0, static (a, b) => b != 0.0 ? a % b : 0.0),
            _ => QueryValue.Null,
        };
    }

    private static QueryValue EvalExpr(SharqStar expr, QueryValue[] row, string[] columnNames)
    {
        switch (expr)
        {
            case LiteralStar lit:
                return EvalLiteral(lit);

            case ColumnRefStar col:
                return EvalColumnRef(col, row, columnNames);

            case BinaryStar bin:
                return EvalBinary(bin, row, columnNames);

            case UnaryStar un:
                return EvalUnary(un, row, columnNames);

            case IsNullStar isNull:
            {
                var val = EvalExpr(isNull.Operand, row, columnNames);
                bool result = isNull.Negated ? !val.IsNull : val.IsNull;
                return QueryValue.FromInt64(result ? 1 : 0);
            }

            case CaseStar nested:
                return Evaluate(nested, row, columnNames);

            default:
                return QueryValue.Null;
        }
    }

    private static QueryValue EvalLiteral(LiteralStar lit) => lit.Kind switch
    {
        LiteralKind.Null => QueryValue.Null,
        LiteralKind.Integer => QueryValue.FromInt64(lit.IntegerValue),
        LiteralKind.Float => QueryValue.FromDouble(lit.FloatValue),
        LiteralKind.String => QueryValue.FromString(lit.StringValue!),
        LiteralKind.Bool => QueryValue.FromInt64(lit.BoolValue ? 1 : 0),
        _ => QueryValue.Null,
    };

    private static QueryValue EvalColumnRef(ColumnRefStar col, QueryValue[] row, string[] columnNames)
    {
        string name = col.TableAlias != null ? $"{col.TableAlias}.{col.Name}" : col.Name;
        int ordinal = QueryValueOps.TryResolveOrdinal(columnNames, name);

        // If qualified name not found, try unqualified
        if (ordinal < 0 && col.TableAlias != null)
            ordinal = QueryValueOps.TryResolveOrdinal(columnNames, col.Name);

        return ordinal >= 0 ? row[ordinal] : QueryValue.Null;
    }

    private static QueryValue EvalBinary(BinaryStar bin, QueryValue[] row, string[] columnNames)
    {
        var left = EvalExpr(bin.Left, row, columnNames);
        var right = EvalExpr(bin.Right, row, columnNames);

        // NULL propagation for comparisons and arithmetic
        if (left.IsNull || right.IsNull)
        {
            // Logical operators: NULL AND false = false, NULL OR true = true
            if (bin.Op == BinaryOp.And)
            {
                if (!left.IsNull && !IsTruthy(left)) return QueryValue.FromInt64(0);
                if (!right.IsNull && !IsTruthy(right)) return QueryValue.FromInt64(0);
            }
            if (bin.Op == BinaryOp.Or)
            {
                if (!left.IsNull && IsTruthy(left)) return QueryValue.FromInt64(1);
                if (!right.IsNull && IsTruthy(right)) return QueryValue.FromInt64(1);
            }
            return QueryValue.Null;
        }

        return bin.Op switch
        {
            // Comparison
            BinaryOp.Equal => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) == 0 ? 1 : 0),
            BinaryOp.NotEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) != 0 ? 1 : 0),
            BinaryOp.LessThan => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) < 0 ? 1 : 0),
            BinaryOp.GreaterThan => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) > 0 ? 1 : 0),
            BinaryOp.LessOrEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) <= 0 ? 1 : 0),
            BinaryOp.GreaterOrEqual => QueryValue.FromInt64(QueryValueOps.CompareValues(left, right) >= 0 ? 1 : 0),

            // Logical
            BinaryOp.And => QueryValue.FromInt64(IsTruthy(left) && IsTruthy(right) ? 1 : 0),
            BinaryOp.Or => QueryValue.FromInt64(IsTruthy(left) || IsTruthy(right) ? 1 : 0),

            // Arithmetic
            BinaryOp.Add => EvalArithmetic(left, right, static (a, b) => a + b, static (a, b) => a + b),
            BinaryOp.Subtract => EvalArithmetic(left, right, static (a, b) => a - b, static (a, b) => a - b),
            BinaryOp.Multiply => EvalArithmetic(left, right, static (a, b) => a * b, static (a, b) => a * b),
            BinaryOp.Divide => EvalArithmetic(left, right, static (a, b) => b != 0 ? a / b : 0, static (a, b) => b != 0.0 ? a / b : 0.0),
            BinaryOp.Modulo => EvalArithmetic(left, right, static (a, b) => b != 0 ? a % b : 0, static (a, b) => b != 0.0 ? a % b : 0.0),

            _ => QueryValue.Null,
        };
    }

    private static QueryValue EvalArithmetic(
        QueryValue left, QueryValue right,
        Func<long, long, long> intOp,
        Func<double, double, double> dblOp)
    {
        // If either is double, promote to double
        if (left.Type == QueryValueType.Double || right.Type == QueryValueType.Double)
        {
            double l = left.Type == QueryValueType.Double ? left.AsDouble() : left.AsInt64();
            double r = right.Type == QueryValueType.Double ? right.AsDouble() : right.AsInt64();
            return QueryValue.FromDouble(dblOp(l, r));
        }
        if (left.Type == QueryValueType.Int64 && right.Type == QueryValueType.Int64)
            return QueryValue.FromInt64(intOp(left.AsInt64(), right.AsInt64()));
        return QueryValue.Null;
    }

    private static QueryValue EvalUnary(UnaryStar un, QueryValue[] row, string[] columnNames)
    {
        var val = EvalExpr(un.Operand, row, columnNames);
        if (val.IsNull) return QueryValue.Null;

        return un.Op switch
        {
            UnaryOp.Not => QueryValue.FromInt64(IsTruthy(val) ? 0 : 1),
            UnaryOp.Negate when val.Type == QueryValueType.Int64 => QueryValue.FromInt64(-val.AsInt64()),
            UnaryOp.Negate when val.Type == QueryValueType.Double => QueryValue.FromDouble(-val.AsDouble()),
            _ => QueryValue.Null,
        };
    }

    /// <summary>
    /// SQL truthiness: 0 and NULL are false, non-zero integers and non-zero doubles are true.
    /// </summary>
    private static bool IsTruthy(QueryValue val)
    {
        if (val.IsNull) return false;
        return val.Type switch
        {
            QueryValueType.Int64 => val.AsInt64() != 0,
            QueryValueType.Double => val.AsDouble() != 0.0,
            QueryValueType.Text => true, // non-null text is truthy
            _ => false,
        };
    }
}
