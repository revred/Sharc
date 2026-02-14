// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Sharc.Core.Primitives;
using Sharc.Core.Query;

namespace Sharc.Core.Records;

/// <summary>
/// Decodes SQLite record format (header + body) into typed column values.
/// </summary>
internal sealed class RecordDecoder : IRecordDecoder, ISharcExtension
{
    /// <inheritdoc />
    public string Name => "RecordDecoder";

    /// <inheritdoc />
    public void OnRegister(object context) { }

    /// <inheritdoc />
    public ColumnValue[] DecodeRecord(ReadOnlySpan<byte> payload)
    {
        int count = GetColumnCount(payload);
        var columns = new ColumnValue[count];
        DecodeRecord(payload, columns);
        return columns;
    }

    /// <inheritdoc />
    public void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination)
    {
        int offset = VarintDecoder.Read(payload, out long headerSize);
        int headerEnd = (int)headerSize;

        // Single pass: read serial types from header and decode body simultaneously.
        // Use stackalloc to avoid List<long> allocation for up to 64 columns.
        int colCount = 0;
        Span<long> serialTypes = stackalloc long[64];
        int pos = offset;
        while (pos < headerEnd)
        {
            pos += VarintDecoder.Read(payload[pos..], out long st);
            if (colCount < serialTypes.Length)
                serialTypes[colCount] = st;
            colCount++;
        }

        // For tables with >64 columns (rare), fall back to heap allocation
        if (colCount > 64)
        {
            serialTypes = new long[colCount];
            pos = offset;
            colCount = 0;
            while (pos < headerEnd)
            {
                pos += VarintDecoder.Read(payload[pos..], out long st);
                serialTypes[colCount++] = st;
            }
        }

        DecodeBody(payload, destination, serialTypes.Slice(0, Math.Min(colCount, serialTypes.Length)), headerEnd);
    }

    /// <inheritdoc />
    // This implementation is static-capable but must be an instance method to satisfy the interface.
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public void DecodeRecord(ReadOnlySpan<byte> payload, ColumnValue[] destination, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        DecodeBody(payload, destination, serialTypes, bodyOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecodeBody(ReadOnlySpan<byte> payload, ColumnValue[] destination, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        int decodeCount = Math.Min(serialTypes.Length, destination.Length);
        for (int i = 0; i < decodeCount; i++)
        {
            long st = serialTypes[i];
            int contentSize = SerialTypeCodec.GetContentSize(st);
            destination[i] = DecodeValue(payload.Slice(bodyOffset, contentSize), st);
            bodyOffset += contentSize;
        }
    }

    /// <inheritdoc />
    public int GetColumnCount(ReadOnlySpan<byte> payload)
    {
        int offset = VarintDecoder.Read(payload, out long headerSize);
        int headerEnd = (int)headerSize;

        int count = 0;
        while (offset < headerEnd)
        {
            offset += VarintDecoder.Read(payload[offset..], out _);
            count++;
        }

        return count;
    }

    /// <inheritdoc />
    public ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex)
    {
        int offset = VarintDecoder.Read(payload, out long headerSize);
        int headerEnd = (int)headerSize;

        // Skip to the target column's serial type and calculate body offset
        int bodyOffset = headerEnd;
        long targetSerialType = 0;
        int colIdx = 0;
        bool found = false;

        int headerPos = offset;
        while (headerPos < headerEnd)
        {
            headerPos += VarintDecoder.Read(payload[headerPos..], out long st);
            if (colIdx == columnIndex)
            {
                targetSerialType = st;
                found = true;
                break;
            }
            bodyOffset += SerialTypeCodec.GetContentSize(st);
            colIdx++;
        }

        if (!found)
            throw new ArgumentOutOfRangeException(nameof(columnIndex),
                columnIndex, "Column index exceeds record column count.");

        int contentSize = SerialTypeCodec.GetContentSize(targetSerialType);
        return DecodeValue(payload.Slice(bodyOffset, contentSize), targetSerialType);
    }

    /// <inheritdoc />
    // This implementation is static-capable but must be an instance method to satisfy the interface.
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public ColumnValue DecodeColumn(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        if (columnIndex < 0 || columnIndex >= serialTypes.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), columnIndex, "Column index is out of range.");

        int currentOffset = bodyOffset;
        for (int i = 0; i < columnIndex; i++)
        {
            currentOffset += SerialTypeCodec.GetContentSize(serialTypes[i]);
        }

        long targetSerialType = serialTypes[columnIndex];
        int contentSize = SerialTypeCodec.GetContentSize(targetSerialType);
        
        return DecodeValue(payload.Slice(currentOffset, contentSize), targetSerialType);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSerialTypes(ReadOnlySpan<byte> payload, long[] serialTypes, out int bodyOffset)
    {
        return ReadSerialTypes(payload, serialTypes.AsSpan(), out bodyOffset);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSerialTypes(ReadOnlySpan<byte> payload, Span<long> serialTypes, out int bodyOffset)
    {
        int offset = VarintDecoder.Read(payload, out long headerSize);
        bodyOffset = (int)headerSize;
        int headerEnd = bodyOffset;

        int colCount = 0;
        int capacity = serialTypes.Length;
        while (offset < headerEnd && colCount < capacity)
        {
            offset += VarintDecoder.Read(payload[offset..], out serialTypes[colCount]);
            colCount++;
        }

        // Count remaining columns (if any beyond the destination array)
        while (offset < headerEnd)
        {
            offset += VarintDecoder.Read(payload[offset..], out _);
            colCount++;
        }

        return colCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ColumnValue DecodeValue(ReadOnlySpan<byte> data, long serialType)
    {
        return serialType switch
        {
            0 => ColumnValue.Null(),
            1 => ColumnValue.FromInt64(1, (sbyte)data[0]),
            2 => ColumnValue.FromInt64(2, BinaryPrimitives.ReadInt16BigEndian(data)),
            3 => DecodeInt24(data),
            4 => ColumnValue.FromInt64(4, BinaryPrimitives.ReadInt32BigEndian(data)),
            5 => DecodeInt48(data),
            6 => ColumnValue.FromInt64(6, BinaryPrimitives.ReadInt64BigEndian(data)),
            7 => ColumnValue.FromDouble(BinaryPrimitives.ReadDoubleBigEndian(data)),
            8 => ColumnValue.FromInt64(8, 0),
            9 => ColumnValue.FromInt64(9, 1),
            _ => DecodeVariableLength(data, serialType)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ColumnValue DecodeInt24(ReadOnlySpan<byte> data)
    {
        int raw = (data[0] << 16) | (data[1] << 8) | data[2];
        // Sign-extend from 24-bit
        if ((raw & 0x800000) != 0)
            raw |= unchecked((int)0xFF000000);
        return ColumnValue.FromInt64(3, raw);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ColumnValue DecodeInt48(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8) | data[5];
        // Sign-extend from 48-bit
        if ((raw & 0x800000000000L) != 0)
            raw |= unchecked((long)0xFFFF000000000000L);
        return ColumnValue.FromInt64(5, raw);
    }

    private static ColumnValue DecodeVariableLength(ReadOnlySpan<byte> data, long serialType)
    {
        if (serialType >= 12 && (serialType & 1) == 0)
        {
            // BLOB
            return ColumnValue.Blob(serialType, data.ToArray());
        }

        if (serialType >= 13 && (serialType & 1) == 1)
        {
            // TEXT
            return ColumnValue.Text(serialType, data.ToArray());
        }

        throw new ArgumentOutOfRangeException(nameof(serialType), serialType, "Invalid serial type.");
    }

    /// <summary>
    /// Computes the byte offset within the payload body for the given column index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeColumnOffset(ReadOnlySpan<long> serialTypes, int columnIndex, int bodyOffset)
    {
        int offset = bodyOffset;
        for (int i = 0; i < columnIndex; i++)
            offset += SerialTypeCodec.GetContentSize(serialTypes[i]);
        return offset;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public string DecodeStringDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        int offset = ComputeColumnOffset(serialTypes, columnIndex, bodyOffset);
        int size = SerialTypeCodec.GetContentSize(serialTypes[columnIndex]);
        // Decode UTF-8 directly from the page span — one string allocation, zero byte[] intermediates
        return System.Text.Encoding.UTF8.GetString(payload.Slice(offset, size));
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public long DecodeInt64Direct(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        long st = serialTypes[columnIndex];
        // Constant integer serial types (0, 1) — no body bytes needed
        if (st == 8) return 0;
        if (st == 9) return 1;
        if (st == 0) return 0; // NULL → 0

        int offset = ComputeColumnOffset(serialTypes, columnIndex, bodyOffset);
        var data = payload.Slice(offset, SerialTypeCodec.GetContentSize(st));

        return st switch
        {
            1 => (sbyte)data[0],
            2 => BinaryPrimitives.ReadInt16BigEndian(data),
            3 => ((data[0] & 0x80) != 0)
                ? (int)((uint)(data[0] << 16) | (uint)(data[1] << 8) | data[2]) | unchecked((int)0xFF000000)
                : (data[0] << 16) | (data[1] << 8) | data[2],
            4 => BinaryPrimitives.ReadInt32BigEndian(data),
            5 => DecodeInt48Raw(data),
            6 => BinaryPrimitives.ReadInt64BigEndian(data),
            _ => 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt48Raw(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8) | data[5];
        if ((raw & 0x800000000000L) != 0)
            raw |= unchecked((long)0xFFFF000000000000L);
        return raw;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public double DecodeDoubleDirect(ReadOnlySpan<byte> payload, int columnIndex, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        long st = serialTypes[columnIndex];

        // NULL → 0.0
        if (st == 0) return 0.0;
        // Constant integer serial types
        if (st == 8) return 0.0;
        if (st == 9) return 1.0;

        int offset = ComputeColumnOffset(serialTypes, columnIndex, bodyOffset);

        // Serial type 7 = 8-byte IEEE 754 float
        if (st == 7)
            return BinaryPrimitives.ReadDoubleBigEndian(payload.Slice(offset, 8));

        // Integer serial types (1-6) — SQLite stores doubles as integers when exact
        var data = payload.Slice(offset, SerialTypeCodec.GetContentSize(st));
        long intVal = st switch
        {
            1 => (sbyte)data[0],
            2 => BinaryPrimitives.ReadInt16BigEndian(data),
            3 => ((data[0] & 0x80) != 0)
                ? (int)((uint)(data[0] << 16) | (uint)(data[1] << 8) | data[2]) | unchecked((int)0xFF000000)
                : (data[0] << 16) | (data[1] << 8) | data[2],
            4 => BinaryPrimitives.ReadInt32BigEndian(data),
            5 => DecodeInt48Raw(data),
            6 => BinaryPrimitives.ReadInt64BigEndian(data),
            _ => 0
        };
        return (double)intVal;
    }

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> payload, ResolvedFilter[] filters)
    {
        // 1. Calculate max column ordinal to bound the serial type parsing
        int maxOrdinal = -1;
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].ColumnOrdinal > maxOrdinal)
                maxOrdinal = filters[i].ColumnOrdinal;
        }

        // 2. Parse serial types (stackalloc to avoid allocation)
        Span<long> serialTypes = stackalloc long[Math.Max(maxOrdinal + 1, 16)];
        int colCount = ReadSerialTypes(payload, serialTypes, out int bodyOffset);

        // 3. Evaluate each filter
        for (int i = 0; i < filters.Length; i++)
        {
            ref readonly var f = ref filters[i];

            // If filter column is out of range, treat as NULL
            if (f.ColumnOrdinal >= colCount)
            {
                // NULL never matches standard operators
                return false; 
            }

            long st = serialTypes[f.ColumnOrdinal];

            // 4. Locate column data
            int currentOffset = bodyOffset;
            for (int c = 0; c < f.ColumnOrdinal; c++)
            {
                currentOffset += SerialTypeCodec.GetContentSize(serialTypes[c]);
            }
            int contentSize = SerialTypeCodec.GetContentSize(st);
            var data = payload.Slice(currentOffset, contentSize);

            // 5. Compare raw bytes
            if (!MatchesRawValue(data, st, f.Operator, f.Value))
                return false;
        }

        return true;
    }

    private static bool MatchesRawValue(ReadOnlySpan<byte> data, long st, SharcOperator op, object? filterValue)
    {
        if (st == 0) return false; // NULL matches nothing
        if (filterValue is null) return false;

        int cmp = 0;

        if (IsIntegral(st))
        {
            long val = DecodeInt(data, st);
            switch (filterValue)
            {
                case long l: cmp = val.CompareTo(l); break;
                case int i: cmp = val.CompareTo((long)i); break;
                case double d: cmp = ((double)val).CompareTo(d); break;
                default: return false;
            }
        }
        else if (IsReal(st))
        {
            double val = BinaryPrimitives.ReadDoubleBigEndian(data);
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
            if (filterValue is string s)
            {
                // Optimization: Stack-based comparison for short strings
                if (s.Length <= 128)
                {
                    Span<char> chars = stackalloc char[s.Length];
                    int charCount = System.Text.Encoding.UTF8.GetChars(data, chars);
                    // Standard string comparison logic would require identical chars
                    // but we only populated 'chars' from UTF8 bytes.
                    // This comparison is only strictly valid if char counts match.
                    if (charCount != s.Length) 
                    {
                        // Length mismatch -> Not equal.
                        // For ordering, we'd need complex logic.
                        // Assuming most filters are EQUAL, this is a fast reject path.
                        // But what if op is LessThan? We can't shortcut.
                        // Fallback to alloc for non-Equal?
                        if (op == SharcOperator.Equal) return false;
                        if (op == SharcOperator.NotEqual) return true;
                        
                        // Fallback for ordering
                        var strVal = System.Text.Encoding.UTF8.GetString(data);
                        cmp = string.Compare(strVal, s, StringComparison.Ordinal);
                    }
                    else
                    {
                        // Lengths match, compare chars
                        cmp = chars.SequenceCompareTo(s.AsSpan());
                    }
                }
                else
                {
                    var strVal = System.Text.Encoding.UTF8.GetString(data);
                    cmp = string.Compare(strVal, s, StringComparison.Ordinal);
                }
            }
            else return false;
        }
        else
        {
            return false;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIntegral(long st) => st >= 1 && st <= 6 || st == 8 || st == 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsReal(long st) => st == 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsText(long st) => st >= 13 && (st & 1) == 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt(ReadOnlySpan<byte> data, long st)
    {
        return st switch
        {
            1 => (sbyte)data[0],
            2 => BinaryPrimitives.ReadInt16BigEndian(data),
            3 => DecodeInt24Raw(data),
            4 => BinaryPrimitives.ReadInt32BigEndian(data),
            5 => DecodeInt48Raw(data),
            6 => BinaryPrimitives.ReadInt64BigEndian(data),
            8 => 0,
            9 => 1,
            _ => 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt24Raw(ReadOnlySpan<byte> data)
    {
        int raw = (data[0] << 16) | (data[1] << 8) | data[2];
        if ((raw & 0x800000) != 0) raw |= unchecked((int)0xFF000000);
        return raw;
    }
}
