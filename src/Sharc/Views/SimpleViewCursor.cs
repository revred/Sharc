// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Sharc.Views;

/// <summary>
/// Zero-allocation view cursor that wraps a <see cref="SharcDataReader"/>
/// with optional column ordinal remapping via an <c>int[]</c> projection.
/// <para>
/// Memory contract: after construction, <see cref="MoveNext"/> and all
/// <see cref="IRowAccessor"/> methods allocate <b>ZERO</b> bytes per row.
/// Column remapping is a single array index lookup — O(1), no dictionary, no hash.
/// </para>
/// </summary>
internal sealed class SimpleViewCursor : IViewCursor
{
    private readonly SharcDataReader _reader;
    private readonly int[]? _projection; // viewOrdinal → readerOrdinal
    private readonly string[] _columnNames;
    private long _rowsRead;

    internal SimpleViewCursor(SharcDataReader reader, int[]? projection, string[] columnNames)
    {
        _reader = reader;
        _projection = projection;
        _columnNames = columnNames;
    }

    /// <inheritdoc />
    public int FieldCount => _projection?.Length ?? _reader.FieldCount;

    /// <inheritdoc />
    public long RowsRead => _rowsRead;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_reader.Read())
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
        return _reader.GetInt64(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.GetDouble(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.GetString(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetBlob(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.GetBlob(mapped);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNull(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.IsNull(mapped);
    }

    /// <inheritdoc />
    public string GetColumnName(int ordinal) => _columnNames[ordinal];

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharcColumnType GetColumnType(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.GetColumnType(mapped);
    }

    /// <inheritdoc />
    public void Dispose() => _reader.Dispose();
}
