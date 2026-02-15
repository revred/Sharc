// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;
using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.Core.Schema;
using Sharc.Core.Query;

namespace Sharc;

/// <summary>
/// Forward-only reader for iterating over table rows.
/// Designed for low-allocation sequential access to SQLite records.
/// </summary>
/// <remarks>
/// Usage pattern:
/// <code>
/// using var reader = db.CreateReader("users");
/// while (reader.Read())
/// {
///     long id = reader.GetInt64(0);
///     string name = reader.GetString(1);
/// }
/// </code>
/// </remarks>
public sealed class SharcDataReader : IDisposable
{
    private readonly IBTreeCursor? _cursor;
    private readonly IRecordDecoder? _recordDecoder;
    private readonly IReadOnlyList<ColumnInfo>? _columns;
    private readonly int[]? _projection;
    private readonly int _rowidAliasOrdinal;
    private readonly int _columnCount;
    private ColumnValue[]? _currentRow;
    private ColumnValue[]? _reusableBuffer;
    private bool _disposed;

    // Index seek support
    private readonly IBTreeReader? _bTreeReader;
    private readonly IReadOnlyList<IndexInfo>? _tableIndexes;

    // Row-level filter support (legacy path)
    private readonly ResolvedFilter[]? _filters;

    // Byte-level filter support (FilterStar path)
    private readonly IFilterNode? _filterNode;
    private readonly FilterNode? _concreteFilterNode;
    private readonly long[]? _filterSerialTypes;
    private int _filterBodyOffset;
    private int _filterColCount;

    // Lazy decode support for projection path — avoids decoding TEXT/BLOB
    // body data until the caller actually requests the value.
    private readonly long[]? _serialTypes;
    private readonly int[]? _decodedGenerations;
    private int _decodedGeneration;
    private bool _lazyMode;
    private int _currentBodyOffset;

    private readonly string[]? _materializedColumnNames;
    private int _materializedIndex = -1;

    // Cached column names — built once on first call to GetColumnNames()
    private string[]? _cachedColumnNames;

    // Unboxed materialized mode — QueryValue stores int/double inline without boxing
    private readonly Query.QueryValue[][]? _queryValueRows;

    // Concatenating mode — streams two readers sequentially for UNION ALL
    private readonly SharcDataReader? _concatFirst;
    private readonly SharcDataReader? _concatSecond;
    private bool _concatOnSecond;

    internal SharcDataReader(IBTreeCursor cursor, IRecordDecoder recordDecoder,
        IReadOnlyList<ColumnInfo> columns, int[]? projection,
        IBTreeReader? bTreeReader = null, IReadOnlyList<IndexInfo>? tableIndexes = null,
        ResolvedFilter[]? filters = null, IFilterNode? filterNode = null)
    {
        _cursor = cursor;
        _recordDecoder = recordDecoder;
        _columns = columns;
        _projection = projection;
        _bTreeReader = bTreeReader;
        _tableIndexes = tableIndexes;
        _filters = filters;
        _filters = filters;
        _filterNode = filterNode;
        _concreteFilterNode = filterNode as FilterNode;
        _columnCount = columns.Count;

        // Rent a reusable buffer from ArrayPool — returned in Dispose()
        _reusableBuffer = ArrayPool<ColumnValue>.Shared.Rent(columns.Count);

        // Pool lazy-decode buffers when using projection
        if (projection != null)
        {
            _serialTypes = ArrayPool<long>.Shared.Rent(columns.Count);
            _decodedGenerations = ArrayPool<int>.Shared.Rent(columns.Count);
            _serialTypes.AsSpan(0, columns.Count).Clear();
            _decodedGenerations.AsSpan(0, columns.Count).Clear();
        }

        // Pool serial type buffer for byte-level filter evaluation
        if (filterNode != null)
        {
            _filterSerialTypes = ArrayPool<long>.Shared.Rent(columns.Count);
            _filterSerialTypes.AsSpan(0, columns.Count).Clear();
        }

        // Detect INTEGER PRIMARY KEY (rowid alias) â€” SQLite stores NULL in the record
        // for this column; the real value is the b-tree key (rowid).
        _rowidAliasOrdinal = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].IsPrimaryKey &&
                columns[i].DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                _rowidAliasOrdinal = columns[i].Ordinal;
                break;
            }
        }
    }

    /// <summary>
    /// Creates an unboxed materialized reader from <see cref="Query.QueryValue"/> rows.
    /// Typed accessors (GetInt64, GetDouble, GetString) read values without boxing.
    /// Boxing only occurs when the caller invokes <see cref="GetValue(int)"/>.
    /// </summary>
    internal SharcDataReader(Query.QueryValue[][] rows, string[] columnNames)
    {
        _queryValueRows = rows;
        _materializedColumnNames = columnNames;
        _columnCount = columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Creates a concatenating reader that streams from <paramref name="first"/> then
    /// <paramref name="second"/>. Used for zero-materialization UNION ALL.
    /// </summary>
    internal SharcDataReader(SharcDataReader first, SharcDataReader second, string[] columnNames)
    {
        _concatFirst = first;
        _concatSecond = second;
        _materializedColumnNames = columnNames;
        _columnCount = columnNames.Length;
        _rowidAliasOrdinal = -1;
    }

    /// <summary>
    /// Gets the number of columns in the current result set.
    /// </summary>
    public int FieldCount => _materializedColumnNames?.Length
        ?? _projection?.Length
        ?? _columns!.Count;

    /// <summary>Returns true when the reader is in unboxed <see cref="Query.QueryValue"/> mode.</summary>
    private bool IsQueryValueMode => _queryValueRows != null;

    /// <summary>Returns true when the reader is in concatenating mode (streaming UNION ALL).</summary>
    private bool IsConcatMode => _concatFirst != null;

    /// <summary>Gets the currently active reader in concatenating mode.</summary>
    private SharcDataReader ActiveConcatReader => _concatOnSecond ? _concatSecond! : _concatFirst!;

    /// <summary>
    /// Gets the rowid of the current row.
    /// </summary>
    public long RowId => _cursor?.RowId ?? 0;

    /// <summary>
    /// Seeks directly to the row with the specified rowid using B-tree binary search.
    /// This is dramatically faster than sequential scan for point lookups.
    /// After a successful seek, use typed accessors (GetInt64, GetString, etc.) to read values.
    /// After Seek, you can call Read() to continue sequential iteration from the seek position.
    /// </summary>
    /// <param name="rowId">The rowid to seek to.</param>
    /// <returns>True if an exact match was found; false otherwise.</returns>
    public bool Seek(long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool found = _cursor!.Seek(rowId);

        if (found)
        {
            DecodeCurrentRow();
        }
        else
        {
            _currentRow = null;
            _lazyMode = false;
        }

        return found;
    }

    /// <summary>
    /// Seeks to the first row matching the given index key values.
    /// Scans the specified index B-tree for matching entries, extracts the table rowid,
    /// then seeks the table cursor to that row.
    /// </summary>
    /// <param name="indexName">Name of the index to use.</param>
    /// <param name="keyValues">Key values to match against the index columns (in index column order).</param>
    /// <returns>True if a matching row was found; false otherwise.</returns>
    /// <exception cref="ArgumentException">The index was not found or the reader was not created with index support.</exception>
    public bool SeekIndex(string indexName, params object[] keyValues)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_bTreeReader == null || _tableIndexes == null)
            throw new ArgumentException("Reader was not created with index support.");

        var indexInfo = _tableIndexes.FirstOrDefault(i =>
            i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Index '{indexName}' not found.");

        using var indexCursor = _bTreeReader.CreateIndexCursor((uint)indexInfo.RootPage);

        // Determine sort direction for the first key column to enable early exit.
        // Index entries are stored in sorted order, so once we've passed the target
        // value we can stop scanning instead of reading the entire index.
        bool firstColumnDescending = indexInfo.Columns.Count > 0 && indexInfo.Columns[0].IsDescending;

        while (indexCursor.MoveNext())
        {
            var indexRecord = _recordDecoder!.DecodeRecord(indexCursor.Payload);
            if (indexRecord.Length < 2) continue; // Need at least one key column + rowid

            // Compare key values against the first N columns of the index record
            bool match = true;
            for (int i = 0; i < keyValues.Length && i < indexRecord.Length - 1; i++)
            {
                if (!IndexKeyMatches(indexRecord[i], keyValues[i]))
                {
                    match = false;

                    // Early exit: if the first key column is past the target value,
                    // no subsequent entries can match (B-tree sorted order).
                    if (i == 0 && IndexKeyIsPastTarget(indexRecord[0], keyValues[0], firstColumnDescending))
                    {
                        _currentRow = null;
                        _lazyMode = false;
                        return false;
                    }

                    break;
                }
            }

            if (!match) continue;

            // Last column in the index record is the table rowid
            long rowId = indexRecord[^1].AsInt64();
            return Seek(rowId);
        }

        _currentRow = null;
        _lazyMode = false;
        return false;
    }

    private static bool IndexKeyMatches(ColumnValue indexValue, object keyValue)
    {
        return keyValue switch
        {
            long l => !indexValue.IsNull && indexValue.AsInt64() == l,
            int i => !indexValue.IsNull && indexValue.AsInt64() == i,
            string s => !indexValue.IsNull && indexValue.AsString().Equals(s, StringComparison.Ordinal),
            double d => !indexValue.IsNull && indexValue.AsDouble() == d,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if the index entry's key value is past the target in sort order,
    /// meaning no further entries can match. NULLs sort first in SQLite (smallest),
    /// so a NULL index value is never "past" the target.
    /// </summary>
    private static bool IndexKeyIsPastTarget(ColumnValue indexValue, object targetValue, bool isDescending)
    {
        if (indexValue.IsNull) return false;

        int cmp = targetValue switch
        {
            long l => indexValue.AsInt64().CompareTo(l),
            int i => indexValue.AsInt64().CompareTo((long)i),
            string s => string.Compare(indexValue.AsString(), s, StringComparison.Ordinal),
            double d => indexValue.AsDouble().CompareTo(d),
            _ => 0
        };

        // For ASC: if index value > target, we've passed it
        // For DESC: if index value < target, we've passed it
        return isDescending ? cmp < 0 : cmp > 0;
    }

    /// <summary>
    /// Advances the reader to the next row.
    /// </summary>
    /// <returns>True if there is another row; false if the end has been reached.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Concatenating mode: stream from first reader, then second
        if (_concatFirst != null)
        {
            if (!_concatOnSecond)
            {
                if (_concatFirst.Read()) return true;
                _concatOnSecond = true;
            }
            return _concatSecond!.Read();
        }

        // Unboxed materialized mode: iterate QueryValue rows
        if (_queryValueRows != null)
        {
            _materializedIndex++;
            return _materializedIndex < _queryValueRows.Length;
        }

        while (_cursor!.MoveNext())
        {
            // ── FilterStar byte-level path: evaluate raw record before decoding ──
            if (_filterNode != null)
            {
                _filterColCount = _recordDecoder!.ReadSerialTypes(_cursor!.Payload, _filterSerialTypes!, out _filterBodyOffset);
                int stCount = Math.Min(_filterColCount, _filterSerialTypes!.Length);

                if (_concreteFilterNode != null)
                {
                    if (!_concreteFilterNode.Evaluate(_cursor.Payload,
                        _filterSerialTypes.AsSpan(0, stCount), _filterBodyOffset, _cursor.RowId))
                        continue;
                }
                else if (!_filterNode.Evaluate(_cursor.Payload,
                    _filterSerialTypes.AsSpan(0, stCount), _filterBodyOffset, _cursor.RowId))
                    continue;

                DecodeCurrentRow();
                return true;
            }

            // ── Zero-Allocation Filter Check ──
            // Evaluate filters against raw serial types/bytes before allocating managed objects.
            // Pushdown to RecordDecoder for zero-allocation check
            if (_filters != null && _filters.Length > 0 &&
                !_recordDecoder!.Matches(_cursor!.Payload, _filters))
            {
                continue; // Skip row without decoding
            }

            // ── Legacy SharcFilter path ──
            DecodeCurrentRow();

            return true;
        }

        _currentRow = null;
        _lazyMode = false;
        return false;
    }

    private bool EvaluateFilters()
    {
        // Filters require full column values. When in lazy mode (projection),
        // force a full decode so filter columns are available.
        if (_lazyMode)
        {
            _recordDecoder!.DecodeRecord(_cursor!.Payload, _reusableBuffer!);
            _lazyMode = false;
        }

        // Resolve INTEGER PRIMARY KEY alias — the record stores NULL,
        // but the real value is the b-tree rowid.
        if (_rowidAliasOrdinal >= 0 && _reusableBuffer![_rowidAliasOrdinal].IsNull)
        {
            _reusableBuffer[_rowidAliasOrdinal] =
                ColumnValue.FromInt64(4, _cursor!.RowId);
        }

        return FilterEvaluator.MatchesAll(_filters!, _reusableBuffer!);
    }

    private void DecodeCurrentRow()
    {
        if (_projection != null)
        {
            // PROJECTION PATH (Lazy Decode)
            // If we have a filter, we already parsed the serial types in Read().
            // Reuse them to avoid re-parsing the header.
            if (_filterNode != null)
            {
                // Copy filter serial types to projection serial types
                // We only need to copy up to the number of columns in the table (or found columns)
                int copyCount = Math.Min(_filterColCount, _serialTypes!.Length);
                Array.Copy(_filterSerialTypes!, _serialTypes, copyCount);

                // Cache the body offset for fast lazy decoding
                _currentBodyOffset = _filterBodyOffset;
            }
            else
            {
                // Lazy decode: read serial types (varint parsing, no body decode).
                _recordDecoder!.ReadSerialTypes(_cursor!.Payload, _serialTypes!, out _currentBodyOffset);
            }

            // Increment generation instead of Array.Clear — O(1) vs O(N)
            _decodedGeneration++;
            _lazyMode = true;
        }
        else
        {
            // FULL ROW PATH
            if (_filterNode != null && _filterSerialTypes != null)
            {
                // Optimization: Use the pre-parsed serial types from the filter step
                // to skip header parsing in RecordDecoder.
                _recordDecoder!.DecodeRecord(_cursor!.Payload, _reusableBuffer!,
                    _filterSerialTypes.AsSpan(0, Math.Min(_filterColCount, _filterSerialTypes.Length)),
                    _filterBodyOffset);
            }
            else
            {
                // Full decode — reuse the pre-allocated buffer
                _recordDecoder!.DecodeRecord(_cursor!.Payload, _reusableBuffer!);
            }
            _lazyMode = false;
        }

        _currentRow = _reusableBuffer;
    }

    /// <summary>
    /// Returns true if the column value is NULL.
    /// </summary>
    public bool IsNull(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.IsNull(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].IsNull;

        if (_currentRow == null)
            throw new InvalidOperationException("No current row. Call Read() first.");

        int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        // Fast path: in lazy mode, check serial type directly (no body decode needed)
        if (_lazyMode)
        {
            _recordDecoder!.ReadSerialTypes(_cursor!.Payload, _serialTypes!, out _);
            // INTEGER PRIMARY KEY stores NULL in record; real value is rowid â€” not actually null
            if (actualOrdinal == _rowidAliasOrdinal && _serialTypes![actualOrdinal] == 0)
                return false;
            return _serialTypes![actualOrdinal] == 0;
        }

        return GetColumnValue(ordinal).IsNull;
    }

    /// <summary>
    /// Gets a column value as a 64-bit signed integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetInt64(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetInt64(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].AsInt64();

        // Fast path: decode directly from page span (skip ColumnValue construction)
        if (_lazyMode)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            if (actualOrdinal == _rowidAliasOrdinal)
                return _cursor!.RowId;
            return _recordDecoder!.DecodeInt64Direct(_cursor!.Payload, actualOrdinal, _serialTypes!, _currentBodyOffset);
        }
        return GetColumnValue(ordinal).AsInt64();
    }

    /// <summary>
    /// Gets a column value as a 32-bit signed integer.
    /// </summary>
    public int GetInt32(int ordinal)
    {
        return (int)GetInt64(ordinal);
    }

    /// <summary>
    /// Gets a column value as a double-precision float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDouble(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetDouble(ordinal);

        if (_queryValueRows != null)
        {
            var qv = _queryValueRows[_materializedIndex][ordinal];
            return qv.Type == Query.QueryValueType.Int64 ? (double)qv.AsInt64() : qv.AsDouble();
        }

        // Fast path: decode directly from page span (skip ColumnValue construction)
        if (_lazyMode)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            return _recordDecoder!.DecodeDoubleDirect(_cursor!.Payload, actualOrdinal, _serialTypes!, _currentBodyOffset);
        }
        return GetColumnValue(ordinal).AsDouble();
    }

    /// <summary>
    /// Gets a column value as a UTF-8 string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetString(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetString(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].AsString();

        // Fast path: decode UTF-8 directly from page span (eliminates intermediate byte[] allocation)
        if (_lazyMode)
        {
            int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;
            return _recordDecoder!.DecodeStringDirect(_cursor!.Payload, actualOrdinal, _serialTypes!, _currentBodyOffset);
        }
        return GetColumnValue(ordinal).AsString();
    }

    /// <summary>
    /// Gets a column value as a byte array (BLOB).
    /// </summary>
    public byte[] GetBlob(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetBlob(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].AsBlob();

        return GetColumnValue(ordinal).AsBytes().ToArray();
    }

    /// <summary>
    /// Gets a column value as a read-only span of bytes (zero-copy for BLOBs).
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// </summary>
    public ReadOnlySpan<byte> GetBlobSpan(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetBlobSpan(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].AsBlob();

        return GetColumnValue(ordinal).AsBytes().Span;
    }

    /// <summary>
    /// Gets the column name at the specified ordinal.
    /// </summary>
    public string GetColumnName(int ordinal)
    {
        if (_materializedColumnNames != null)
            return _materializedColumnNames[ordinal];
        if (_projection != null)
            return _columns![_projection[ordinal]].Name;
        return _columns![ordinal].Name;
    }

    /// <summary>
    /// Returns all column names as an array. Cached after first call.
    /// Used internally by the query pipeline to avoid repeated allocations.
    /// </summary>
    internal string[] GetColumnNames()
    {
        if (_materializedColumnNames != null) return _materializedColumnNames;
        if (_cachedColumnNames != null) return _cachedColumnNames;
        int count = FieldCount;
        var names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = GetColumnName(i);
        _cachedColumnNames = names;
        return names;
    }

    /// <summary>
    /// Returns the ordinal of the column with the given name (case-insensitive).
    /// </summary>
    /// <param name="columnName">The column name to look up.</param>
    /// <returns>The zero-based column ordinal.</returns>
    /// <exception cref="ArgumentException">No column with the specified name exists.</exception>
    public int GetOrdinal(string columnName)
    {
        for (int i = 0; i < FieldCount; i++)
        {
            if (string.Equals(GetColumnName(i), columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new ArgumentException($"Column '{columnName}' not found.");
    }

    /// <summary>
    /// Gets the SQLite type affinity of the column value in the current row.
    /// </summary>
    public SharcColumnType GetColumnType(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetColumnType(ordinal);

        if (_queryValueRows != null)
        {
            return _queryValueRows[_materializedIndex][ordinal].Type switch
            {
                Query.QueryValueType.Null => SharcColumnType.Null,
                Query.QueryValueType.Int64 => SharcColumnType.Integral,
                Query.QueryValueType.Double => SharcColumnType.Real,
                Query.QueryValueType.Text => SharcColumnType.Text,
                Query.QueryValueType.Blob => SharcColumnType.Blob,
                _ => SharcColumnType.Null,
            };
        }

        var val = GetColumnValue(ordinal);
        return val.StorageClass switch
        {
            ColumnStorageClass.Null => SharcColumnType.Null,
            ColumnStorageClass.Integral => SharcColumnType.Integral,
            ColumnStorageClass.Real => SharcColumnType.Real,
            ColumnStorageClass.Text => SharcColumnType.Text,
            ColumnStorageClass.Blob => SharcColumnType.Blob,
            _ => SharcColumnType.Null
        };
    }

    /// <summary>
    /// Gets the column value as a boxed object. Returns DBNull.Value for NULL.
    /// Prefer typed accessors for zero-allocation reads.
    /// </summary>
    public object GetValue(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetValue(ordinal);

        if (_queryValueRows != null)
            return _queryValueRows[_materializedIndex][ordinal].ToObject();

        var val = GetColumnValue(ordinal);
        return val.StorageClass switch
        {
            ColumnStorageClass.Null => DBNull.Value,
            ColumnStorageClass.Integral => val.AsInt64(),
            ColumnStorageClass.Real => val.AsDouble(),
            ColumnStorageClass.Text => val.AsString(),
            ColumnStorageClass.Blob => val.AsBytes().ToArray(),
            _ => DBNull.Value
        };
    }

    private ColumnValue GetColumnValue(int ordinal)
    {
        if (_currentRow == null)
            throw new InvalidOperationException("No current row. Call Read() first.");

        int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        if (actualOrdinal < 0 || actualOrdinal >= _columnCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal,
                "Column ordinal is out of range.");

        // Lazy decode: decode this column on first access (projection path only)
        if (_lazyMode && _decodedGenerations![actualOrdinal] != _decodedGeneration)
        {
            _reusableBuffer![actualOrdinal] = _recordDecoder!.DecodeColumn(_cursor!.Payload, actualOrdinal, _serialTypes!, _currentBodyOffset);
            _decodedGenerations[actualOrdinal] = _decodedGeneration;
        }

        var value = _currentRow[actualOrdinal];

        // INTEGER PRIMARY KEY columns store NULL in the record; the real value is the rowid.
        if (actualOrdinal == _rowidAliasOrdinal && value.IsNull)
            return ColumnValue.FromInt64(1, _cursor!.RowId);

        return value;
    }

    /// <summary>
    /// Gets a column value as raw UTF-8 bytes without allocating a managed string.
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// For TEXT columns, this avoids the <see cref="System.Text.Encoding.UTF8"/> decode allocation.
    /// </summary>
    public ReadOnlySpan<byte> GetUtf8Span(int ordinal)
    {
        if (_concatFirst != null)
            return ActiveConcatReader.GetUtf8Span(ordinal);

        if (_queryValueRows != null)
            return System.Text.Encoding.UTF8.GetBytes(_queryValueRows[_materializedIndex][ordinal].AsString());

        return GetColumnValue(ordinal).AsBytes().Span;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _concatFirst?.Dispose();
        _concatSecond?.Dispose();
        _cursor?.Dispose();

        if (_reusableBuffer is not null)
        {
            ArrayPool<ColumnValue>.Shared.Return(_reusableBuffer, clearArray: true);
            _reusableBuffer = null;
        }

        if (_serialTypes is not null)
            ArrayPool<long>.Shared.Return(_serialTypes);
        if (_decodedGenerations is not null)
            ArrayPool<int>.Shared.Return(_decodedGenerations);
        if (_filterSerialTypes is not null)
            ArrayPool<long>.Shared.Return(_filterSerialTypes);
    }
}

/// <summary>
/// SQLite storage classes as exposed by Sharc.
/// </summary>
public enum SharcColumnType
{
    /// <summary>NULL value.</summary>
    Null = 0,

    /// <summary>Signed integer (1, 2, 3, 4, 6, or 8 bytes).</summary>
    Integral = 1,

    /// <summary>IEEE 754 64-bit float.</summary>
    Real = 2,

    /// <summary>UTF-8 text string.</summary>
    Text = 3,

    /// <summary>Binary large object.</summary>
    Blob = 4
}
