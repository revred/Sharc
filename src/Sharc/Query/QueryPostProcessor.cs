// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Applies post-processing to query results: DISTINCT, ORDER BY, LIMIT/OFFSET.
/// Materializes the source reader into unboxed <see cref="QueryValue"/> rows,
/// transforms, and returns a new reader.
/// </summary>
internal static class QueryPostProcessor
{
    /// <summary>
    /// Applies post-processing pipeline if needed. Returns the source reader unchanged
    /// if no post-processing is required.
    /// </summary>
    internal static SharcDataReader Apply(
        SharcDataReader source,
        QueryIntent intent)
    {
        bool needsAggregate = intent.HasAggregates;
        bool needsDistinct = intent.IsDistinct;
        bool needsSort = intent.OrderBy is { Count: > 0 };
        bool needsLimit = intent.Limit.HasValue || intent.Offset.HasValue;

        if (!needsAggregate && !needsDistinct && !needsSort && !needsLimit)
            return source;

        // Streaming top-N: ORDER BY + LIMIT without full materialization
        if (needsSort && needsLimit && !needsAggregate && !needsDistinct
            && intent.Limit.HasValue && !intent.Offset.HasValue)
        {
            return StreamingTopN(source, intent);
        }

        // Streaming aggregate: GROUP BY + aggregates without full materialization
        if (needsAggregate && !needsDistinct)
        {
            return StreamingAggregate(source, intent, needsSort, needsLimit);
        }

        // Materialize all rows from the source reader into unboxed QueryValue arrays
        var (rows, columnNames) = Materialize(source);
        source.Dispose();

        // Pipeline: Aggregate+GroupBy → Distinct → Sort → Limit/Offset (SQL semantics)
        if (needsAggregate)
        {
            (rows, columnNames) = AggregateProcessor.Apply(
                rows, columnNames,
                intent.Aggregates!,
                intent.GroupBy,
                intent.Columns);
        }

        if (needsDistinct)
            rows = ApplyDistinct(rows, columnNames.Length);

        if (needsSort)
            ApplyOrderBy(rows, intent.OrderBy!, columnNames);

        if (needsLimit)
            rows = ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        return new SharcDataReader([.. rows], columnNames);
    }

    // ─── Streaming Top-N ────────────────────────────────────────

    private static SharcDataReader StreamingTopN(SharcDataReader source, QueryIntent intent)
    {
        return ApplyStreamingTopN(source, intent.OrderBy!, intent.Limit!.Value);
    }

    /// <summary>
    /// Applies streaming ORDER BY + LIMIT via a bounded heap with fast rejection.
    /// Reads only ORDER BY columns to decide whether to materialize each row,
    /// avoiding allocation for rows that won't enter the top-N.
    /// </summary>
    internal static SharcDataReader ApplyStreamingTopN(
        SharcDataReader source,
        IReadOnlyList<OrderIntent> orderBy,
        long limitValue)
    {
        int fieldCount = source.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = source.GetColumnName(i);

        // Build the "worst-first" comparison for the heap.
        // For ASC ordering, the worst row is the LARGEST (max-heap evicts max).
        // For DESC ordering, the worst row is the SMALLEST.
        var ordinals = new int[orderBy.Count];
        var descending = new bool[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
        {
            ordinals[i] = ResolveOrdinal(columnNames, orderBy[i].ColumnName);
            descending[i] = orderBy[i].Descending;
        }

        Comparison<QueryValue[]> worstFirst = (a, b) =>
        {
            for (int i = 0; i < ordinals.Length; i++)
            {
                int cmp = CompareValues(a[ordinals[i]], b[ordinals[i]]);
                if (cmp != 0)
                    return descending[i] ? -cmp : cmp;
            }
            return 0;
        };

        int limit = (int)Math.Min(limitValue, int.MaxValue);
        var heap = new TopNHeap(limit, worstFirst);

        while (source.Read())
        {
            // Fast rejection: when heap is full, compare only ORDER BY columns
            // against the root before allocating the full row.
            if (heap.IsFull)
            {
                var root = heap.PeekRoot();
                bool rejected = true;
                for (int i = 0; i < ordinals.Length; i++)
                {
                    var sortVal = MaterializeColumn(source, ordinals[i]);
                    int cmp = CompareValues(sortVal, root[ordinals[i]]);
                    int effectiveCmp = descending[i] ? -cmp : cmp;
                    if (effectiveCmp < 0) { rejected = false; break; } // better than root
                    if (effectiveCmp > 0) break; // worse than root — skip
                    // equal on this column — check next ORDER BY column
                }
                if (rejected) continue; // skip full materialization
            }

            var row = new QueryValue[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                row[i] = MaterializeColumn(source, i);
            heap.TryInsert(row);
        }
        source.Dispose();

        var sorted = heap.ExtractSorted();
        return new SharcDataReader(sorted, columnNames);
    }

    // ─── Streaming Aggregate ─────────────────────────────────────

    private static SharcDataReader StreamingAggregate(
        SharcDataReader source, QueryIntent intent,
        bool needsSort, bool needsLimit)
    {
        int fieldCount = source.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = source.GetColumnName(i);

        var aggregator = new StreamingAggregator(
            columnNames,
            intent.Aggregates!,
            intent.GroupBy,
            intent.Columns);

        // Reuse a single buffer — StreamingAggregator copies all values it needs
        // (group key values on new groups, numeric accumulators by value).
        var buffer = new QueryValue[fieldCount];
        while (source.Read())
        {
            for (int i = 0; i < fieldCount; i++)
                buffer[i] = MaterializeColumn(source, i);
            aggregator.AccumulateRow(buffer);
        }
        source.Dispose();

        var (rows, outColumnNames) = aggregator.Finalize();

        if (needsSort)
            ApplyOrderBy(rows, intent.OrderBy!, outColumnNames);

        if (needsLimit)
            rows = ApplyLimitOffset(rows, intent.Limit, intent.Offset);

        return new SharcDataReader([.. rows], outColumnNames);
    }

    // ─── Materialization ──────────────────────────────────────────

    internal static (List<QueryValue[]> rows, string[] columnNames) Materialize(SharcDataReader reader)
    {
        int fieldCount = reader.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            columnNames[i] = reader.GetColumnName(i);

        var rows = new List<QueryValue[]>();
        while (reader.Read())
        {
            var row = new QueryValue[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                row[i] = MaterializeColumn(reader, i);
            rows.Add(row);
        }

        return (rows, columnNames);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QueryValue MaterializeColumn(SharcDataReader reader, int ordinal)
    {
        var type = reader.GetColumnType(ordinal);
        return type switch
        {
            SharcColumnType.Integral => QueryValue.FromInt64(reader.GetInt64(ordinal)),
            SharcColumnType.Real => QueryValue.FromDouble(reader.GetDouble(ordinal)),
            SharcColumnType.Text => QueryValue.FromString(reader.GetString(ordinal)),
            SharcColumnType.Blob => QueryValue.FromBlob(reader.GetBlob(ordinal)),
            _ => QueryValue.Null,
        };
    }

    // ─── DISTINCT ─────────────────────────────────────────────────

    internal static List<QueryValue[]> ApplyDistinct(List<QueryValue[]> rows, int columnCount)
    {
        var comparer = new QvRowEqualityComparer(columnCount);
        var seen = new HashSet<QueryValue[]>(comparer);
        var result = new List<QueryValue[]>(rows.Count);

        foreach (var row in rows)
        {
            if (seen.Add(row))
                result.Add(row);
        }

        return result;
    }

    // ─── ORDER BY ─────────────────────────────────────────────────

    internal static void ApplyOrderBy(
        List<QueryValue[]> rows,
        IReadOnlyList<OrderIntent> orderBy,
        string[] columnNames)
    {
        var ordinals = new int[orderBy.Count];
        var descending = new bool[orderBy.Count];

        for (int i = 0; i < orderBy.Count; i++)
        {
            ordinals[i] = ResolveOrdinal(columnNames, orderBy[i].ColumnName);
            descending[i] = orderBy[i].Descending;
        }

        // Span.Sort with struct comparer: JIT specializes the generic,
        // eliminating delegate allocation and enabling inlining.
        var comparer = new RowComparer(ordinals, descending);
        CollectionsMarshal.AsSpan(rows).Sort(comparer);
    }

    /// <summary>
    /// Struct-based row comparer for ORDER BY sorting. When used with
    /// <c>Span&lt;T&gt;.Sort&lt;TComparer&gt;</c>, the JIT specializes the generic
    /// method for this value type — no delegate, no boxing, enables inlining.
    /// </summary>
    internal readonly struct RowComparer : IComparer<QueryValue[]>
    {
        private readonly int[] _ordinals;
        private readonly bool[] _descending;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal RowComparer(int[] ordinals, bool[] descending)
        {
            _ordinals = ordinals;
            _descending = descending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(QueryValue[]? a, QueryValue[]? b)
        {
            if (a is null || b is null) return 0;
            for (int i = 0; i < _ordinals.Length; i++)
            {
                int cmp = CompareValues(a[_ordinals[i]], b[_ordinals[i]]);
                if (cmp != 0)
                    return _descending[i] ? -cmp : cmp;
            }
            return 0;
        }
    }

    internal static int ResolveOrdinal(string[] columnNames, string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new ArgumentException($"ORDER BY column '{name}' not found in result set.");
    }

    internal static int CompareValues(QueryValue a, QueryValue b)
    {
        bool aNull = a.IsNull;
        bool bNull = b.IsNull;

        if (aNull && bNull) return 0;
        if (aNull) return 1;  // NULLs sort last by default
        if (bNull) return -1;

        return (a.Type, b.Type) switch
        {
            (QueryValueType.Int64, QueryValueType.Int64) => a.AsInt64().CompareTo(b.AsInt64()),
            (QueryValueType.Double, QueryValueType.Double) => a.AsDouble().CompareTo(b.AsDouble()),
            (QueryValueType.Text, QueryValueType.Text) => string.Compare(a.AsString(), b.AsString(), StringComparison.Ordinal),
            (QueryValueType.Int64, QueryValueType.Double) => ((double)a.AsInt64()).CompareTo(b.AsDouble()),
            (QueryValueType.Double, QueryValueType.Int64) => a.AsDouble().CompareTo((double)b.AsInt64()),
            _ => 0
        };
    }

    // ─── LIMIT / OFFSET ──────────────────────────────────────────

    internal static List<QueryValue[]> ApplyLimitOffset(
        List<QueryValue[]> rows,
        long? limit,
        long? offset)
    {
        int start = offset.HasValue ? (int)Math.Min(offset.Value, rows.Count) : 0;
        int remaining = rows.Count - start;
        int count = limit.HasValue ? (int)Math.Min(limit.Value, remaining) : remaining;

        if (start == 0 && count == rows.Count)
            return rows;

        return rows.GetRange(start, Math.Max(0, count));
    }

    // ─── QueryValue row equality ─────────────────────────────────

    /// <summary>
    /// Structural equality comparer for <see cref="QueryValue"/> rows — compares element-by-element.
    /// Shared by DISTINCT, UNION, INTERSECT, and EXCEPT operations.
    /// No boxing: compares int/double inline.
    /// </summary>
    internal sealed class QvRowEqualityComparer : IEqualityComparer<QueryValue[]>
    {
        private readonly int _columnCount;

        internal QvRowEqualityComparer(int columnCount) => _columnCount = columnCount;

        public bool Equals(QueryValue[]? x, QueryValue[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            for (int i = 0; i < _columnCount; i++)
            {
                if (!ValuesEqual(x[i], y[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(QueryValue[] obj)
        {
            var hash = new HashCode();
            for (int i = 0; i < _columnCount; i++)
            {
                ref var val = ref obj[i];
                switch (val.Type)
                {
                    case QueryValueType.Null:
                        hash.Add(0);
                        break;
                    case QueryValueType.Int64:
                        hash.Add(val.AsInt64());
                        break;
                    case QueryValueType.Double:
                        hash.Add(val.AsDouble());
                        break;
                    case QueryValueType.Text:
                        hash.Add(val.AsString(), StringComparer.Ordinal);
                        break;
                    default:
                        hash.Add(val.ObjectValue);
                        break;
                }
            }
            return hash.ToHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ValuesEqual(QueryValue a, QueryValue b)
        {
            if (a.Type != b.Type)
            {
                // Cross-type comparison: int vs double
                if (a.Type == QueryValueType.Int64 && b.Type == QueryValueType.Double)
                    return (double)a.AsInt64() == b.AsDouble();
                if (a.Type == QueryValueType.Double && b.Type == QueryValueType.Int64)
                    return a.AsDouble() == (double)b.AsInt64();
                // null vs non-null
                if (a.IsNull && b.IsNull) return true;
                return false;
            }

            return a.Type switch
            {
                QueryValueType.Null => true,
                QueryValueType.Int64 => a.AsInt64() == b.AsInt64(),
                QueryValueType.Double => a.AsDouble() == b.AsDouble(),
                QueryValueType.Text => string.Equals(a.AsString(), b.AsString(), StringComparison.Ordinal),
                _ => Equals(a.ObjectValue, b.ObjectValue),
            };
        }
    }

    // ─── Legacy object?[] support (kept for backward compatibility) ─

    /// <summary>
    /// Structural equality comparer for legacy <c>object?[]</c> rows.
    /// Used by callers that haven't migrated to <see cref="QueryValue"/>.
    /// </summary>
    internal sealed class RowEqualityComparer : IEqualityComparer<object?[]>
    {
        private readonly int _columnCount;

        internal RowEqualityComparer(int columnCount) => _columnCount = columnCount;

        public bool Equals(object?[]? x, object?[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            for (int i = 0; i < _columnCount; i++)
            {
                if (!ValuesEqual(x[i], y[i]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(object?[] obj)
        {
            var hash = new HashCode();
            for (int i = 0; i < _columnCount; i++)
            {
                var val = obj[i];
                if (val is DBNull or null)
                    hash.Add(0);
                else
                    hash.Add(val);
            }
            return hash.ToHashCode();
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            bool aNull = a is null or DBNull;
            bool bNull = b is null or DBNull;
            if (aNull && bNull) return true;
            if (aNull || bNull) return false;

            return (a, b) switch
            {
                (long la, long lb) => la == lb,
                (double da, double db) => da == db,
                (string sa, string sb) => string.Equals(sa, sb, StringComparison.Ordinal),
                (long la, double db) => (double)la == db,
                (double da, long lb) => da == (double)lb,
                _ => Equals(a, b),
            };
        }
    }

    // ─── Aggregate column projection ──────────────────────────────

    /// <summary>
    /// Computes the minimal column set needed for aggregate queries:
    /// GROUP BY columns + aggregate source columns. Returns null if
    /// no specific projection is needed (e.g. COUNT(*) only).
    /// </summary>
    internal static string[]? ComputeAggregateProjection(QueryIntent intent)
    {
        if (!intent.HasAggregates) return null;

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (intent.GroupBy is { Count: > 0 })
        {
            foreach (var col in intent.GroupBy)
                needed.Add(col);
        }

        foreach (var agg in intent.Aggregates!)
        {
            if (agg.ColumnName != null)
                needed.Add(agg.ColumnName);
        }

        return needed.Count > 0 ? [.. needed] : null;
    }
}
