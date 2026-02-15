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
    private readonly string[] _sourceColumnNames;

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
        _sourceColumnNames = sourceColumnNames;
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

        // .NET dict[key] = value for existing keys updates value but keeps stored key,
        // so the stored key (with its own copy) is preserved even when lookupKey
        // references the reusable buffer.
        ref var accRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_groups, lookupKey, out bool exists);

        if (exists)
        {
            // Fast path: update accumulator in-place via ref — no struct copy + write-back
            UpdateAccumulator(ref accRef, row);
        }
        else
        {
            // New group — copy key values for permanent storage in the dictionary.
            // We must fix the stored key since GetValueRefOrAddDefault stored our
            // reusable buffer reference. Remove + re-add with a proper key.
            _groups.Remove(lookupKey);

            var storedValues = new QueryValue[_groupOrdinals.Length];
            _lookupKeyBuffer.AsSpan(0, _groupOrdinals.Length).CopyTo(storedValues);

            accRef = CreateAccumulator(row);
            accRef.GroupKeyValues = storedValues;
            _groups[new GroupKey(storedValues)] = accRef;
        }
    }

    /// <summary>
    /// Finalizes the aggregation and returns materialized result rows.
    /// </summary>
    internal (List<QueryValue[]> rows, string[] columnNames) Finalize()
    {
        if (_groupOrdinals == null)
        {
            var row = BuildOutputRow(ref _singleAccumulator, null);
            return ([row], _outputColumns);
        }

        var result = new List<QueryValue[]>(_groups.Count);
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
            Count = 1,
            PerAgg = new AggState[_aggregates.Count],
        };

        for (int i = 0; i < _aggregates.Count; i++)
        {
            int srcOrd = _aggSourceOrdinals[i];
            ref var state = ref acc.PerAgg[i];

            if (_aggregates[i].Function == AggregateFunction.CountStar)
            {
                state.CountStarOrNonNull = 1;
                continue;
            }

            if (srcOrd < 0) continue;
            ref var val = ref row[srcOrd];

            if (_aggregates[i].Function == AggregateFunction.Count)
            {
                state.CountStarOrNonNull = val.IsNull ? 0 : 1;
                continue;
            }

            if (val.IsNull) continue;

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

            state.Min = val;
            state.Max = val;
            state.HasMinMax = true;
        }

        return acc;
    }

    private void UpdateAccumulator(ref GroupAccumulator acc, QueryValue[] row)
    {
        acc.Count++;

        for (int i = 0; i < _aggregates.Count; i++)
        {
            int srcOrd = _aggSourceOrdinals[i];
            ref var state = ref acc.PerAgg[i];

            if (_aggregates[i].Function == AggregateFunction.CountStar)
            {
                state.CountStarOrNonNull++;
                continue;
            }

            if (srcOrd < 0) continue;
            ref var val = ref row[srcOrd];

            if (_aggregates[i].Function == AggregateFunction.Count)
            {
                if (!val.IsNull) state.CountStarOrNonNull++;
                continue;
            }

            if (val.IsNull) continue;

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
                        // Promote int sum to double
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

            // Min/Max
            if (!state.HasMinMax)
            {
                state.Min = val;
                state.Max = val;
                state.HasMinMax = true;
            }
            else
            {
                if (QueryPostProcessor.CompareValues(val, state.Min) < 0)
                    state.Min = val;
                if (QueryPostProcessor.CompareValues(val, state.Max) > 0)
                    state.Max = val;
            }
        }
    }

    private QueryValue[] BuildOutputRow(ref GroupAccumulator acc, QueryValue[]? groupKeyValues)
    {
        var row = new QueryValue[_outputColumns.Length];

        // Fill group key columns
        if (groupKeyValues != null && _groupOrdinals != null)
        {
            for (int i = 0; i < _outputColumns.Length; i++)
            {
                if (!IsAggregateColumn(i))
                {
                    int srcOrdinal = TryResolveOrdinal(_sourceColumnNames, _outputColumns[i]);
                    if (srcOrdinal >= 0)
                    {
                        // Find which group ordinal matches
                        for (int g = 0; g < _groupOrdinals.Length; g++)
                        {
                            if (_groupOrdinals[g] == srcOrdinal)
                            {
                                row[i] = groupKeyValues[g];
                                break;
                            }
                        }
                    }
                }
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

    private bool IsAggregateColumn(int outputOrdinal)
    {
        foreach (var agg in _aggregates)
        {
            if (agg.OutputOrdinal == outputOrdinal)
                return true;
        }
        return false;
    }

    // ─── Accumulator types ──────────────────────────────────────

    private struct GroupAccumulator
    {
        public long Count;
        public AggState[] PerAgg;
        public QueryValue[]? GroupKeyValues;
    }

    private struct AggState
    {
        public long CountStarOrNonNull;
        public long SumInt;
        public double SumDouble;
        public bool HasDouble;
        public long NumericCount;
        public QueryValue Min;
        public QueryValue Max;
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
        public GroupKey(QueryValue[] values) => Values = values;
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
                if (QueryPostProcessor.CompareValues(x.Values[i], y.Values[i]) != 0)
                    return false;
            }
            return true;
        }

        public int GetHashCode(GroupKey obj)
        {
            var hash = new HashCode();
            var values = obj.Values;
            for (int i = 0; i < values.Length; i++)
            {
                ref var val = ref values[i];
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
