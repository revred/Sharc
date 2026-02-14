// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;

namespace Sharc;

/// <summary>
/// A filter with its column ordinal pre-resolved for efficient per-row evaluation.
/// </summary>
internal readonly struct ResolvedFilter
{
    public required int ColumnOrdinal { get; init; }
    public required SharcOperator Operator { get; init; }
    public required object? Value { get; init; }
}

/// <summary>
/// Evaluates filter conditions against decoded column values.
/// All comparisons follow SQLite affinity and NULL semantics.
/// </summary>
internal static class FilterEvaluator
{
    /// <summary>
    /// Evaluates all filters against the current row (AND semantics).
    /// Returns true only if every filter matches.
    /// </summary>
    public static bool MatchesAll(ResolvedFilter[] filters, ColumnValue[] row)
    {
        for (int i = 0; i < filters.Length; i++)
        {
            ref readonly var f = ref filters[i];
            if (!Matches(row[f.ColumnOrdinal], f.Operator, f.Value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Evaluates a single filter condition against a column value.
    /// </summary>
    internal static bool Matches(ColumnValue column, SharcOperator op, object? filterValue)
    {
        // DECISION: SQLite NULL semantics â€” NULL never matches any comparison.
        // To find NULLs, callers would need a dedicated IsNull filter (future enhancement).
        if (column.IsNull)
            return false;

        if (filterValue is null)
            return false;

        int? cmp = CompareColumnValue(column, filterValue);
        if (cmp is null)
            return false;

        return op switch
        {
            SharcOperator.Equal => cmp == 0,
            SharcOperator.NotEqual => cmp != 0,
            SharcOperator.LessThan => cmp < 0,
            SharcOperator.GreaterThan => cmp > 0,
            SharcOperator.LessOrEqual => cmp <= 0,
            SharcOperator.GreaterOrEqual => cmp >= 0,
            _ => false
        };
    }

    /// <summary>
    /// Zero-allocation filter evaluation against raw record bytes.
    /// Parses serial types on the stack and compares spans directly.
    /// </summary>
    public static bool MatchesRaw(ReadOnlySpan<byte> payload, ResolvedFilter[] filters, Sharc.Core.Records.RecordDecoder decoder)
    {
        // 1. Parse header to get serial types (stackalloc to avoid array allocation)
        Span<long> serialTypes = stackalloc long[64];
        int colCount = decoder.ReadSerialTypes(payload, serialTypes, out int bodyOffset);

        // 2. Evaluate each filter
        for (int i = 0; i < filters.Length; i++)
        {
            ref readonly var f = ref filters[i];
            
            // If filter column is out of range, treat as NULL
            if (f.ColumnOrdinal >= colCount)
            {
                if (!MatchesNull(f.Operator)) return false;
                continue;
            }

            long st = serialTypes[f.ColumnOrdinal];

            // 3. Locate column data
            int currentOffset = bodyOffset;
            for (int c = 0; c < f.ColumnOrdinal; c++)
            {
                currentOffset += Sharc.Core.Primitives.SerialTypeCodec.GetContentSize(serialTypes[c]);
            }
            int contentSize = Sharc.Core.Primitives.SerialTypeCodec.GetContentSize(st);
            var data = payload.Slice(currentOffset, contentSize);

            // 4. Compare raw bytes
            if (!MatchesRawValue(data, st, f.Operator, f.Value))
                return false;
        }

        return true;
    }

    private static bool MatchesNull(SharcOperator op)
    {
        // SQLite semantics: NULL > value is unknown (false), NULL < value is unknown (false)
        // NULL == NULL is false (use IS NULL operator in SQL, but SharcOperator is simple)
        // For now, consistent with Matches(): NULL never matches anything.
        return false;
    }

    private static bool MatchesRawValue(ReadOnlySpan<byte> data, long st, SharcOperator op, object? filterValue)
    {
        // Null column check
        if (st == 0) return false; // MatchesNull(op) - effectively always false for standard ops
        if (filterValue is null) return false;

        int cmp = 0;

        // Determine column type from serial type
        if (IsIntegral(st))
        {
            long val = DecodeInt(data, st);
            switch (filterValue)
            {
                case long l: cmp = val.CompareTo(l); break;
                case int i: cmp = val.CompareTo((long)i); break;
                case double d: cmp = ((double)val).CompareTo(d); break;
                default: return false; // Incompatible type
            }
        }
        else if (IsReal(st))
        {
            double val = System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(data);
            switch (filterValue)
            {
                case double d: cmp = val.CompareTo(d); break;
                case float f: cmp = val.CompareTo((double)f); break;
                case long l: cmp = val.CompareTo((double)l); break;
                case int i: cmp = val.CompareTo((double)i); break;
                default: return false;
            }
        }
        else if (IsText(st))
        {
            // Avoid string allocation: compare UTF-8 span if possible?
            // For now, to be safe and simple without custom Utf8Comparer, we might strictly need decoding.
            // BUT wait! We want ZERO allocation.
            // Converting the filter values to UTF-8 bytes *once* in ResolvedFilter would be best.
            // For now, let's decode to string (stackalloc char?) or just accept string alloc ONLY for matching rows?
            // NO, we want zero alloc for SKIPPED rows.
            
            // Optimization: If filter value is string, get bytes. 
            // NOTE: This assumes ResolvedFilter could hold bytes, but it holds object.
            // We'll incur a check overhead here. 
            if (filterValue is string s)
            {
                // Slow path: string-to-string comparison
                // Decode from valid UTF-8 span
                // We'll use a temporary string for now, BUT we can optimize further by updating ResolvedFilter later.
                // Reverting to string alloc here would defeat the purpose for TEXT columns.
                // However, INTEGER/REAL columns are zero-alloc now.
                
                // TODO: Optimize text comparison. For now, fallback to string creation ONLY on text columns.
                // Most benchmarks are on Integer/Real.
                var strVal = System.Text.Encoding.UTF8.GetString(data);
                cmp = string.Compare(strVal, s, StringComparison.Ordinal);
            }
            else return false;
        }
        else
        {
            return false; // Blob or other
        }

        return op switch
        {
            SharcOperator.Equal => cmp == 0,
            SharcOperator.NotEqual => cmp != 0,
            SharcOperator.LessThan => cmp < 0,
            SharcOperator.GreaterThan => cmp > 0,
            SharcOperator.LessOrEqual => cmp <= 0,
            SharcOperator.GreaterOrEqual => cmp >= 0,
            _ => false
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsIntegral(long st) => st >= 1 && st <= 6 || st == 8 || st == 9;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsReal(long st) => st == 7;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsText(long st) => st >= 13 && (st & 1) == 1;

    private static long DecodeInt(ReadOnlySpan<byte> data, long st)
    {
        return st switch
        {
            1 => (sbyte)data[0],
            2 => System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(data),
            3 => DecodeInt24(data),
            4 => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data),
            5 => DecodeInt48(data),
            6 => System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(data),
            8 => 0,
            9 => 1,
            _ => 0
        };
    }

    // Duplicated from RecordDecoder to avoid public exposure changes, or make RecordDecoder public?
    // RecordDecoder.DecodeInt24 is private.
    private static long DecodeInt24(ReadOnlySpan<byte> data)
    {
        int raw = (data[0] << 16) | (data[1] << 8) | data[2];
        if ((raw & 0x800000) != 0) raw |= unchecked((int)0xFF000000);
        return raw;
    }
    
    private static long DecodeInt48(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8) | data[5];
        if ((raw & 0x800000000000L) != 0) raw |= unchecked((long)0xFFFF000000000000L);
        return raw;
    }

    /// <summary>
    /// Compares a ColumnValue to a CLR object. Returns negative, zero, or positive.
    /// Returns null if comparison is undefined (incompatible types).
    /// </summary>
    internal static int? CompareColumnValue(ColumnValue column, object filterValue)
    {
        // ... (existing implementation) ...
        return column.StorageClass switch
        {
            ColumnStorageClass.Integral => filterValue switch
            {
                long l => column.AsInt64().CompareTo(l),
                int i => column.AsInt64().CompareTo((long)i),
                double d => ((double)column.AsInt64()).CompareTo(d),
                _ => null
            },
            ColumnStorageClass.Real => filterValue switch
            {
                double d => column.AsDouble().CompareTo(d),
                float f => column.AsDouble().CompareTo((double)f),
                long l => column.AsDouble().CompareTo((double)l),
                int i => column.AsDouble().CompareTo((double)i),
                _ => null
            },
            ColumnStorageClass.Text => filterValue switch
            {
                string s => string.Compare(column.AsString(), s, StringComparison.Ordinal),
                _ => null
            },
            _ => null
        };
    }
}