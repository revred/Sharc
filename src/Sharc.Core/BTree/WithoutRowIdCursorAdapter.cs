// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Core.BTree;

/// <summary>
/// Adapts an <see cref="IIndexBTreeCursor"/> to <see cref="IBTreeCursor"/> for WITHOUT ROWID tables.
/// WITHOUT ROWID tables store records directly in an index B-tree with no separate rowid.
/// RowId is synthesized from the INTEGER PRIMARY KEY column or a monotonic counter.
/// </summary>
internal sealed class WithoutRowIdCursorAdapter : IBTreeCursor
{
    private readonly IIndexBTreeCursor _inner;
    private readonly IRecordDecoder _recordDecoder;
    private readonly int _integerPkOrdinal;
    private long _syntheticRowId;

    /// <summary>
    /// Creates a new adapter wrapping an index cursor for WITHOUT ROWID table access.
    /// </summary>
    /// <param name="inner">The index B-tree cursor to wrap.</param>
    /// <param name="recordDecoder">Record decoder for extracting PK values.</param>
    /// <param name="integerPkOrdinal">Ordinal of the INTEGER PRIMARY KEY column, or -1 if none.</param>
    public WithoutRowIdCursorAdapter(IIndexBTreeCursor inner, IRecordDecoder recordDecoder,
        int integerPkOrdinal = -1)
    {
        _inner = inner;
        _recordDecoder = recordDecoder;
        _integerPkOrdinal = integerPkOrdinal;
    }

    /// <inheritdoc />
    public long RowId => _syntheticRowId;

    /// <inheritdoc />
    public int PayloadSize => _inner.PayloadSize;

    /// <inheritdoc />
    public ReadOnlySpan<byte> Payload => _inner.Payload;

    /// <inheritdoc />
    public bool IsStale => _inner.IsStale;

    /// <inheritdoc />
    public void Reset()
    {
        _inner.Reset();
        _syntheticRowId = 0;
    }

    /// <inheritdoc />
    public bool MoveNext()
    {
        if (!_inner.MoveNext())
            return false;

        if (_integerPkOrdinal >= 0)
        {
            var pkValue = _recordDecoder.DecodeColumn(_inner.Payload, _integerPkOrdinal);
            _syntheticRowId = pkValue.IsNull ? ++_syntheticRowId : pkValue.AsInt64();
        }
        else
        {
            _syntheticRowId++;
        }

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not supported for WITHOUT ROWID tables — the index B-tree is ordered by PRIMARY KEY,
    /// not by insertion order, so "last" has no meaningful rowid semantics.
    /// </remarks>
    public bool MoveLast()
    {
        throw new NotSupportedException("MoveLast is not supported for WITHOUT ROWID tables.");
    }

    /// <summary>
    /// Seek by rowid is not supported for WITHOUT ROWID tables.
    /// The B-tree is ordered by PRIMARY KEY, not by rowid.
    /// </summary>
    public bool Seek(long rowId)
    {
        // DECISION: Seek by rowid is not meaningful for WITHOUT ROWID tables.
        // The B-tree is ordered by PRIMARY KEY columns, not by a synthetic rowid.
        throw new NotSupportedException(
            "Seek by rowid is not supported for WITHOUT ROWID tables.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inner.Dispose();
    }
}