// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Primitives;
using Sharc.Query;

namespace Sharc;

public sealed partial class SharcDataReader
{
    // ─── Fingerprinting ──────────────────────────────────────────

    /// <summary>
    /// Computes a 64-bit FNV-1a fingerprint of the current row's projected columns
    /// from raw cursor payload bytes. Zero string allocation — hashes raw UTF-8 bytes
    /// directly from page spans. Used for set operation dedup (UNION/INTERSECT/EXCEPT).
    /// </summary>
    /// <remarks>
    /// Collision probability: for N rows, P ≈ N²/2⁶⁵. At 5000 rows: ~10⁻¹¹%.
    /// Stays below 0.0001% up to ~6 million rows.
    /// </remarks>
    internal Fingerprint128 GetRowFingerprint()
    {
        if (_composite != null)
            return _composite.GetRowFingerprint(this);

        // Cursor mode: hash raw payload bytes (zero string allocation)
        return GetCursorRowFingerprint();
    }

    private Fingerprint128 GetCursorRowFingerprint()
    {
        var payload = _cursor!.Payload;

        if (_serialTypes != null && IsLazy && _columnOffsets != null)
        {
            // Fast path: reuse precomputed offsets from DecodeCurrentRow() — zero recomputation
            int colCount = Math.Min(_columnCount, _serialTypes.Length);
            return ComputeFingerprint(payload, _serialTypes.AsSpan(0, colCount), _columnOffsets.AsSpan(0, colCount));
        }

        // Non-projection path: parse serial types on the fly (stackalloc — zero alloc)
        Span<long> stackSt = stackalloc long[Math.Min((int)_columnCount, 64)];
        _recordDecoder!.ReadSerialTypes(payload, stackSt, out int bodyOffset);
        return ComputeFingerprint(payload, stackSt.Slice(0, Math.Min(_columnCount, stackSt.Length)), bodyOffset);
    }

    private Fingerprint128 ComputeFingerprint(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        // Compute cumulative byte offsets for all physical columns (single pass)
        Span<int> offsets = stackalloc int[serialTypes.Length];
        int runningOffset = bodyOffset;
        for (int c = 0; c < serialTypes.Length; c++)
        {
            offsets[c] = runningOffset;
            runningOffset += SerialTypeCodec.GetContentSize(serialTypes[c]);
        }

        return ComputeFingerprint(payload, serialTypes, offsets);
    }

    private Fingerprint128 ComputeFingerprint(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, ReadOnlySpan<int> offsets)
    {
        // Hash projected columns
        var hasher = new Fnv1aHasher();
        int projectedCount = _projection?.Length ?? _columnCount;

        for (int i = 0; i < projectedCount; i++)
        {
            int physOrdinal = _projection != null ? _projection[i] : i;

            // INTEGER PRIMARY KEY: hash rowid instead of the NULL stored in the record
            if (physOrdinal == _rowidAliasOrdinal)
            {
                hasher.AddTypeTag(i, 1); // 1 = integer
                hasher.AppendLong(_cursor!.RowId);
                continue;
            }

            if (physOrdinal >= serialTypes.Length) continue;

            long st = serialTypes[physOrdinal];
            int size = SerialTypeCodec.GetContentSize(st);

            // Type tag: 0=null, 1=int, 2=float, 3=text, 4=blob
            hasher.AddTypeTag(i, st == 0 ? (byte)0
                : st <= 6 ? (byte)1
                : st == 7 ? (byte)2
                : (st >= 13 && (st & 1) == 1) ? (byte)3
                : (byte)4);

            // Hash serial type (encodes type info) + column body bytes (encodes value)
            hasher.AppendLong(st);
            if (size > 0)
                hasher.Append(payload.Slice(offsets[physOrdinal], size));
        }

        return hasher.Hash;
    }

    private Fingerprint128 GetMaterializedRowFingerprint()
    {
        var row = _composite!.CurrentMaterializedRow;
        var hasher = new Fnv1aHasher();
        for (int i = 0; i < _columnCount && i < row.Length; i++)
        {
            ref var val = ref row[i];
            switch (val.Type)
            {
                case QueryValueType.Null:
                    hasher.AddTypeTag(i, 0);
                    hasher.AppendLong(0); break;
                case QueryValueType.Int64:
                    hasher.AddTypeTag(i, 1);
                    hasher.AppendLong(val.AsInt64()); break;
                case QueryValueType.Double:
                    hasher.AddTypeTag(i, 2);
                    hasher.AppendLong(BitConverter.DoubleToInt64Bits(val.AsDouble())); break;
                case QueryValueType.Text:
                    hasher.AddTypeTag(i, 3);
                    hasher.AppendString(val.AsString()); break;
                default:
                    hasher.AddTypeTag(i, 4);
                    hasher.AppendLong(val.ObjectValue?.GetHashCode() ?? 0); break;
            }
        }
        return hasher.Hash;
    }

    /// <summary>
    /// Computes a 64-bit FNV-1a fingerprint of a single column from the current row.
    /// Zero string allocation — hashes raw bytes directly from cursor payload.
    /// Used for group-key matching in streaming aggregation to avoid materializing
    /// text columns for rows in existing groups.
    /// </summary>
    internal Fingerprint128 GetColumnFingerprint(int ordinal)
    {
        if (_composite != null)
            return _composite.GetColumnFingerprint(this, ordinal);

        // Cursor/lazy mode: hash raw column bytes without string allocation
        int physOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        if (physOrdinal == _rowidAliasOrdinal)
        {
            var h = new Fnv1aHasher();
            h.AddTypeTag(0, 1); // integer
            h.AppendLong(_cursor!.RowId);
            return h.Hash;
        }

        var payload = _cursor!.Payload;
        var stSpan = IsLazy ? _serialTypes! : _filter!.FilterSerialTypes!;

        // Use precomputed O(1) offset
        int offset = _columnOffsets![physOrdinal];

        long st = stSpan[physOrdinal];
        int size = Core.Primitives.SerialTypeCodec.GetContentSize(st);

        var hasher = new Fnv1aHasher();
        // Type tag for single-column fingerprint
        hasher.AddTypeTag(0, st == 0 ? (byte)0
            : st <= 6 ? (byte)1
            : st == 7 ? (byte)2
            : (st >= 13 && (st & 1) == 1) ? (byte)3
            : (byte)4);
        hasher.AppendLong(st);
        if (size > 0) hasher.Append(payload.Slice(offset, size));
        return hasher.Hash;
    }
}
