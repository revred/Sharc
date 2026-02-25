// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Query;

/// <summary>
/// Shared primitive operations on <see cref="QueryValue"/> rows.
/// Consolidates comparison, ordinal resolution, and materialization logic
/// used across the query pipeline (post-processor, aggregator, set operations).
/// </summary>
internal static class QueryValueOps
{
    private static readonly StringComparer OrdinalComparer = StringComparer.Ordinal;

    /// <summary>
    /// Compares two <see cref="QueryValue"/> instances for ordering.
    /// NULLs sort last (positive for a-NULL, negative for b-NULL).
    /// Cross-type int/double comparisons are supported.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CompareValues(QueryValue a, QueryValue b)
    {
        var aType = a.Type;
        var bType = b.Type;

        if (aType == QueryValueType.Null)
            return bType == QueryValueType.Null ? 0 : 1; // NULLs sort last by default
        if (bType == QueryValueType.Null)
            return -1;

        if (aType == bType)
        {
            return aType switch
            {
                QueryValueType.Int64 => a.AsInt64().CompareTo(b.AsInt64()),
                QueryValueType.Double => a.AsDouble().CompareTo(b.AsDouble()),
                QueryValueType.Text => string.Compare(a.AsString(), b.AsString(), StringComparison.Ordinal),
                _ => 0,
            };
        }

        if (aType == QueryValueType.Int64 && bType == QueryValueType.Double)
            return ((double)a.AsInt64()).CompareTo(b.AsDouble());
        if (aType == QueryValueType.Double && bType == QueryValueType.Int64)
            return a.AsDouble().CompareTo((double)b.AsInt64());

        return 0;
    }

    /// <summary>
    /// Resolves a column name to its ordinal position (case-insensitive).
    /// Throws <see cref="ArgumentException"/> if not found.
    /// </summary>
    internal static int ResolveOrdinal(string[] columnNames, string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new ArgumentException($"Column '{name}' not found in result set.");
    }

    /// <summary>
    /// Tries to resolve a column name to its ordinal position (case-insensitive).
    /// Returns -1 if not found.
    /// </summary>
    internal static int TryResolveOrdinal(string[] columnNames, string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Reads a single column from a <see cref="SharcDataReader"/> into an unboxed <see cref="QueryValue"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static QueryValue MaterializeColumn(SharcDataReader reader, int ordinal)
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

    /// <summary>
    /// Fast value equality used by row/group comparers.
    /// Cross-type numeric equality (int vs double) matches SQL comparison behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ValuesEqual(QueryValue a, QueryValue b)
    {
        if (a.Type == b.Type)
        {
            return a.Type switch
            {
                QueryValueType.Null => true,
                QueryValueType.Int64 => a.AsInt64() == b.AsInt64(),
                QueryValueType.Double => a.AsDouble() == b.AsDouble(),
                QueryValueType.Text => string.Equals(a.AsString(), b.AsString(), StringComparison.Ordinal),
                _ => Equals(a.ObjectValue, b.ObjectValue),
            };
        }

        if (a.Type == QueryValueType.Int64 && b.Type == QueryValueType.Double)
            return (double)a.AsInt64() == b.AsDouble();
        if (a.Type == QueryValueType.Double && b.Type == QueryValueType.Int64)
            return a.AsDouble() == (double)b.AsInt64();

        return a.IsNull && b.IsNull;
    }

    /// <summary>
    /// Stable hash for a single value, consistent with <see cref="ValuesEqual"/>.
    /// Numeric values normalize through <c>double</c> so int/double equivalents hash identically.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetValueHashCode(in QueryValue value)
    {
        return value.Type switch
        {
            QueryValueType.Null => 0,
            QueryValueType.Int64 => BitConverter.DoubleToInt64Bits((double)value.AsInt64()).GetHashCode(),
            QueryValueType.Double => BitConverter.DoubleToInt64Bits(value.AsDouble()).GetHashCode(),
            QueryValueType.Text => OrdinalComparer.GetHashCode(value.AsString()),
            _ => value.ObjectValue?.GetHashCode() ?? 0,
        };
    }

    // ─── Row equality ──────────────────────────────────────────────

    /// <summary>
    /// Structural equality comparer for <see cref="QueryValue"/> rows — compares element-by-element.
    /// Shared by DISTINCT, UNION, INTERSECT, EXCEPT, and GROUP BY operations.
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
            int hash = 17;
            for (int i = 0; i < _columnCount; i++)
            {
                hash = unchecked((hash * 31) + GetValueHashCode(in obj[i]));
            }
            return hash;
        }
    }

    // ─── Row ordering ──────────────────────────────────────────────

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

    // ─── QueryValue hash helper ────────────────────────────────────

    /// <summary>
    /// Adds a <see cref="QueryValue"/> to a <see cref="HashCode"/> accumulator.
    /// Shared by all group-key comparers to ensure consistent hashing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddToHash(ref HashCode hash, ref QueryValue val)
    {
        hash.Add(GetValueHashCode(in val));
    }
}
