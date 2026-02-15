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
    /// <summary>
    /// Compares two <see cref="QueryValue"/> instances for ordering.
    /// NULLs sort last (positive for a-NULL, negative for b-NULL).
    /// Cross-type int/double comparisons are supported.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    // ─── Row equality ──────────────────────────────────────────────

    /// <summary>
    /// Structural equality comparer for <see cref="QueryValue"/> rows — compares element-by-element.
    /// Shared by DISTINCT, UNION, INTERSECT, EXCEPT, and GROUP BY operations.
    /// No boxing: compares int/double inline.
    /// </summary>
    internal class QvRowEqualityComparer : IEqualityComparer<QueryValue[]>
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
        switch (val.Type)
        {
            case QueryValueType.Null: hash.Add(0); break;
            case QueryValueType.Int64: hash.Add(val.AsInt64()); break;
            case QueryValueType.Double: hash.Add(val.AsDouble()); break;
            case QueryValueType.Text: hash.Add(val.AsString(), StringComparer.Ordinal); break;
            default: hash.Add(val.ObjectValue); break;
        }
    }
}
