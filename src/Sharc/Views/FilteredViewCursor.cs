// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Views;

/// <summary>
/// Decorator cursor that applies a predicate filter to an inner <see cref="IViewCursor"/>.
/// <para>
/// Memory contract: <see cref="MoveNext"/> allocates <b>ZERO</b> bytes per row.
/// The predicate is a <c>Func&lt;IRowAccessor, bool&gt;</c> — no boxing.
/// All <see cref="IRowAccessor"/> methods delegate to the inner cursor.
/// </para>
/// </summary>
internal sealed class FilteredViewCursor : IViewCursor
{
    private readonly IViewCursor _inner;
    private readonly Func<IRowAccessor, bool> _predicate;
    private long _rowsRead;

    internal FilteredViewCursor(IViewCursor inner, Func<IRowAccessor, bool> predicate)
    {
        _inner = inner;
        _predicate = predicate;
    }

    /// <inheritdoc />
    public int FieldCount => _inner.FieldCount;

    /// <inheritdoc />
    public long RowsRead => _rowsRead;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (_inner.MoveNext())
        {
            if (_predicate(_inner))
            {
                _rowsRead++;
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int ordinal) => _inner.GetDouble(ordinal);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int ordinal) => _inner.GetString(ordinal);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetBlob(int ordinal) => _inner.GetBlob(ordinal);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNull(int ordinal) => _inner.IsNull(ordinal);

    /// <inheritdoc />
    public string GetColumnName(int ordinal) => _inner.GetColumnName(ordinal);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharcColumnType GetColumnType(int ordinal) => _inner.GetColumnType(ordinal);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
