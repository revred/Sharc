/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
