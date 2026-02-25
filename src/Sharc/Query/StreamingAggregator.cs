// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Streaming hash aggregator that processes rows one at a time, maintaining
/// per-group accumulators. Uses O(G) memory where G = number of groups,
/// instead of O(N) for the full-materialization path.
/// Supports buffer reuse: callers may pass the same QueryValue[] each call.
/// </summary>
internal sealed class StreamingAggregator
{
    private readonly IReadOnlyList<AggregateIntent> _aggregates;
    private readonly int[] _aggSourceOrdinals;
    private readonly int[]? _groupOrdinals;
    private readonly string[] _outputColumns;

    // Precomputed output column mapping: for each output ordinal, either:
    //   groupKeyIndex >= 0: copy from groupKeyValues[groupKeyIndex]
    //   groupKeyIndex == -1: this is an aggregate column (filled separately)
    private readonly int[] _outputGroupKeyMap;

    // Per-group accumulators keyed by extracted group-key values (not full rows).
    // GroupKey stores its own copy of key values, enabling row buffer reuse.
    private readonly Dictionary<GroupKey, GroupAccumulator> _groups;

    // Reusable lookup buffer for group key extraction — avoids allocating
    // a new QueryValue[] per row just for dictionary lookup.
    private readonly QueryValue[]? _lookupKeyBuffer;

    // No GROUP BY → single accumulator
    private GroupAccumulator _singleAccumulator;
    private bool _hasSingleAccumulator;

    internal StreamingAggregator(
        string[] sourceColumnNames,
        IReadOnlyList<AggregateIntent> aggregates,
        IReadOnlyList<string>? groupByColumns,
        IReadOnlyList<string>? outputColumnNames)
    {
        _aggregates = aggregates;
        _outputColumns = outputColumnNames?.ToArray()
            ?? BuildOutputColumnNames(aggregates, groupByColumns);

        // Resolve group ordinals
        if (groupByColumns is { Count: > 0 })
        {
            _groupOrdinals = new int[groupByColumns.Count];
            for (int i = 0; i < groupByColumns.Count; i++)
                _groupOrdinals[i] = ResolveOrdinal(sourceColumnNames, groupByColumns[i]);

            _lookupKeyBuffer = new QueryValue[groupByColumns.Count];
        }

        // Resolve aggregate source ordinals
        _aggSourceOrdinals = new int[aggregates.Count];
        for (int i = 0; i < aggregates.Count; i++)
        {
            _aggSourceOrdinals[i] = aggregates[i].ColumnName != null
                ? ResolveOrdinal(sourceColumnNames, aggregates[i].ColumnName!)
                : -1; // COUNT(*)
        }

        _groups = new Dictionary<GroupKey, GroupAccumulator>(GroupKeyComparer.Instance);

        // Precompute output column → group key index map
        _outputGroupKeyMap = new int[_outputColumns.Length];
        for (int i = 0; i < _outputColumns.Length; i++)
        {
            _outputGroupKeyMap[i] = -1; // default: aggregate column
            if (_groupOrdinals == null) continue;

            bool isAgg = false;
            foreach (var agg in aggregates)
                if (agg.OutputOrdinal == i) { isAgg = true; break; }

            if (!isAgg)
            {
                int srcOrd = TryResolveOrdinal(sourceColumnNames, _outputColumns[i]);
                if (srcOrd >= 0)
                {
                    for (int g = 0; g < _groupOrdinals.Length; g++)
                    {
                        if (_groupOrdinals[g] == srcOrd) { _outputGroupKeyMap[i] = g; break; }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a single row, updating the appropriate group accumulator.
    /// The row array may be reused between calls — all needed values are
    /// copied by value into accumulators or stored group keys.
    /// </summary>
    internal void AccumulateRow(QueryValue[] row)
    {
        if (_groupOrdinals == null)
        {
            // No GROUP BY — single accumulator
            if (!_hasSingleAccumulator)
            {
                _singleAccumulator = CreateAccumulator(row);
                _hasSingleAccumulator = true;
            }
            else
            {
                UpdateAccumulator(ref _singleAccumulator, row);
            }
            return;
        }

        // Extract group key values into reusable lookup buffer
        for (int i = 0; i < _groupOrdinals.Length; i++)
            _lookupKeyBuffer![i] = row[_groupOrdinals[i]];

        var lookupKey = new GroupKey(_lookupKeyBuffer!);

        // Fast path for existing groups: direct by-ref update, no struct copy.
        ref var accRef = ref CollectionsMarshal.GetValueRefOrNullRef(_groups, lookupKey);
        if (!Unsafe.IsNullRef(ref accRef))
        {
            UpdateAccumulator(ref accRef, row);
            return;
        }

        // New group: copy key values once for stable dictionary ownership.
        var storedValues = new QueryValue[_groupOrdinals.Length];
        _lookupKeyBuffer.AsSpan(0, _groupOrdinals.Length).CopyTo(storedValues);

        var acc = CreateAccumulator(row);
        acc.GroupKeyValues = storedValues;
        _groups.Add(new GroupKey(storedValues, lookupKey.HashCode), acc);
    }

    /// <summary>
    /// Finalizes the aggregation and returns materialized result rows.
    /// </summary>
    internal (RowSet rows, string[] columnNames) Finalize()
    {
        if (_groupOrdinals == null)
        {
            var row = BuildOutputRow(ref _singleAccumulator, null);
            return ([row], _outputColumns);
        }

        var result = new RowSet(_groups.Count);
        foreach (var (_, acc) in _groups)
        {
            var accCopy = acc; // struct copy for ref
            var row = BuildOutputRow(ref accCopy, acc.GroupKeyValues);
            result.Add(row);
        }

        return (result, _outputColumns);
    }

    private GroupAccumulator CreateAccumulator(QueryValue[] row)
    {
        var acc = new GroupAccumulator
        {
            PerAgg = new AggState[_aggregates.Count],
        };

        for (int i = 0; i < _aggregates.Count; i++)
        {
            var function = _aggregates[i].Function;
            int srcOrd = _aggSourceOrdinals[i];
            ref var state = ref acc.PerAgg[i];

            if (function == AggregateFunction.CountStar)
            {
                state.CountStarOrNonNull = 1;
                continue;
            }

            if (srcOrd < 0) continue;
            ref var val = ref row[srcOrd];

            if (function == AggregateFunction.Count)
            {
                state.CountStarOrNonNull = val.IsNull ? 0 : 1;
                continue;
            }

            if (val.IsNull) continue;

            if (function is AggregateFunction.Sum or AggregateFunction.Avg)
            {
                switch (val.Type)
                {
                    case QueryValueType.Int64:
                        state.SumInt = val.AsInt64();
                        state.NumericCount = 1;
                        break;
                    case QueryValueType.Double:
                        state.SumDouble = val.AsDouble();
                        state.HasDouble = true;
                        state.NumericCount = 1;
                        break;
                }
            }
            else if (function is AggregateFunction.Min or AggregateFunction.Max)
            {
                state.Min = val;
                state.Max = val;
                state.HasMinMax = true;
            }
        }

        return acc;
    }

    private void UpdateAccumulator(ref GroupAccumulator acc, QueryValue[] row)
    {
        for (int i = 0; i < _aggregates.Count; i++)
        {
            var function = _aggregates[i].Function;
            int srcOrd = _aggSourceOrdinals[i];
            ref var state = ref acc.PerAgg[i];

            if (function == AggregateFunction.CountStar)
            {
                state.CountStarOrNonNull++;
                continue;
            }

            if (srcOrd < 0) continue;
            ref var val = ref row[srcOrd];

            if (function == AggregateFunction.Count)
            {
                if (!val.IsNull) state.CountStarOrNonNull++;
                continue;
            }

            if (val.IsNull) continue;

            if (function is AggregateFunction.Sum or AggregateFunction.Avg)
            {
                switch (val.Type)
                {
                    case QueryValueType.Int64:
                        long lv = val.AsInt64();
                        if (state.HasDouble)
                            state.SumDouble += lv;
                        else
                            state.SumInt += lv;
                        state.NumericCount++;
                        break;
                    case QueryValueType.Double:
                        double dv = val.AsDouble();
                        if (!state.HasDouble)
                        {
                            // Promote int sum to double.
                            state.SumDouble = state.SumInt + dv;
                            state.HasDouble = true;
                        }
                        else
                        {
                            state.SumDouble += dv;
                        }
                        state.NumericCount++;
                        break;
                }
                continue;
            }

            if (function == AggregateFunction.Min)
            {
                if (!state.HasMinMax || QueryValueOps.CompareValues(val, state.Min) < 0)
                    state.Min = val;
                state.HasMinMax = true;
            }
            else if (function == AggregateFunction.Max)
            {
                if (!state.HasMinMax || QueryValueOps.CompareValues(val, state.Max) > 0)
                    state.Max = val;
                state.HasMinMax = true;
            }
        }
    }

    private QueryValue[] BuildOutputRow(ref GroupAccumulator acc, QueryValue[]? groupKeyValues)
    {
        var row = new QueryValue[_outputColumns.Length];

        // Fill group key columns via precomputed map
        if (groupKeyValues != null)
        {
            for (int i = 0; i < _outputGroupKeyMap.Length; i++)
            {
                if (_outputGroupKeyMap[i] >= 0)
                    row[i] = groupKeyValues[_outputGroupKeyMap[i]];
            }
        }

        // Fill aggregate values
        for (int i = 0; i < _aggregates.Count; i++)
        {
            ref var state = ref acc.PerAgg[i];
            row[_aggregates[i].OutputOrdinal] = _aggregates[i].Function switch
            {
                AggregateFunction.CountStar => QueryValue.FromInt64(state.CountStarOrNonNull),
                AggregateFunction.Count => QueryValue.FromInt64(state.CountStarOrNonNull),
                AggregateFunction.Sum => state.HasDouble
                    ? QueryValue.FromDouble(state.SumDouble)
                    : QueryValue.FromInt64(state.SumInt),
                AggregateFunction.Avg => state.NumericCount > 0
                    ? QueryValue.FromDouble(
                        state.HasDouble
                            ? state.SumDouble / state.NumericCount
                            : (double)state.SumInt / state.NumericCount)
                    : QueryValue.FromDouble(0.0),
                AggregateFunction.Min => state.HasMinMax ? state.Min : QueryValue.Null,
                AggregateFunction.Max => state.HasMinMax ? state.Max : QueryValue.Null,
                _ => QueryValue.Null,
            };
        }

        return row;
    }

    // ─── Accumulator types ──────────────────────────────────────

    private struct GroupAccumulator
    {
        public AggState[] PerAgg;
        public QueryValue[]? GroupKeyValues;
    }

    private struct AggState
    {
        public long CountStarOrNonNull;
        public long SumInt;
        public double SumDouble;
        public long NumericCount;
        public QueryValue Min;
        public QueryValue Max;
        public bool HasDouble;
        public bool HasMinMax;
    }

    // ─── Group key ──────────────────────────────────────────────

    /// <summary>
    /// Group key that stores extracted column values directly (not a reference
    /// to the full row). This enables row buffer reuse since the key owns its data.
    /// </summary>
    private readonly struct GroupKey
    {
        public readonly QueryValue[] Values;
        public readonly int HashCode;

        public GroupKey(QueryValue[] values)
        {
            Values = values;
            HashCode = ComputeHash(values);
        }

        public GroupKey(QueryValue[] values, int hashCode)
        {
            Values = values;
            HashCode = hashCode;
        }

        private static int ComputeHash(QueryValue[] values)
        {
            int hash = 17;
            for (int i = 0; i < values.Length; i++)
                hash = unchecked((hash * 31) + QueryValueOps.GetValueHashCode(in values[i]));
            return hash;
        }
    }

    /// <summary>
    /// Singleton comparer that compares GroupKey values element-by-element.
    /// Since GroupKey.Values contains only the group-by columns, indices
    /// are 0..N (no ordinal indirection needed).
    /// </summary>
    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        public static readonly GroupKeyComparer Instance = new();

        public bool Equals(GroupKey x, GroupKey y)
        {
            if (x.Values.Length != y.Values.Length) return false;
            for (int i = 0; i < x.Values.Length; i++)
            {
                if (!QueryValueOps.ValuesEqual(x.Values[i], y.Values[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(GroupKey obj) => obj.HashCode;
    }

    // ─── Helpers ────────────────────────────────────────────────

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
