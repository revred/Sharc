// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Text;
using Sharc.Core;

namespace Sharc.Query.Optimization;

/// <summary>
/// An <see cref="IBTreeCursor"/> adapter that wraps an <see cref="IIndexBTreeCursor"/>
/// and an <see cref="IBTreeCursor"/> (table cursor) to provide index-accelerated reads.
/// Supports both integer and text key seeks.
/// Pattern follows <c>IndexEdgeCursor</c> from the graph layer.
/// </summary>
internal sealed class IndexSeekCursor : IBTreeCursor
{
    private readonly IIndexBTreeCursor _indexCursor;
    private readonly IBTreeCursor _tableCursor;
    private readonly IRecordDecoder _decoder;
    private readonly IndexSeekPlan _plan;
    private readonly long[] _indexSerials;
    private readonly byte[]? _textKeyUtf8;
    private bool _seekDone;
    private bool _exhausted;
    private bool _disposed;

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
        _indexSerials = ArrayPool<long>.Shared.Rent(16);

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
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_exhausted)
            return false;

        // First call: seek into the index
        if (!_seekDone)
        {
            _seekDone = true;
            bool found;

            if (_plan.IsTextKey && _textKeyUtf8 != null)
                found = _indexCursor.SeekFirst(_textKeyUtf8);
            else
                found = _indexCursor.SeekFirst(_plan.SeekKey);

            if (!found && _plan.SeekOp == Intent.IntentOp.Eq)
            {
                _exhausted = true;
                return false;
            }

            return TryResolveCurrentEntry();
        }

        // Subsequent calls: advance the index cursor
        if (!_indexCursor.MoveNext())
        {
            _exhausted = true;
            return false;
        }

        return TryResolveCurrentEntry();
    }

    private bool TryResolveCurrentEntry()
    {
        return (_plan.IsTextKey && _textKeyUtf8 != null)
            ? ResolveTextEntries()
            : ResolveIntegerEntries();
    }

    private bool ResolveTextEntries()
    {
        var textKey = _textKeyUtf8!;
        do
        {
            var payload = _indexCursor.Payload;
            if (payload.IsEmpty)
                break;

            int colCount = _decoder.ReadSerialTypes(payload, _indexSerials, out int bodyOffset);

            long serialType = _indexSerials[0];
            // Non-text serial type (NULL, integer, blob) -> past matching entries
            if (serialType < 13 || (serialType & 1) == 0)
                break;

            int textLen = (int)(serialType - 13) / 2;
            if (!payload.Slice(bodyOffset, textLen).SequenceEqual(textKey))
                break;

            // Rowid is always the last column in an index record
            long rowId = _decoder.DecodeInt64Direct(payload, colCount - 1, _indexSerials, bodyOffset);
            if (_tableCursor.Seek(rowId))
                return true;
        }
        while (_indexCursor.MoveNext());

        _exhausted = true;
        return false;
    }

    private bool ResolveIntegerEntries()
    {
        do
        {
            var payload = _indexCursor.Payload;
            if (payload.IsEmpty)
                break;

            int colCount = _decoder.ReadSerialTypes(payload, _indexSerials, out int bodyOffset);
            long firstColValue = _decoder.DecodeInt64Direct(payload, 0, _indexSerials, bodyOffset);

            if (!IsWithinRange(firstColValue))
                break;

            // For Eq, skip entries where key doesn't match exactly
            if (_plan.SeekOp == Intent.IntentOp.Eq && firstColValue != _plan.SeekKey)
            {
                if (firstColValue > _plan.SeekKey)
                    break;
                continue;
            }

            long rowId = _decoder.DecodeInt64Direct(payload, colCount - 1, _indexSerials, bodyOffset);
            if (_tableCursor.Seek(rowId))
                return true;
        }
        while (_indexCursor.MoveNext());

        _exhausted = true;
        return false;
    }

    private bool IsWithinRange(long value)
    {
        return _plan.SeekOp switch
        {
            Intent.IntentOp.Eq => value == _plan.SeekKey,
            Intent.IntentOp.Gt => true,
            Intent.IntentOp.Gte => true,
            Intent.IntentOp.Lt => value < _plan.SeekKey,
            Intent.IntentOp.Lte => value <= _plan.SeekKey,
            Intent.IntentOp.Between => value <= _plan.UpperBound,
            _ => false
        };
    }

    /// <inheritdoc />
    public void Reset()
    {
        _indexCursor.Reset();
        _tableCursor.Reset();
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
        if (_disposed) return;
        _disposed = true;
        ArrayPool<long>.Shared.Return(_indexSerials);
        _indexCursor.Dispose();
        _tableCursor.Dispose();
    }
}
