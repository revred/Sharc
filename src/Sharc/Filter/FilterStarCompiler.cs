/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Compiles IFilterStar expressions into closure-composed delegates.
/// Direct delegate composition — no expression trees, no reflection, AOT-safe.
/// </summary>
internal static class FilterStarCompiler
{
    public static BakedDelegate Compile(IFilterStar expression, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        var columnMap = BuildColumnMap(columns);
        return BuildDelegate(expression, columns, columnMap, rowidAliasOrdinal);
    }

    internal static BakedDelegate Compile(
        IFilterStar expression,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        return BuildDelegate(expression, columns, columnMap, rowidAliasOrdinal);
    }

    private static BakedDelegate BuildDelegate(
        IFilterStar expr,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        return expr switch
        {
            AndExpression and => BuildAnd(and, columns, columnMap, rowidAliasOrdinal),
            OrExpression or => BuildOr(or, columns, columnMap, rowidAliasOrdinal),
            NotExpression not => BuildNot(not, columns, columnMap, rowidAliasOrdinal),
            PredicateExpression pred => BuildPredicate(pred, columns, columnMap, rowidAliasOrdinal),
            _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}")
        };
    }

    private static BakedDelegate BuildPredicate(
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
            return JitPredicateBuilder.BuildGuidComparison(hiOrdinal, loOrdinal, pred.Operator, pred.Value);
        }

        // Decimal expansion for merged FIX128 columns (__hi/__lo).
        if (pred.Value.ValueTag == TypedFilterValue.Tag.Decimal &&
            col.IsMergedDecimalColumn &&
            col.MergedPhysicalOrdinals is { Length: 2 } decimalOrdinals)
        {
            var hiOrdinal = decimalOrdinals[0];
            var loOrdinal = decimalOrdinals[1];
            return JitPredicateBuilder.BuildMergedDecimalComparison(hiOrdinal, loOrdinal, pred.Operator, pred.Value);
        }

        return JitPredicateBuilder.Build(pred, col, rowidAliasOrdinal);
    }

    private static BakedDelegate BuildAnd(
        AndExpression and,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        if (and.Children.Length == 0) return static (_, _, _, _) => true;

        // Sort children by estimated cost so cheap predicates short-circuit first
        var children = and.Children;
        if (children.Length > 1)
        {
            children = (IFilterStar[])children.Clone();
            Array.Sort(children, (a, b) =>
                EstimateCost(a, columnMap, rowidAliasOrdinal)
                    .CompareTo(EstimateCost(b, columnMap, rowidAliasOrdinal)));
        }

        var delegates = new BakedDelegate[children.Length];
        for (int i = 0; i < children.Length; i++)
            delegates[i] = BuildDelegate(children[i], columns, columnMap, rowidAliasOrdinal);

        if (delegates.Length == 1) return delegates[0];
        if (delegates.Length == 2)
        {
            var a = delegates[0]; var b = delegates[1];
            return (payload, serialTypes, offsets, rowId) =>
                a(payload, serialTypes, offsets, rowId) && b(payload, serialTypes, offsets, rowId);
        }

        return (payload, serialTypes, offsets, rowId) =>
        {
            for (int i = 0; i < delegates.Length; i++)
                if (!delegates[i](payload, serialTypes, offsets, rowId)) return false;
            return true;
        };
    }

    private static BakedDelegate BuildOr(
        OrExpression or,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        if (or.Children.Length == 0) return static (_, _, _, _) => false;

        var delegates = new BakedDelegate[or.Children.Length];
        for (int i = 0; i < or.Children.Length; i++)
            delegates[i] = BuildDelegate(or.Children[i], columns, columnMap, rowidAliasOrdinal);

        if (delegates.Length == 1) return delegates[0];
        if (delegates.Length == 2)
        {
            var a = delegates[0]; var b = delegates[1];
            return (payload, serialTypes, offsets, rowId) =>
                a(payload, serialTypes, offsets, rowId) || b(payload, serialTypes, offsets, rowId);
        }

        return (payload, serialTypes, offsets, rowId) =>
        {
            for (int i = 0; i < delegates.Length; i++)
                if (delegates[i](payload, serialTypes, offsets, rowId)) return true;
            return false;
        };
    }

    private static BakedDelegate BuildNot(
        NotExpression not,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        var inner = BuildDelegate(not.Inner, columns, columnMap, rowidAliasOrdinal);
        return (payload, serialTypes, offsets, rowId) => !inner(payload, serialTypes, offsets, rowId);
    }

    private static ColumnInfo ResolveColumn(
        PredicateExpression pred,
        IReadOnlyList<ColumnInfo> columns,
        Dictionary<string, ColumnInfo> columnMap)
    {
        if (pred.ColumnOrdinal is int ordinal)
        {
            if ((uint)ordinal >= (uint)columns.Count)
                throw new ArgumentException($"Filter column ordinal {ordinal} is out of range.");
            return columns[ordinal];
        }

        if (pred.ColumnName != null
            && columnMap.TryGetValue(pred.ColumnName, out var col)
            && col != null)
            return col;

        throw new ArgumentException($"Filter column '{pred.ColumnName}' not found.");
    }

    /// <summary>
    /// Estimates the per-row evaluation cost of a filter expression.
    /// Used to reorder AND children so cheap predicates short-circuit first.
    /// </summary>
    private static int EstimateCost(
        IFilterStar expr,
        Dictionary<string, ColumnInfo> columnMap,
        int rowidAliasOrdinal)
    {
        if (expr is not PredicateExpression pred)
            return 10; // nested AND/OR/NOT — highest cost

        // Resolve ordinal to check if this is a rowid predicate
        int ordinal = -1;
        if (pred.ColumnOrdinal is int explicitOrdinal)
        {
            ordinal = explicitOrdinal;
        }
        else if (pred.ColumnName != null
            && columnMap.TryGetValue(pred.ColumnName, out var col)
            && col != null)
        {
            ordinal = col.MergedPhysicalOrdinals?[0] ?? col.Ordinal;
        }

        if (ordinal == rowidAliasOrdinal)
            return 0; // rowid comparison — no column decode needed

        return pred.Operator switch
        {
            FilterOp.Eq or FilterOp.Neq when pred.Value.ValueTag == TypedFilterValue.Tag.Int64 => 1,
            FilterOp.Lt or FilterOp.Lte or FilterOp.Gt or FilterOp.Gte
                when pred.Value.ValueTag == TypedFilterValue.Tag.Int64 => 1,
            FilterOp.Between when pred.Value.ValueTag == TypedFilterValue.Tag.Int64Range => 1,
            FilterOp.IsNull or FilterOp.IsNotNull => 1,
            FilterOp.Eq or FilterOp.Neq when pred.Value.ValueTag == TypedFilterValue.Tag.Double => 2,
            FilterOp.Between when pred.Value.ValueTag == TypedFilterValue.Tag.DoubleRange => 2,
            FilterOp.Eq or FilterOp.Neq when pred.Value.ValueTag == TypedFilterValue.Tag.Decimal => 2,
            FilterOp.Eq or FilterOp.Neq when pred.Value.ValueTag == TypedFilterValue.Tag.Utf8 => 3,
            FilterOp.StartsWith => 4,
            FilterOp.Contains or FilterOp.EndsWith => 5,
            FilterOp.In or FilterOp.NotIn => 6,
            _ => 3
        };
    }

    private static Dictionary<string, ColumnInfo> BuildColumnMap(IReadOnlyList<ColumnInfo> columns)
    {
        var map = new Dictionary<string, ColumnInfo>(columns.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
            map[columns[i].Name] = columns[i];
        return map;
    }

    // —— Shared helper methods used by JitPredicateBuilder closures ——

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetSerialType(ReadOnlySpan<long> serialTypes, int index)
    {
        uint idx = (uint)index;
        return idx < (uint)serialTypes.Length ? serialTypes[index] : 0L;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOffset(ReadOnlySpan<int> offsets, int index)
    {
        uint idx = (uint)index;
        return idx < (uint)offsets.Length ? offsets[index] : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> GetSlice(ReadOnlySpan<byte> payload, int offset, int length)
    {
        uint uOffset = (uint)offset;
        uint uLength = (uint)length;
        if (uOffset + uLength > (uint)payload.Length) return [];
        return payload.Slice(offset, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> GetColumnData(ReadOnlySpan<byte> payload, ReadOnlySpan<int> offsets, int ordinal, long serialType)
    {
        int offset = GetOffset(offsets, ordinal);
        int length = SerialTypeCodec.GetContentSize(serialType);
        return GetSlice(payload, offset, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareOp(FilterOp op, int cmpResult) => op switch
    {
        FilterOp.Eq => cmpResult == 0,
        FilterOp.Neq => cmpResult != 0,
        FilterOp.Lt => cmpResult < 0,
        FilterOp.Lte => cmpResult <= 0,
        FilterOp.Gt => cmpResult > 0,
        FilterOp.Gte => cmpResult >= 0,
        _ => false
    };

    /// <summary>
    /// Zero-allocation UTF-8 set membership check.
    /// Compares raw byte spans against pre-encoded UTF-8 keys.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Utf8SetContainsBytes(ReadOnlySpan<byte> data, byte[][] utf8Keys)
    {
        if (data.IsEmpty || utf8Keys.Length == 0) return false;
        for (int i = 0; i < utf8Keys.Length; i++)
        {
            if (data.SequenceEqual(utf8Keys[i]))
                return true;
        }
        return false;
    }
}
