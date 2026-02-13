/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Sharc.Core.Primitives;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// Compiles IFilterStar expressions into JIT-optimized delegates using Expression Trees.
/// </summary>
internal static class FilterStarCompiler
{
    public static BakedDelegate Compile(IFilterStar expression, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal)
    {
        var payloadParam = Expression.Parameter(typeof(ReadOnlySpan<byte>), "payload");
        var serialTypesParam = Expression.Parameter(typeof(ReadOnlySpan<long>), "serialTypes");
        var offsetsParam = Expression.Parameter(typeof(ReadOnlySpan<int>), "offsets");
        var rowIdParam = Expression.Parameter(typeof(long), "rowId");

        var body = BuildExpression(expression, columns, rowidAliasOrdinal, 
                                   payloadParam, serialTypesParam, offsetsParam, rowIdParam);

        return Expression.Lambda<BakedDelegate>(body, payloadParam, serialTypesParam, offsetsParam, rowIdParam).Compile();
    }

    private static Expression BuildExpression(IFilterStar expr, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal,
                                             ParameterExpression payload, ParameterExpression serialTypes,
                                             ParameterExpression offsets, ParameterExpression rowId)
    {
        return expr switch
        {
            AndExpression and => BuildAnd(and, columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId),
            OrExpression or => BuildOr(or, columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId),
            NotExpression not => Expression.Not(BuildExpression(not.Inner, columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId)),
            PredicateExpression pred => JitPredicateBuilder.Build(pred, columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId),
            _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}")
        };
    }

    private static Expression BuildAnd(AndExpression and, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal,
                                      ParameterExpression payload, ParameterExpression serialTypes,
                                      ParameterExpression offsets, ParameterExpression rowId)
    {
        if (and.Children.Length == 0) return Expression.Constant(true);
        var result = BuildExpression(and.Children[0], columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId);
        for (int i = 1; i < and.Children.Length; i++)
            result = Expression.AndAlso(result, BuildExpression(and.Children[i], columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId));
        return result;
    }

    private static Expression BuildOr(OrExpression or, IReadOnlyList<ColumnInfo> columns, int rowidAliasOrdinal,
                                     ParameterExpression payload, ParameterExpression serialTypes,
                                     ParameterExpression offsets, ParameterExpression rowId)
    {
        if (or.Children.Length == 0) return Expression.Constant(false);
        var result = BuildExpression(or.Children[0], columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId);
        for (int i = 1; i < or.Children.Length; i++)
            result = Expression.OrElse(result, BuildExpression(or.Children[i], columns, rowidAliasOrdinal, payload, serialTypes, offsets, rowId));
        return result;
    }

    public static long GetSerialType(ReadOnlySpan<long> serialTypes, int index)
    {
        uint idx = (uint)index;
        return idx < (uint)serialTypes.Length ? serialTypes[index] : 0L;
    }

    public static int GetOffset(ReadOnlySpan<int> offsets, int index)
    {
        uint idx = (uint)index;
        return idx < (uint)offsets.Length ? offsets[index] : 0;
    }

    public static ReadOnlySpan<byte> GetSlice(ReadOnlySpan<byte> payload, int offset, int length)
    {
        uint uOffset = (uint)offset;
        uint uLength = (uint)length;
        if (uOffset + uLength > (uint)payload.Length) return [];
        return payload.Slice(offset, length);
    }

    public static bool Utf8SetContains(ReadOnlySpan<byte> data, HashSet<string> set)
    {
        if (data.IsEmpty || set.Count == 0) return false;
        return set.Contains(Encoding.UTF8.GetString(data));
    }
}
