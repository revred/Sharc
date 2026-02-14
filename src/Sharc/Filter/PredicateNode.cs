// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Runtime.CompilerServices;
using Sharc.Core.Primitives;

namespace Sharc;

/// <summary>
/// Leaf filter node â€” evaluates a single column predicate against raw record bytes.
/// Zero allocation on the evaluation path.
/// </summary>
internal sealed class PredicateNode : IFilterNode
{
    private readonly int _columnOrdinal;
    private readonly FilterOp _operator;
    private readonly TypedFilterValue _value;
    private readonly int _rowidAliasOrdinal;

    internal PredicateNode(int columnOrdinal, FilterOp op, TypedFilterValue value, int rowidAliasOrdinal = -1)
    {
        _columnOrdinal = columnOrdinal;
        _operator = op;
        _value = value;
        _rowidAliasOrdinal = rowidAliasOrdinal;
    }

    public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                         int bodyOffset, long rowId)
    {
        if (_columnOrdinal >= serialTypes.Length)
            return false;

        long serialType = serialTypes[_columnOrdinal];

        // â”€â”€ Phase 1: Serial-type-only predicates (zero body access) â”€â”€
        if (_operator == FilterOp.IsNull)
            return serialType == 0 && _columnOrdinal != _rowidAliasOrdinal;

        if (_operator == FilterOp.IsNotNull)
            return serialType != 0 || _columnOrdinal == _rowidAliasOrdinal;

        // NULL columns never match any comparison (SQLite semantics)
        if (serialType == 0)
        {
            // INTEGER PRIMARY KEY alias: real value is rowid
            if (_columnOrdinal == _rowidAliasOrdinal)
                return EvaluateRowidAlias(rowId);
            return false;
        }

        // â”€â”€ Phase 2: Byte-level predicates â”€â”€
        var (offset, length) = GetColumnBodyPosition(serialTypes, bodyOffset, _columnOrdinal);
        var columnData = payload.Slice(offset, length);

        if (SerialTypeCodec.IsIntegral(serialType))
            return EvaluateInteger(columnData, serialType);

        if (SerialTypeCodec.IsReal(serialType))
            return EvaluateReal(columnData);

        if (SerialTypeCodec.IsText(serialType))
            return EvaluateText(columnData);

        return false;
    }

    private bool EvaluateRowidAlias(long rowId)
    {
        return _operator switch
        {
            FilterOp.Eq => _value.ValueTag == TypedFilterValue.Tag.Int64 && rowId == _value.AsInt64(),
            FilterOp.Neq => _value.ValueTag != TypedFilterValue.Tag.Int64 || rowId != _value.AsInt64(),
            FilterOp.Lt => _value.ValueTag == TypedFilterValue.Tag.Int64 && rowId < _value.AsInt64(),
            FilterOp.Lte => _value.ValueTag == TypedFilterValue.Tag.Int64 && rowId <= _value.AsInt64(),
            FilterOp.Gt => _value.ValueTag == TypedFilterValue.Tag.Int64 && rowId > _value.AsInt64(),
            FilterOp.Gte => _value.ValueTag == TypedFilterValue.Tag.Int64 && rowId >= _value.AsInt64(),
            FilterOp.Between => _value.ValueTag == TypedFilterValue.Tag.Int64Range &&
                                rowId >= _value.AsInt64() && rowId <= _value.AsInt64High(),
            FilterOp.In => EvaluateInt64In(rowId),
            FilterOp.NotIn => !EvaluateInt64In(rowId),
            _ => false
        };
    }

    private bool EvaluateInteger(ReadOnlySpan<byte> data, long serialType)
    {
        return _operator switch
        {
            FilterOp.Eq => _value.ValueTag switch
            {
                TypedFilterValue.Tag.Int64 => RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) == 0,
                TypedFilterValue.Tag.Double => (double)RawByteComparer.DecodeInt64(data, serialType) == _value.AsDouble(),
                _ => false
            },
            FilterOp.Neq => _value.ValueTag switch
            {
                TypedFilterValue.Tag.Int64 => RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) != 0,
                TypedFilterValue.Tag.Double => (double)RawByteComparer.DecodeInt64(data, serialType) != _value.AsDouble(),
                _ => true
            },
            FilterOp.Lt => _value.ValueTag == TypedFilterValue.Tag.Int64 &&
                           RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) < 0,
            FilterOp.Lte => _value.ValueTag == TypedFilterValue.Tag.Int64 &&
                            RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) <= 0,
            FilterOp.Gt => _value.ValueTag == TypedFilterValue.Tag.Int64 &&
                           RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) > 0,
            FilterOp.Gte => _value.ValueTag == TypedFilterValue.Tag.Int64 &&
                            RawByteComparer.CompareInt64(data, serialType, _value.AsInt64()) >= 0,
            FilterOp.Between => _value.ValueTag == TypedFilterValue.Tag.Int64Range &&
                                EvaluateInt64Between(data, serialType),
            FilterOp.In => _value.ValueTag == TypedFilterValue.Tag.Int64Set &&
                           EvaluateInt64In(RawByteComparer.DecodeInt64(data, serialType)),
            FilterOp.NotIn => _value.ValueTag != TypedFilterValue.Tag.Int64Set ||
                              !EvaluateInt64In(RawByteComparer.DecodeInt64(data, serialType)),
            FilterOp.StartsWith or FilterOp.EndsWith or FilterOp.Contains => false,
            _ => false
        };
    }

    private bool EvaluateReal(ReadOnlySpan<byte> data)
    {
        double colVal = RawByteComparer.DecodeDouble(data);

        return _operator switch
        {
            FilterOp.Eq => _value.ValueTag switch
            {
                TypedFilterValue.Tag.Double => colVal == _value.AsDouble(),
                TypedFilterValue.Tag.Int64 => colVal == (double)_value.AsInt64(),
                _ => false
            },
            FilterOp.Neq => _value.ValueTag switch
            {
                TypedFilterValue.Tag.Double => colVal != _value.AsDouble(),
                TypedFilterValue.Tag.Int64 => colVal != (double)_value.AsInt64(),
                _ => true
            },
            FilterOp.Lt => _value.ValueTag == TypedFilterValue.Tag.Double && colVal < _value.AsDouble(),
            FilterOp.Lte => _value.ValueTag == TypedFilterValue.Tag.Double && colVal <= _value.AsDouble(),
            FilterOp.Gt => _value.ValueTag == TypedFilterValue.Tag.Double && colVal > _value.AsDouble(),
            FilterOp.Gte => _value.ValueTag == TypedFilterValue.Tag.Double && colVal >= _value.AsDouble(),
            FilterOp.Between => _value.ValueTag == TypedFilterValue.Tag.DoubleRange &&
                                colVal >= _value.AsDouble() && colVal <= _value.AsDoubleHigh(),
            _ => false
        };
    }

    private bool EvaluateText(ReadOnlySpan<byte> data)
    {
        return _operator switch
        {
            FilterOp.Eq => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                           RawByteComparer.Utf8Compare(data, _value.AsUtf8()) == 0,
            FilterOp.Neq => _value.ValueTag != TypedFilterValue.Tag.Utf8 ||
                            RawByteComparer.Utf8Compare(data, _value.AsUtf8()) != 0,
            FilterOp.Lt => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                           RawByteComparer.Utf8Compare(data, _value.AsUtf8()) < 0,
            FilterOp.Lte => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                            RawByteComparer.Utf8Compare(data, _value.AsUtf8()) <= 0,
            FilterOp.Gt => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                           RawByteComparer.Utf8Compare(data, _value.AsUtf8()) > 0,
            FilterOp.Gte => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                            RawByteComparer.Utf8Compare(data, _value.AsUtf8()) >= 0,
            FilterOp.StartsWith => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                                   RawByteComparer.Utf8StartsWith(data, _value.AsUtf8()),
            FilterOp.EndsWith => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                                 RawByteComparer.Utf8EndsWith(data, _value.AsUtf8()),
            FilterOp.Contains => _value.ValueTag == TypedFilterValue.Tag.Utf8 &&
                                 RawByteComparer.Utf8Contains(data, _value.AsUtf8()),
            FilterOp.In => _value.ValueTag == TypedFilterValue.Tag.Utf8Set &&
                           EvaluateUtf8In(data),
            FilterOp.NotIn => _value.ValueTag != TypedFilterValue.Tag.Utf8Set ||
                              !EvaluateUtf8In(data),
            _ => false
        };
    }

    private bool EvaluateInt64Between(ReadOnlySpan<byte> data, long serialType)
    {
        long val = RawByteComparer.DecodeInt64(data, serialType);
        return val >= _value.AsInt64() && val <= _value.AsInt64High();
    }

    private bool EvaluateInt64In(long columnValue)
    {
        long[] set = _value.AsInt64Set();
        for (int i = 0; i < set.Length; i++)
        {
            if (set[i] == columnValue)
                return true;
        }
        return false;
    }

    private bool EvaluateUtf8In(ReadOnlySpan<byte> data)
    {
        ReadOnlyMemory<byte>[] set = _value.AsUtf8Set();
        for (int i = 0; i < set.Length; i++)
        {
            if (data.SequenceEqual(set[i].Span))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes the byte offset and length of a specific column within the record body,
    /// using only the serial types array. No record body access required.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int Offset, int Length) GetColumnBodyPosition(
        ReadOnlySpan<long> serialTypes, int bodyStartOffset, int columnIndex)
    {
        int offset = bodyStartOffset;
        for (int i = 0; i < columnIndex; i++)
        {
            offset += SerialTypeCodec.GetContentSize(serialTypes[i]);
        }
        int length = SerialTypeCodec.GetContentSize(serialTypes[columnIndex]);
        return (offset, length);
    }
}