// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Applies GROUP BY and aggregate functions (COUNT, SUM, AVG, MIN, MAX)
/// to <see cref="QueryValue"/> rows. No boxing for numeric operations.
/// </summary>
internal static class AggregateProcessor
{
    /// <summary>
    /// Groups rows by the specified columns and computes aggregate values.
    /// Returns materialized result rows with the output column layout from the query.
    /// </summary>
    internal static MaterializedResultSet Apply(
        List<QueryValue[]> sourceRows,
        string[] sourceColumnNames,
        IReadOnlyList<AggregateIntent> aggregates,
        IReadOnlyList<string>? groupByColumns,
        IReadOnlyList<string>? outputColumnNames)
    {
        string[] outColumns = outputColumnNames?.ToArray()
            ?? BuildOutputColumnNames(aggregates, groupByColumns);

        int[]? groupOrdinals = null;
        if (groupByColumns is { Count: > 0 })
        {
            groupOrdinals = new int[groupByColumns.Count];
            for (int i = 0; i < groupByColumns.Count; i++)
                groupOrdinals[i] = ResolveOrdinal(sourceColumnNames, groupByColumns[i]);
        }

        int[] aggSourceOrdinals = new int[aggregates.Count];
        for (int i = 0; i < aggregates.Count; i++)
        {
            aggSourceOrdinals[i] = aggregates[i].ColumnName != null
                ? ResolveOrdinal(sourceColumnNames, aggregates[i].ColumnName!)
                : -1; // COUNT(*)
        }

        if (groupOrdinals == null)
        {
            var row = ComputeAggregateRow(sourceRows, aggregates, aggSourceOrdinals, outColumns.Length);
            return new MaterializedResultSet([row], outColumns);
        }

        var groups = GroupRows(sourceRows, groupOrdinals);

        var result = new List<QueryValue[]>(groups.Count);
        foreach (var groupRows in groups.Values)
        {
            var row = new QueryValue[outColumns.Length];

            var firstRow = groupRows[0];
            for (int i = 0; i < outColumns.Length; i++)
            {
                int srcOrdinal = TryResolveOrdinal(sourceColumnNames, outColumns[i]);
                if (srcOrdinal >= 0 && !IsAggregateColumn(i, aggregates))
                    row[i] = firstRow[srcOrdinal];
            }

            foreach (var agg in aggregates)
            {
                int srcOrdinal = agg.ColumnName != null
                    ? ResolveOrdinal(sourceColumnNames, agg.ColumnName)
                    : -1;
                row[agg.OutputOrdinal] = ComputeAggregate(agg.Function, groupRows, srcOrdinal);
            }

            result.Add(row);
        }

        return new MaterializedResultSet(result, outColumns);
    }

    private static QueryValue[] ComputeAggregateRow(
        List<QueryValue[]> rows,
        IReadOnlyList<AggregateIntent> aggregates,
        int[] sourceOrdinals,
        int outputWidth)
    {
        var row = new QueryValue[outputWidth];
        for (int i = 0; i < aggregates.Count; i++)
        {
            row[aggregates[i].OutputOrdinal] = ComputeAggregate(
                aggregates[i].Function, rows, sourceOrdinals[i]);
        }
        return row;
    }

    private static QueryValue ComputeAggregate(
        AggregateFunction func, List<QueryValue[]> rows, int columnOrdinal)
    {
        if (func == AggregateFunction.CountStar) return QueryValue.FromInt64(rows.Count);
        if (func == AggregateFunction.Count) return QueryValue.FromInt64(CountNonNull(rows, columnOrdinal));
        if (func == AggregateFunction.Avg) return QueryValue.FromDouble(AvgColumn(rows, columnOrdinal));
        if (func == AggregateFunction.Min) return MinColumn(rows, columnOrdinal);
        if (func == AggregateFunction.Max) return MaxColumn(rows, columnOrdinal);

        if (func == AggregateFunction.Sum)
        {
            return HasDoubleValues(rows, columnOrdinal)
                ? QueryValue.FromDouble(SumColumnDouble(rows, columnOrdinal))
                : QueryValue.FromInt64(SumColumnInt(rows, columnOrdinal));
        }

        throw new NotSupportedException($"Unsupported aggregate function: {func}");
    }

    private static long CountNonNull(List<QueryValue[]> rows, int ordinal)
    {
        long count = 0;
        foreach (var row in rows)
        {
            if (!row[ordinal].IsNull)
                count++;
        }
        return count;
    }

    private static long SumColumnInt(List<QueryValue[]> rows, int ordinal)
    {
        long sum = 0;
        foreach (var row in rows)
        {
            if (row[ordinal].Type == QueryValueType.Int64)
                sum += row[ordinal].AsInt64();
        }
        return sum;
    }

    private static double SumColumnDouble(List<QueryValue[]> rows, int ordinal)
    {
        double sum = 0;
        foreach (var row in rows)
        {
            ref var val = ref row[ordinal];
            if (val.Type == QueryValueType.Int64)
                sum += val.AsInt64();
            else if (val.Type == QueryValueType.Double)
                sum += val.AsDouble();
        }
        return sum;
    }

    private static bool HasDoubleValues(List<QueryValue[]> rows, int ordinal)
    {
        foreach (var row in rows)
        {
            if (row[ordinal].Type == QueryValueType.Double)
                return true;
        }
        return false;
    }

    private static double AvgColumn(List<QueryValue[]> rows, int ordinal)
    {
        double sum = 0;
        long count = 0;

        foreach (var row in rows)
        {
            ref var val = ref row[ordinal];
            if (val.Type == QueryValueType.Int64) { sum += val.AsInt64(); count++; }
            else if (val.Type == QueryValueType.Double) { sum += val.AsDouble(); count++; }
        }

        return count > 0 ? sum / count : 0.0;
    }

    private static QueryValue MinColumn(List<QueryValue[]> rows, int ordinal)
    {
        QueryValue min = QueryValue.Null;
        bool hasValue = false;
        foreach (var row in rows)
        {
            ref var val = ref row[ordinal];
            if (val.IsNull) continue;
            if (!hasValue || QueryPostProcessor.CompareValues(val, min) < 0)
            {
                min = val;
                hasValue = true;
            }
        }
        return hasValue ? min : QueryValue.Null;
    }

    private static QueryValue MaxColumn(List<QueryValue[]> rows, int ordinal)
    {
        QueryValue max = QueryValue.Null;
        bool hasValue = false;
        foreach (var row in rows)
        {
            ref var val = ref row[ordinal];
            if (val.IsNull) continue;
            if (!hasValue || QueryPostProcessor.CompareValues(val, max) > 0)
            {
                max = val;
                hasValue = true;
            }
        }
        return hasValue ? max : QueryValue.Null;
    }

    private static Dictionary<GroupKey, List<QueryValue[]>> GroupRows(
        List<QueryValue[]> rows, int[] groupOrdinals)
    {
        var comparer = new GroupKeyComparer(groupOrdinals);
        var groups = new Dictionary<GroupKey, List<QueryValue[]>>(comparer);

        foreach (var row in rows)
        {
            var key = new GroupKey(row, groupOrdinals);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(row);
        }

        return groups;
    }

    /// <summary>Wraps a row + group ordinals for structural hashing/equality.</summary>
    private readonly struct GroupKey
    {
        public readonly QueryValue[] Row;
        public readonly int[] Ordinals;
        public GroupKey(QueryValue[] row, int[] ordinals) { Row = row; Ordinals = ordinals; }
    }

    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        private readonly int[] _ordinals;
        public GroupKeyComparer(int[] ordinals) => _ordinals = ordinals;

        public bool Equals(GroupKey x, GroupKey y)
        {
            for (int i = 0; i < _ordinals.Length; i++)
            {
                int ord = _ordinals[i];
                if (QueryPostProcessor.CompareValues(x.Row[ord], y.Row[ord]) != 0)
                    return false;
            }
            return true;
        }

        public int GetHashCode(GroupKey obj)
        {
            var hash = new HashCode();
            foreach (int ord in _ordinals)
            {
                ref var val = ref obj.Row[ord];
                switch (val.Type)
                {
                    case QueryValueType.Null: hash.Add(0); break;
                    case QueryValueType.Int64: hash.Add(val.AsInt64()); break;
                    case QueryValueType.Double: hash.Add(val.AsDouble()); break;
                    case QueryValueType.Text: hash.Add(val.AsString(), StringComparer.Ordinal); break;
                    default: hash.Add(val.ObjectValue); break;
                }
            }
            return hash.ToHashCode();
        }
    }

    private static string[] BuildOutputColumnNames(
        IReadOnlyList<AggregateIntent> aggregates,
        IReadOnlyList<string>? groupByColumns)
    {
        int groupCount = groupByColumns?.Count ?? 0;
        var names = new string[groupCount + aggregates.Count];
        for (int i = 0; i < groupCount; i++)
            names[i] = groupByColumns![i];
        for (int i = 0; i < aggregates.Count; i++)
            names[groupCount + i] = aggregates[i].Alias;
        return names;
    }

    private static bool IsAggregateColumn(int outputOrdinal, IReadOnlyList<AggregateIntent> aggregates)
    {
        foreach (var agg in aggregates)
        {
            if (agg.OutputOrdinal == outputOrdinal)
                return true;
        }
        return false;
    }

    private static int ResolveOrdinal(string[] columnNames, string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new ArgumentException($"Column '{name}' not found in source result set.");
    }

    private static int TryResolveOrdinal(string[] columnNames, string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
