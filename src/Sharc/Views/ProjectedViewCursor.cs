// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Views;

/// <summary>
/// Zero-allocation view cursor that wraps an <see cref="IViewCursor"/>
/// with optional column ordinal remapping via an <c>int[]</c> projection.
/// Used for subview composition — view-on-view chains where the inner
/// cursor is another view (not a <see cref="SharcDataReader"/>).
/// <para>
/// Memory contract: after construction, <see cref="MoveNext"/> and all
/// <see cref="IRowAccessor"/> methods allocate <b>ZERO</b> bytes per row.
/// Column remapping is a single array index lookup — O(1), no dictionary, no hash.
/// </para>
/// </summary>
internal sealed class ProjectedViewCursor : IViewCursor
{
    private readonly IViewCursor _inner;
    private readonly int[]? _projection; // subviewOrdinal → parentOrdinal
    private readonly string[] _columnNames;
    private long _rowsRead;

    internal ProjectedViewCursor(IViewCursor inner, int[]? projection, string[] columnNames)
    {
        _inner = inner;
        _projection = projection;
        _columnNames = columnNames;
    }

    /// <inheritdoc />
    public int FieldCount => _columnNames.Length;

    /// <inheritdoc />
    public long RowsRead => _rowsRead;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_inner.MoveNext())
        {
            _rowsRead++;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.GetInt64(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.GetDouble(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.GetString(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetBlob(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.GetBlob(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNull(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.IsNull(mapped);
    }

    /// <inheritdoc />
    public string GetColumnName(int ordinal) => _columnNames[ordinal];

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharcColumnType GetColumnType(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _inner.GetColumnType(mapped);
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
