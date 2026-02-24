// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Core.Primitives;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Builds closure-based BakedDelegate instances for individual predicates.
/// Direct method calls — no expression trees, no reflection.
/// </summary>
internal static class JitPredicateBuilder
{
    public static BakedDelegate Build(PredicateExpression pred,
                                     IReadOnlyList<ColumnInfo> columns,
                                     int rowidAliasOrdinal)
    {
        int ordinal = pred.ColumnOrdinal ?? columns.FirstOrDefault(c => c.Name.Equals(pred.ColumnName, StringComparison.OrdinalIgnoreCase))?.Ordinal ?? -1;

        if (ordinal < 0 || ordinal >= columns.Count)
            throw new ArgumentOutOfRangeException(nameof(pred), $"Column '{pred.ColumnName}' (ordinal {ordinal}) not found.");

        // NULL handling (SQLite semantics: NULL is never equal, but matches IS NULL)
        if (pred.Operator == FilterOp.IsNull)
            return (payload, serialTypes, offsets, rowId) =>
                FilterStarCompiler.GetSerialType(serialTypes, ordinal) == SerialTypeCodec.NullSerialType && ordinal != rowidAliasOrdinal;

        if (pred.Operator == FilterOp.IsNotNull)
            return (payload, serialTypes, offsets, rowId) =>
                FilterStarCompiler.GetSerialType(serialTypes, ordinal) != SerialTypeCodec.NullSerialType || ordinal == rowidAliasOrdinal;

        // RowID Alias
        if (ordinal == rowidAliasOrdinal) return BuildRowIdPredicate(pred);

        // Column Data
        return pred.Operator switch
        {
            FilterOp.Eq or FilterOp.Neq or FilterOp.Lt or FilterOp.Lte or FilterOp.Gt or FilterOp.Gte
                => BuildComparison(pred, ordinal),
            FilterOp.StartsWith or FilterOp.EndsWith or FilterOp.Contains
                => BuildStringOperation(pred, ordinal),
            FilterOp.Between
                => BuildBetween(pred, ordinal),
            FilterOp.In or FilterOp.NotIn
                => BuildIn(pred, ordinal),
            _ => static (_, _, _, _) => false
        };
    }

    private static BakedDelegate BuildStringOperation(PredicateExpression pred, int ordinal)
    {
        // Cache the UTF-8 bytes as a single heap array — captured by closure.
        byte[] utf8Bytes = pred.Value.AsUtf8().ToArray();

        return pred.Operator switch
        {
            FilterOp.StartsWith => (payload, serialTypes, offsets, rowId) =>
            {
                long st = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, st);
                return RawByteComparer.Utf8StartsWith(data, utf8Bytes);
            },
            FilterOp.EndsWith => (payload, serialTypes, offsets, rowId) =>
            {
                long st = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, st);
                return RawByteComparer.Utf8EndsWith(data, utf8Bytes);
            },
            FilterOp.Contains => (payload, serialTypes, offsets, rowId) =>
            {
                long st = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, st);
                return RawByteComparer.Utf8Contains(data, utf8Bytes);
            },
            _ => throw new NotSupportedException($"Operator {pred.Operator} is not a string operator.")
        };
    }

    private static BakedDelegate BuildComparison(PredicateExpression pred, int ordinal)
    {
        return pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64 => BuildInt64Comparison(ordinal, pred.Operator, pred.Value.AsInt64()),
            TypedFilterValue.Tag.Double => BuildDoubleComparison(ordinal, pred.Operator, pred.Value.AsDouble()),
            TypedFilterValue.Tag.Utf8 => BuildUtf8Comparison(ordinal, pred.Operator, pred.Value.AsUtf8().ToArray()),
            _ => static (_, _, _, _) => false
        };
    }

    // Int64 filter: prefer IsIntegral (same-type), fall back to IsReal (cross-type)
    private static BakedDelegate BuildInt64Comparison(int ordinal, FilterOp op, long filterValue)
    {
        double doubleFilterValue = (double)filterValue;
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            if (SerialTypeCodec.IsIntegral(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                return FilterStarCompiler.CompareOp(op, RawByteComparer.CompareInt64(data, serialType, filterValue));
            }
            if (SerialTypeCodec.IsReal(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                return FilterStarCompiler.CompareOp(op, RawByteComparer.CompareDouble(data, doubleFilterValue));
            }
            return false;
        };
    }

    // Double filter: prefer IsReal (same-type), fall back to IsIntegral (cross-type)
    private static BakedDelegate BuildDoubleComparison(int ordinal, FilterOp op, double filterValue)
    {
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            if (SerialTypeCodec.IsReal(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                return FilterStarCompiler.CompareOp(op, RawByteComparer.CompareDouble(data, filterValue));
            }
            if (SerialTypeCodec.IsIntegral(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                return FilterStarCompiler.CompareOp(op, RawByteComparer.CompareIntAsDouble(data, serialType, filterValue));
            }
            return false;
        };
    }

    private static BakedDelegate BuildUtf8Comparison(int ordinal, FilterOp op, byte[] utf8Bytes)
    {
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            if (SerialTypeCodec.IsText(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                return FilterStarCompiler.CompareOp(op, RawByteComparer.Utf8Compare(data, utf8Bytes));
            }
            return false;
        };
    }

    private static BakedDelegate BuildBetween(PredicateExpression pred, int ordinal)
    {
        return pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64Range => BuildInt64Between(ordinal, pred.Value.AsInt64(), pred.Value.AsInt64High()),
            TypedFilterValue.Tag.DoubleRange => BuildDoubleBetween(ordinal, pred.Value.AsDouble(), pred.Value.AsDoubleHigh()),
            _ => static (_, _, _, _) => false
        };
    }

    // Int64Range: prefer IsIntegral, cross-type fall back to IsReal
    private static BakedDelegate BuildInt64Between(int ordinal, long low, long high)
    {
        double doubleLow = (double)low;
        double doubleHigh = (double)high;
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            if (SerialTypeCodec.IsIntegral(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                long val = RawByteComparer.DecodeInt64(data, serialType);
                return val >= low && val <= high;
            }
            if (SerialTypeCodec.IsReal(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                double val = RawByteComparer.DecodeDouble(data);
                return val >= doubleLow && val <= doubleHigh;
            }
            return false;
        };
    }

    // DoubleRange: prefer IsReal, cross-type fall back to IsIntegral
    private static BakedDelegate BuildDoubleBetween(int ordinal, double low, double high)
    {
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            if (SerialTypeCodec.IsReal(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                double val = RawByteComparer.DecodeDouble(data);
                return val >= low && val <= high;
            }
            if (SerialTypeCodec.IsIntegral(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                double val = (double)RawByteComparer.DecodeInt64(data, serialType);
                return val >= low && val <= high;
            }
            return false;
        };
    }

    private static BakedDelegate BuildIn(PredicateExpression pred, int ordinal)
    {
        bool isNotIn = pred.Operator == FilterOp.NotIn;

        return pred.Value.ValueTag switch
        {
            TypedFilterValue.Tag.Int64Set => BuildInt64In(ordinal, new HashSet<long>(pred.Value.AsInt64Set()), isNotIn),
            TypedFilterValue.Tag.Utf8Set => BuildUtf8InZeroAlloc(ordinal, pred.Value.AsUtf8Set().Select(m => m.ToArray()).ToArray(), isNotIn),
            _ => static (_, _, _, _) => false
        };
    }

    private static BakedDelegate BuildInt64In(int ordinal, HashSet<long> set, bool negate)
    {
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            bool contains = false;
            if (SerialTypeCodec.IsIntegral(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                contains = set.Contains(RawByteComparer.DecodeInt64(data, serialType));
            }
            return negate ? !contains : contains;
        };
    }

    /// <summary>
    /// Zero-allocation UTF-8 IN predicate. Compares raw byte spans against
    /// pre-encoded UTF-8 keys — no per-row string materialization.
    /// </summary>
    private static BakedDelegate BuildUtf8InZeroAlloc(int ordinal, byte[][] utf8Keys, bool negate)
    {
        return (payload, serialTypes, offsets, rowId) =>
        {
            long serialType = FilterStarCompiler.GetSerialType(serialTypes, ordinal);
            if (serialType == SerialTypeCodec.NullSerialType) return false;

            bool contains = false;
            if (SerialTypeCodec.IsText(serialType))
            {
                var data = FilterStarCompiler.GetColumnData(payload, offsets, ordinal, serialType);
                contains = FilterStarCompiler.Utf8SetContainsBytes(data, utf8Keys);
            }
            return negate ? !contains : contains;
        };
    }

    private static BakedDelegate BuildRowIdPredicate(PredicateExpression pred)
    {
        if (pred.Operator == FilterOp.Between && pred.Value.ValueTag == TypedFilterValue.Tag.Int64Range)
        {
            long low = pred.Value.AsInt64();
            long high = pred.Value.AsInt64High();
            return (payload, serialTypes, offsets, rowId) => rowId >= low && rowId <= high;
        }

        if (pred.Value.ValueTag != TypedFilterValue.Tag.Int64) return static (_, _, _, _) => false;

        long val = pred.Value.AsInt64();

        return pred.Operator switch
        {
            FilterOp.Eq => (payload, serialTypes, offsets, rowId) => rowId == val,
            FilterOp.Neq => (payload, serialTypes, offsets, rowId) => rowId != val,
            FilterOp.Lt => (payload, serialTypes, offsets, rowId) => rowId < val,
            FilterOp.Lte => (payload, serialTypes, offsets, rowId) => rowId <= val,
            FilterOp.Gt => (payload, serialTypes, offsets, rowId) => rowId > val,
            FilterOp.Gte => (payload, serialTypes, offsets, rowId) => rowId >= val,
            _ => static (_, _, _, _) => false
        };
    }
}
