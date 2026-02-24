/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Runtime.CompilerServices;
using System.Text;
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
        return BuildDelegate(expression, columns, rowidAliasOrdinal);
    }

    private static BakedDelegate BuildDelegate(IFilterStar expr, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        return expr switch
        {
            AndExpression and => BuildAnd(and, columns, rowidAliasOrdinal),
            OrExpression or => BuildOr(or, columns, rowidAliasOrdinal),
            NotExpression not => BuildNot(not, columns, rowidAliasOrdinal),
            PredicateExpression pred => JitPredicateBuilder.Build(pred, columns, rowidAliasOrdinal),
            _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}")
        };
    }

    private static BakedDelegate BuildAnd(AndExpression and, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        if (and.Children.Length == 0) return static (_, _, _, _) => true;

        var delegates = new BakedDelegate[and.Children.Length];
        for (int i = 0; i < and.Children.Length; i++)
            delegates[i] = BuildDelegate(and.Children[i], columns, rowidAliasOrdinal);

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

    private static BakedDelegate BuildOr(OrExpression or, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        if (or.Children.Length == 0) return static (_, _, _, _) => false;

        var delegates = new BakedDelegate[or.Children.Length];
        for (int i = 0; i < or.Children.Length; i++)
            delegates[i] = BuildDelegate(or.Children[i], columns, rowidAliasOrdinal);

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

    private static BakedDelegate BuildNot(NotExpression not, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        var inner = BuildDelegate(not.Inner, columns, rowidAliasOrdinal);
        return (payload, serialTypes, offsets, rowId) => !inner(payload, serialTypes, offsets, rowId);
    }

    // ── Shared helper methods used by JitPredicateBuilder closures ──

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
