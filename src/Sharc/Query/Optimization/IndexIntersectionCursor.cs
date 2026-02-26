// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Text;
using Sharc.Core;

namespace Sharc.Query.Optimization;

/// <summary>
/// Intersects rowids from two index scans, then materializes matching table rows.
/// </summary>
internal sealed class IndexIntersectionCursor : IBTreeCursor, IIndexCursorDiagnostics
{
    private const int SerialBufferSize = 64;

    private readonly IIndexBTreeCursor _leftIndexCursor;
    private readonly IIndexBTreeCursor _rightIndexCursor;
    private readonly IBTreeCursor _tableCursor;
    private readonly IRecordDecoder _decoder;
    private readonly IndexSeekPlan _leftPlan;
    private readonly IndexSeekPlan _rightPlan;
    private readonly byte[]? _leftTextKeyUtf8;
    private readonly byte[]? _rightTextKeyUtf8;
    private readonly long[] _serials;

    private List<long>? _matchedRowIds;
    private int _nextMatchIndex;
    private bool _prepared;
    private bool _disposed;
    private int _indexEntriesScanned;
    private int _indexHits;

    public IndexIntersectionCursor(
        IBTreeReader bTreeReader,
        IRecordDecoder decoder,
        uint tableRootPage,
        IndexSeekPlan leftPlan,
        IndexSeekPlan rightPlan)
    {
        if (leftPlan.Index == null)
            throw new ArgumentException("Left plan must contain an index.", nameof(leftPlan));
        if (rightPlan.Index == null)
            throw new ArgumentException("Right plan must contain an index.", nameof(rightPlan));

        _leftPlan = leftPlan;
        _rightPlan = rightPlan;
        _decoder = decoder;
        _leftIndexCursor = bTreeReader.CreateIndexCursor((uint)leftPlan.Index.RootPage);
        _rightIndexCursor = bTreeReader.CreateIndexCursor((uint)rightPlan.Index.RootPage);
        _tableCursor = bTreeReader.CreateCursor(tableRootPage);
        _serials = ArrayPool<long>.Shared.Rent(SerialBufferSize);

        if (leftPlan.IsTextKey && leftPlan.TextValue != null)
            _leftTextKeyUtf8 = Encoding.UTF8.GetBytes(leftPlan.TextValue);
        if (rightPlan.IsTextKey && rightPlan.TextValue != null)
            _rightTextKeyUtf8 = Encoding.UTF8.GetBytes(rightPlan.TextValue);
    }

    /// <inheritdoc />
    public long RowId => _tableCursor.RowId;

    /// <inheritdoc />
    public ReadOnlySpan<byte> Payload => _tableCursor.Payload;

    /// <inheritdoc />
    public int PayloadSize => _tableCursor.PayloadSize;

    /// <inheritdoc />
    public bool IsStale => _tableCursor.IsStale || _leftIndexCursor.IsStale || _rightIndexCursor.IsStale;

    /// <inheritdoc />
    public int IndexEntriesScanned => _indexEntriesScanned;

    /// <inheritdoc />
    public int IndexHits => _indexHits;

    /// <inheritdoc />
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_prepared)
        {
            _prepared = true;
            _matchedRowIds = BuildIntersection();
            _nextMatchIndex = 0;
        }

        if (_matchedRowIds == null || _nextMatchIndex >= _matchedRowIds.Count)
            return false;

        while (_nextMatchIndex < _matchedRowIds.Count)
        {
            long rowId = _matchedRowIds[_nextMatchIndex++];
            if (_tableCursor.Seek(rowId))
                return true;
        }

        return false;
    }

    private List<long> BuildIntersection()
    {
        var result = new List<long>();

        if (!IndexSeekCursor.TryPositionAtStart(_leftIndexCursor, _leftPlan, _leftTextKeyUtf8))
            return result;

        var leftRowIds = CollectMatches(_leftIndexCursor, _leftPlan, _leftTextKeyUtf8);
        if (leftRowIds.Count == 0)
            return result;

        if (!IndexSeekCursor.TryPositionAtStart(_rightIndexCursor, _rightPlan, _rightTextKeyUtf8))
            return result;

        var emitted = new HashSet<long>();
        do
        {
            var payload = _rightIndexCursor.Payload;
            if (payload.IsEmpty)
                break;

            _indexEntriesScanned++;

            if (IndexSeekCursor.TryMatchEntry(
                _rightPlan,
                _rightTextKeyUtf8,
                _decoder,
                _serials,
                payload,
                out long rowId,
                out bool pastRange))
            {
                _indexHits++;
                if (leftRowIds.Contains(rowId) && emitted.Add(rowId))
                    result.Add(rowId);
            }
            else if (pastRange)
            {
                break;
            }
        }
        while (_rightIndexCursor.MoveNext());

        return result;
    }

    private HashSet<long> CollectMatches(
        IIndexBTreeCursor cursor,
        in IndexSeekPlan plan,
        byte[]? textKeyUtf8)
    {
        var rowIds = new HashSet<long>();

        do
        {
            var payload = cursor.Payload;
            if (payload.IsEmpty)
                break;

            _indexEntriesScanned++;

            if (IndexSeekCursor.TryMatchEntry(
                plan,
                textKeyUtf8,
                _decoder,
                _serials,
                payload,
                out long rowId,
                out bool pastRange))
            {
                _indexHits++;
                rowIds.Add(rowId);
            }
            else if (pastRange)
            {
                break;
            }
        }
        while (cursor.MoveNext());

        return rowIds;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _leftIndexCursor.Reset();
        _rightIndexCursor.Reset();
        _tableCursor.Reset();
        _indexEntriesScanned = 0;
        _indexHits = 0;
        _matchedRowIds = null;
        _nextMatchIndex = 0;
        _prepared = false;
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
        ArrayPool<long>.Shared.Return(_serials);
        _leftIndexCursor.Dispose();
        _rightIndexCursor.Dispose();
        _tableCursor.Dispose();
    }
}
