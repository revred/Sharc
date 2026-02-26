// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Text;
using Sharc.Core;
using Sharc.Core.Primitives;
using Intent = Sharc.Query.Intent;

namespace Sharc.Query.Optimization;

/// <summary>
/// An <see cref="IBTreeCursor"/> adapter that wraps an <see cref="IIndexBTreeCursor"/>
/// and an <see cref="IBTreeCursor"/> (table cursor) to provide index-accelerated reads.
/// Supports integer, real, and text key seeks.
/// </summary>
internal sealed class IndexSeekCursor : IBTreeCursor, IIndexCursorDiagnostics
{
    private const int SerialBufferSize = 64;

    private readonly IIndexBTreeCursor _indexCursor;
    private readonly IBTreeCursor _tableCursor;
    private readonly IRecordDecoder _decoder;
    private readonly IndexSeekPlan _plan;
    private readonly long[] _indexSerials;
    private readonly byte[]? _textKeyUtf8;
    private bool _seekDone;
    private bool _exhausted;
    private bool _disposed;
    private int _indexEntriesScanned;
    private int _indexHits;

    public IndexSeekCursor(
        IBTreeReader bTreeReader,
        IRecordDecoder decoder,
        uint indexRootPage,
        uint tableRootPage,
        IndexSeekPlan plan)
    {
        _indexCursor = bTreeReader.CreateIndexCursor(indexRootPage);
        _tableCursor = bTreeReader.CreateCursor(tableRootPage);
        _decoder = decoder;
        _plan = plan;
        _indexSerials = ArrayPool<long>.Shared.Rent(SerialBufferSize);

        if (plan.IsTextKey && plan.TextValue != null)
            _textKeyUtf8 = Encoding.UTF8.GetBytes(plan.TextValue);
    }

    /// <inheritdoc />
    public long RowId => _tableCursor.RowId;

    /// <inheritdoc />
    public ReadOnlySpan<byte> Payload => _tableCursor.Payload;

    /// <inheritdoc />
    public int PayloadSize => _tableCursor.PayloadSize;

    /// <inheritdoc />
    public bool IsStale => _tableCursor.IsStale || _indexCursor.IsStale;

    /// <inheritdoc />
    public int IndexEntriesScanned => _indexEntriesScanned;

    /// <inheritdoc />
    public int IndexHits => _indexHits;

    /// <inheritdoc />
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_exhausted)
            return false;

        if (!_seekDone)
        {
            _seekDone = true;
            if (!TryPositionAtStart(_indexCursor, _plan, _textKeyUtf8))
            {
                _exhausted = true;
                return false;
            }
        }
        else if (!_indexCursor.MoveNext())
        {
            _exhausted = true;
            return false;
        }

        return ResolveFromCurrentOrNext();
    }

    private bool ResolveFromCurrentOrNext()
    {
        do
        {
            var payload = _indexCursor.Payload;
            if (payload.IsEmpty)
                break;

            _indexEntriesScanned++;

            if (TryMatchEntry(
                _plan,
                _textKeyUtf8,
                _decoder,
                _indexSerials,
                payload,
                out long rowId,
                out bool pastRange))
            {
                _indexHits++;
                if (_tableCursor.Seek(rowId))
                    return true;
            }
            else if (pastRange)
            {
                break;
            }
        }
        while (_indexCursor.MoveNext());

        _exhausted = true;
        return false;
    }

    internal static bool TryPositionAtStart(
        IIndexBTreeCursor indexCursor,
        in IndexSeekPlan plan,
        byte[]? textKeyUtf8)
    {
        bool usingSeek = plan.SeekOp is not (Intent.IntentOp.Lt or Intent.IntentOp.Lte);
        if (!usingSeek)
            return indexCursor.MoveNext();

        bool exact = plan.IsTextKey && textKeyUtf8 != null
            ? indexCursor.SeekFirst(textKeyUtf8)
            : plan.IsRealKey
                ? indexCursor.SeekFirst(plan.SeekRealKey)
                : indexCursor.SeekFirst(plan.SeekKey);

        if (plan.SeekOp == Intent.IntentOp.Eq && !exact)
            return false;

        // For non-Eq operations, SeekFirst(false) still positions the cursor at insertion point.
        return !indexCursor.Payload.IsEmpty;
    }

    internal static bool TryMatchEntry(
        in IndexSeekPlan plan,
        byte[]? textKeyUtf8,
        IRecordDecoder decoder,
        long[] serials,
        ReadOnlySpan<byte> payload,
        out long rowId,
        out bool pastRange)
    {
        rowId = 0;
        pastRange = false;

        if (payload.IsEmpty)
        {
            pastRange = true;
            return false;
        }

        int colCount = decoder.ReadSerialTypes(payload, serials, out int bodyOffset);
        if (colCount <= 1)
            return false;

        if (!MatchesFirstColumn(plan, textKeyUtf8, decoder, payload, serials, bodyOffset, out pastRange))
            return false;

        if (!MatchesResidualConstraints(plan, decoder, payload, serials, colCount, bodyOffset))
            return false;

        rowId = decoder.DecodeInt64Direct(payload, colCount - 1, serials, bodyOffset);
        return true;
    }

    private static bool MatchesFirstColumn(
        in IndexSeekPlan plan,
        byte[]? textKeyUtf8,
        IRecordDecoder decoder,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<long> serials,
        int bodyOffset,
        out bool pastRange)
    {
        pastRange = false;

        long serialType = serials[0];

        if (plan.IsTextKey)
            return MatchesTextFirstColumn(plan, textKeyUtf8, payload, serials, bodyOffset, serialType, out pastRange);

        if (SerialTypeCodec.IsNull(serialType))
            return false;

        if (!SerialTypeCodec.IsIntegral(serialType) && !SerialTypeCodec.IsReal(serialType))
        {
            // TEXT/BLOB sort after numeric values in SQLite order.
            pastRange = true;
            return false;
        }

        double value = decoder.DecodeDoubleDirect(payload, 0, serials, bodyOffset);
        double lower = plan.IsRealKey ? plan.SeekRealKey : plan.SeekKey;
        double upper = plan.IsRealKey ? plan.UpperBoundReal : plan.UpperBound;

        return plan.SeekOp switch
        {
            Intent.IntentOp.Eq => MatchEq(value, lower, out pastRange),
            Intent.IntentOp.Gt => value > lower,
            Intent.IntentOp.Gte => value >= lower,
            Intent.IntentOp.Lt => MatchLt(value, lower, out pastRange),
            Intent.IntentOp.Lte => MatchLte(value, lower, out pastRange),
            Intent.IntentOp.Between => MatchBetween(value, lower, upper, out pastRange),
            _ => false
        };
    }

    private static bool MatchesTextFirstColumn(
        in IndexSeekPlan plan,
        byte[]? textKeyUtf8,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<long> serials,
        int bodyOffset,
        long serialType,
        out bool pastRange)
    {
        pastRange = false;
        if (textKeyUtf8 == null)
            return false;

        if (!SerialTypeCodec.IsText(serialType))
        {
            pastRange = true;
            return false;
        }

        if (!TryGetColumnSlice(payload, serials, bodyOffset, 0, out _, out var firstColumn))
        {
            pastRange = true;
            return false;
        }

        int cmp = firstColumn.SequenceCompareTo(textKeyUtf8);
        if (cmp == 0)
            return true;

        if (cmp > 0)
            pastRange = true;

        return false;
    }

    private static bool MatchesResidualConstraints(
        in IndexSeekPlan plan,
        IRecordDecoder decoder,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<long> serials,
        int colCount,
        int bodyOffset)
    {
        var constraints = plan.ResidualConstraints;
        if (constraints == null || constraints.Length == 0)
            return true;

        int keyColumnCount = colCount - 1; // last column is rowid

        for (int i = 0; i < constraints.Length; i++)
        {
            ref readonly var constraint = ref constraints[i];
            if ((uint)constraint.ColumnOrdinal >= (uint)keyColumnCount)
                return false;

            if (constraint.ColumnOrdinal >= serials.Length)
            {
                var fallbackValue = decoder.DecodeColumn(payload, constraint.ColumnOrdinal);
                if (!MatchesFallbackValue(fallbackValue, constraint))
                    return false;
                continue;
            }

            long serialType = serials[constraint.ColumnOrdinal];

            if (constraint.IsTextKey)
            {
                if (!SerialTypeCodec.IsText(serialType))
                    return false;

                string text = decoder.DecodeStringDirect(payload, constraint.ColumnOrdinal, serials, bodyOffset);
                if (!text.Equals(constraint.TextValue, StringComparison.Ordinal))
                    return false;

                continue;
            }

            if (!SerialTypeCodec.IsIntegral(serialType) && !SerialTypeCodec.IsReal(serialType))
                return false;

            if (constraint.IsRealKey)
            {
                double value = decoder.DecodeDoubleDirect(payload, constraint.ColumnOrdinal, serials, bodyOffset);
                if (!MatchesDoubleConstraint(value, constraint.Op, constraint.RealValue, constraint.RealHighValue))
                    return false;
            }
            else if (constraint.IsIntegerKey)
            {
                if (SerialTypeCodec.IsIntegral(serialType))
                {
                    long value = decoder.DecodeInt64Direct(payload, constraint.ColumnOrdinal, serials, bodyOffset);
                    if (!MatchesInt64Constraint(value, constraint.Op, constraint.IntegerValue, constraint.IntegerHighValue))
                        return false;
                }
                else
                {
                    double value = decoder.DecodeDoubleDirect(payload, constraint.ColumnOrdinal, serials, bodyOffset);
                    if (!MatchesDoubleConstraint(value, constraint.Op, constraint.IntegerValue, constraint.IntegerHighValue))
                        return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesFallbackValue(ColumnValue value, in IndexColumnConstraint constraint)
    {
        if (constraint.IsTextKey)
            return value.StorageClass == ColumnStorageClass.Text &&
                value.AsString().Equals(constraint.TextValue, StringComparison.Ordinal);

        if (constraint.IsRealKey)
        {
            if (value.StorageClass == ColumnStorageClass.Real)
                return MatchesDoubleConstraint(value.AsDouble(), constraint.Op, constraint.RealValue, constraint.RealHighValue);
            if (value.StorageClass == ColumnStorageClass.Integral)
                return MatchesDoubleConstraint(value.AsInt64(), constraint.Op, constraint.RealValue, constraint.RealHighValue);
            return false;
        }

        if (constraint.IsIntegerKey)
        {
            if (value.StorageClass == ColumnStorageClass.Integral)
                return MatchesInt64Constraint(value.AsInt64(), constraint.Op, constraint.IntegerValue, constraint.IntegerHighValue);
            if (value.StorageClass == ColumnStorageClass.Real)
                return MatchesDoubleConstraint(value.AsDouble(), constraint.Op, constraint.IntegerValue, constraint.IntegerHighValue);
            return false;
        }

        return false;
    }

    private static bool TryGetColumnSlice(
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<long> serialTypes,
        int bodyOffset,
        int ordinal,
        out long serialType,
        out ReadOnlySpan<byte> columnData)
    {
        serialType = 0;
        columnData = default;

        if ((uint)ordinal >= (uint)serialTypes.Length)
            return false;

        int offset = bodyOffset;
        for (int i = 0; i < ordinal; i++)
            offset += SerialTypeCodec.GetContentSize(serialTypes[i]);

        serialType = serialTypes[ordinal];
        int size = SerialTypeCodec.GetContentSize(serialType);
        if (offset + size > payload.Length)
            return false;

        columnData = payload.Slice(offset, size);
        return true;
    }

    private static bool MatchEq(double value, double target, out bool pastRange)
    {
        if (value == target)
        {
            pastRange = false;
            return true;
        }

        pastRange = value > target;
        return false;
    }

    private static bool MatchLt(double value, double target, out bool pastRange)
    {
        if (value < target)
        {
            pastRange = false;
            return true;
        }

        pastRange = true;
        return false;
    }

    private static bool MatchLte(double value, double target, out bool pastRange)
    {
        if (value <= target)
        {
            pastRange = false;
            return true;
        }

        pastRange = true;
        return false;
    }

    private static bool MatchBetween(double value, double lower, double upper, out bool pastRange)
    {
        if (value < lower)
        {
            pastRange = false;
            return false;
        }

        if (value > upper)
        {
            pastRange = true;
            return false;
        }

        pastRange = false;
        return true;
    }

    private static bool MatchesInt64Constraint(long value, Intent.IntentOp op, long lower, long upper)
    {
        return op switch
        {
            Intent.IntentOp.Eq => value == lower,
            Intent.IntentOp.Gt => value > lower,
            Intent.IntentOp.Gte => value >= lower,
            Intent.IntentOp.Lt => value < lower,
            Intent.IntentOp.Lte => value <= lower,
            Intent.IntentOp.Between => value >= lower && value <= upper,
            _ => false
        };
    }

    private static bool MatchesDoubleConstraint(double value, Intent.IntentOp op, double lower, double upper)
    {
        return op switch
        {
            Intent.IntentOp.Eq => value == lower,
            Intent.IntentOp.Gt => value > lower,
            Intent.IntentOp.Gte => value >= lower,
            Intent.IntentOp.Lt => value < lower,
            Intent.IntentOp.Lte => value <= lower,
            Intent.IntentOp.Between => value >= lower && value <= upper,
            _ => false
        };
    }

    /// <inheritdoc />
    public void Reset()
    {
        _indexCursor.Reset();
        _tableCursor.Reset();
        _indexEntriesScanned = 0;
        _indexHits = 0;
        _seekDone = false;
        _exhausted = false;
    }

    /// <inheritdoc />
    public bool MoveLast()
    {
        return _tableCursor.MoveLast();
    }

    /// <inheritdoc />
    public bool Seek(long rowId)
    {
        return _tableCursor.Seek(rowId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ArrayPool<long>.Shared.Return(_indexSerials);
        _indexCursor.Dispose();
        _tableCursor.Dispose();
    }
}
