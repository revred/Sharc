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

    // Dedup streaming mode — wraps an underlying reader and filters rows by index
    private readonly SharcDataReader? _dedupUnderlying;
    private readonly SetDedupMode _dedupMode;
    private readonly IndexSet? _dedupRightIndex;
    private readonly IndexSet? _dedupSeen;

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

        // Always use lazy decode — parse serial type headers on Read(),
        // decode individual columns only when Get*() is called. This avoids
        // string allocation for columns that are never accessed (e.g. SELECT *
        // where the caller only reads column 0).
        _serialTypes = ArrayPool<long>.Shared.Rent(columns.Count);
        _decodedGenerations = ArrayPool<int>.Shared.Rent(columns.Count);
        _serialTypes.AsSpan(0, columns.Count).Clear();
        _decodedGenerations.AsSpan(0, columns.Count).Clear();

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
    /// Creates a dedup streaming reader that wraps an underlying reader and
    /// filters rows using 128-bit indexes (FNV-1a of raw cursor bytes).
    /// Used for zero-alloc UNION/INTERSECT/EXCEPT set operations.
    /// </summary>
    internal SharcDataReader(SharcDataReader underlying, SetDedupMode mode,
        IndexSet? rightIndex = null)
    {
        _dedupUnderlying = underlying;
        _dedupMode = mode;
        _dedupRightIndex = rightIndex;
        _dedupSeen = IndexSet.Rent();
        _materializedColumnNames = underlying.GetColumnNames();
        _columnCount = underlying.FieldCount;
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
    public long RowId => _dedupUnderlying?.RowId ?? _cursor?.RowId ?? 0;

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

        // Dedup streaming mode: filter rows by index
        if (_dedupUnderlying != null)
        {
            while (_dedupUnderlying.Read())
            {
                var fp = _dedupUnderlying.GetRowFingerprint();
                bool pass = _dedupMode switch
                {
                    SetDedupMode.Union => _dedupSeen!.Add(fp),
                    SetDedupMode.Intersect => _dedupRightIndex!.Contains(fp) && _dedupSeen!.Add(fp),
                    SetDedupMode.Except => !_dedupRightIndex!.Contains(fp) && _dedupSeen!.Add(fp),
                    _ => false,
                };
                if (pass) return true;
            }
            return false;
        }

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
        // Always lazy decode: parse serial type headers only.
        // Column values are decoded on demand by Get*() calls.
        // This avoids allocating strings for columns never accessed.
        if (_filterNode != null)
        {
            // Reuse the pre-parsed serial types from the filter step
            int copyCount = Math.Min(_filterColCount, _serialTypes!.Length);
            Array.Copy(_filterSerialTypes!, _serialTypes, copyCount);
            _currentBodyOffset = _filterBodyOffset;
        }
        else
        {
            _recordDecoder!.ReadSerialTypes(_cursor!.Payload, _serialTypes!, out _currentBodyOffset);
        }

        // Increment generation instead of Array.Clear — O(1) vs O(N)
        _decodedGeneration++;
        _lazyMode = true;
        _currentRow = _reusableBuffer;
    }

    /// <summary>
    /// Returns true if the column value is NULL.
    /// </summary>
    public bool IsNull(int ordinal)
    {
        if (_dedupUnderlying != null)
            return _dedupUnderlying.IsNull(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetInt64(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetDouble(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetString(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetBlob(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetBlobSpan(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetColumnType(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetValue(ordinal);
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
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetUtf8Span(ordinal);
        if (_concatFirst != null)
            return ActiveConcatReader.GetUtf8Span(ordinal);

        if (_queryValueRows != null)
            return System.Text.Encoding.UTF8.GetBytes(_queryValueRows[_materializedIndex][ordinal].AsString());

        return GetColumnValue(ordinal).AsBytes().Span;
    }

    // ─── Fingerprinting ──────────────────────────────────────────

    /// <summary>
    /// Computes a 64-bit FNV-1a fingerprint of the current row's projected columns
    /// from raw cursor payload bytes. Zero string allocation — hashes raw UTF-8 bytes
    /// directly from page spans. Used for set operation dedup (UNION/INTERSECT/EXCEPT).
    /// </summary>
    /// <remarks>
    /// Collision probability: for N rows, P ≈ N²/2⁶⁵. At 5000 rows: ~10⁻¹¹%.
    /// Stays below 0.0001% up to ~6 million rows.
    /// </remarks>
    internal Fingerprint128 GetRowFingerprint()
    {
        // Dedup mode: delegate to underlying
        if (_dedupUnderlying != null)
            return _dedupUnderlying.GetRowFingerprint();

        // Concat mode: delegate to active reader
        if (_concatFirst != null)
            return ActiveConcatReader.GetRowFingerprint();

        // Materialized mode: hash from QueryValue[]
        if (_queryValueRows != null)
            return GetMaterializedRowFingerprint();

        // Cursor mode: hash raw payload bytes (zero string allocation)
        return GetCursorRowFingerprint();
    }

    private Fingerprint128 GetCursorRowFingerprint()
    {
        var payload = _cursor!.Payload;

        if (_serialTypes != null && _lazyMode)
        {
            // Projection path: serial types already parsed by DecodeCurrentRow()
            return ComputeFingerprint(payload, _serialTypes.AsSpan(0, _columnCount), _currentBodyOffset);
        }

        // Non-projection path: parse serial types on the fly (stackalloc — zero alloc)
        Span<long> stackSt = stackalloc long[Math.Min(_columnCount, 64)];
        _recordDecoder!.ReadSerialTypes(payload, stackSt, out int bodyOffset);
        return ComputeFingerprint(payload, stackSt.Slice(0, Math.Min(_columnCount, stackSt.Length)), bodyOffset);
    }

    private Fingerprint128 ComputeFingerprint(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, int bodyOffset)
    {
        // Compute cumulative byte offsets for all physical columns (single pass)
        Span<int> offsets = stackalloc int[serialTypes.Length];
        int runningOffset = bodyOffset;
        for (int c = 0; c < serialTypes.Length; c++)
        {
            offsets[c] = runningOffset;
            runningOffset += SerialTypeCodec.GetContentSize(serialTypes[c]);
        }

        // Hash projected columns
        var hasher = new Fnv1aHasher();
        int projectedCount = _projection?.Length ?? _columnCount;

        for (int i = 0; i < projectedCount; i++)
        {
            int physOrdinal = _projection != null ? _projection[i] : i;

            // INTEGER PRIMARY KEY: hash rowid instead of the NULL stored in the record
            if (physOrdinal == _rowidAliasOrdinal)
            {
                hasher.AddTypeTag(i, 1); // 1 = integer
                hasher.AppendLong(_cursor!.RowId);
                continue;
            }

            if (physOrdinal >= serialTypes.Length) continue;

            long st = serialTypes[physOrdinal];
            int size = SerialTypeCodec.GetContentSize(st);

            // Type tag: 0=null, 1=int, 2=float, 3=text, 4=blob
            hasher.AddTypeTag(i, st == 0 ? (byte)0
                : st <= 6 ? (byte)1
                : st == 7 ? (byte)2
                : (st >= 13 && (st & 1) == 1) ? (byte)3
                : (byte)4);

            // Hash serial type (encodes type info) + column body bytes (encodes value)
            hasher.AppendLong(st);
            if (size > 0)
                hasher.Append(payload.Slice(offsets[physOrdinal], size));
        }

        return hasher.Hash;
    }

    private Fingerprint128 GetMaterializedRowFingerprint()
    {
        var row = _queryValueRows![_materializedIndex];
        var hasher = new Fnv1aHasher();
        for (int i = 0; i < _columnCount && i < row.Length; i++)
        {
            ref var val = ref row[i];
            switch (val.Type)
            {
                case Query.QueryValueType.Null:
                    hasher.AddTypeTag(i, 0);
                    hasher.AppendLong(0); break;
                case Query.QueryValueType.Int64:
                    hasher.AddTypeTag(i, 1);
                    hasher.AppendLong(val.AsInt64()); break;
                case Query.QueryValueType.Double:
                    hasher.AddTypeTag(i, 2);
                    hasher.AppendLong(BitConverter.DoubleToInt64Bits(val.AsDouble())); break;
                case Query.QueryValueType.Text:
                    hasher.AddTypeTag(i, 3);
                    hasher.Append(System.Text.Encoding.UTF8.GetBytes(val.AsString())); break;
                default:
                    hasher.AddTypeTag(i, 4);
                    hasher.AppendLong(val.ObjectValue?.GetHashCode() ?? 0); break;
            }
        }
        return hasher.Hash;
    }

    /// <summary>
    /// Computes a 64-bit FNV-1a fingerprint of a single column from the current row.
    /// Zero string allocation — hashes raw bytes directly from cursor payload.
    /// Used for group-key matching in streaming aggregation to avoid materializing
    /// text columns for rows in existing groups.
    /// </summary>
    internal Fingerprint128 GetColumnFingerprint(int ordinal)
    {
        if (_dedupUnderlying != null) return _dedupUnderlying.GetColumnFingerprint(ordinal);
        if (_concatFirst != null) return ActiveConcatReader.GetColumnFingerprint(ordinal);

        if (_queryValueRows != null)
        {
            ref var val = ref _queryValueRows![_materializedIndex][ordinal];
            var h = new Fnv1aHasher();
            switch (val.Type)
            {
                case Query.QueryValueType.Int64:
                    h.AddTypeTag(0, 1);
                    h.AppendLong(val.AsInt64()); break;
                case Query.QueryValueType.Double:
                    h.AddTypeTag(0, 2);
                    h.AppendLong(BitConverter.DoubleToInt64Bits(val.AsDouble())); break;
                case Query.QueryValueType.Text:
                    h.AddTypeTag(0, 3);
                    h.Append(System.Text.Encoding.UTF8.GetBytes(val.AsString())); break;
                default:
                    h.AddTypeTag(0, 0);
                    h.AppendLong(0); break;
            }
            return h.Hash;
        }

        // Cursor/lazy mode: hash raw column bytes without string allocation
        int physOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        if (physOrdinal == _rowidAliasOrdinal)
        {
            var h = new Fnv1aHasher();
            h.AddTypeTag(0, 1); // integer
            h.AppendLong(_cursor!.RowId);
            return h.Hash;
        }

        var payload = _cursor!.Payload;
        var stSpan = _lazyMode ? _serialTypes! : _filterSerialTypes!;
        int bodyOff = _currentBodyOffset;

        // Compute byte offset to this column's body
        int offset = bodyOff;
        for (int c = 0; c < physOrdinal; c++)
            offset += Core.Primitives.SerialTypeCodec.GetContentSize(stSpan[c]);

        long st = stSpan[physOrdinal];
        int size = Core.Primitives.SerialTypeCodec.GetContentSize(st);

        var hasher = new Fnv1aHasher();
        // Type tag for single-column fingerprint
        hasher.AddTypeTag(0, st == 0 ? (byte)0
            : st <= 6 ? (byte)1
            : st == 7 ? (byte)2
            : (st >= 13 && (st & 1) == 1) ? (byte)3
            : (byte)4);
        hasher.AppendLong(st);
        if (size > 0) hasher.Append(payload.Slice(offset, size));
        return hasher.Hash;
    }

    /// <summary>
    /// 128-bit FNV-1a hasher with metadata tracking. Zero allocation (ref struct, stack-only).
    /// Computes dual FNV-1a hashes (64-bit primary + 32-bit guard from second lane)
    /// while accumulating payload byte count and column type tags.
    /// Collision probability: P ≈ N²/2⁹⁷ ≈ 10⁻¹⁶ at 6M rows.
    /// </summary>
    internal ref struct Fnv1aHasher
    {
        private ulong _hashLo;
        private ulong _hashHi;
        private int _byteCount;
        private ushort _typeMask;
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        // Second seed: FNV offset XOR a large prime to ensure independence
        private const ulong FnvOffsetBasis2 = 0x6C62272E07BB0142UL;
        private const ulong FnvPrime2 = 0x100000001B3UL;

        public Fnv1aHasher()
        {
            _hashLo = FnvOffsetBasis;
            _hashHi = FnvOffsetBasis2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(ReadOnlySpan<byte> data)
        {
            _byteCount += data.Length;
            for (int i = 0; i < data.Length; i++)
            {
                _hashLo ^= data[i]; _hashLo *= FnvPrime;
                _hashHi ^= data[i]; _hashHi *= FnvPrime2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendLong(long value)
        {
            _byteCount += 8;
            ulong v = (ulong)value;
            for (int shift = 0; shift < 64; shift += 8)
            {
                ulong b = (v >> shift) & 0xFF;
                _hashLo ^= b; _hashLo *= FnvPrime;
                _hashHi ^= b; _hashHi *= FnvPrime2;
            }
        }

        /// <summary>
        /// Adds a column type tag to the structural signature.
        /// Uses rotating XOR over 16 bits — each column shifts left by (colIndex * 2) mod 16,
        /// so up to 8 columns get distinct bit positions for instant structural rejection.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTypeTag(int colIndex, byte typeId)
        {
            int shift = (colIndex * 2) & 0xF; // mod 16
            _typeMask ^= (ushort)(typeId << shift);
        }

        public readonly Fingerprint128 Hash =>
            new(_hashLo, (uint)_hashHi, (ushort)Math.Min(_byteCount, 65535), _typeMask);
    }

    /// <summary>
    /// 128-bit fingerprint with packed metadata. Every bit serves a purpose:
    /// <list type="bullet">
    ///   <item>Lo [64 bits] — Primary FNV-1a hash (bucket selection + primary comparison)</item>
    ///   <item>Guard [32 bits] — Secondary FNV-1a hash (96-bit total collision resistance)</item>
    ///   <item>PayloadLen [16 bits] — Total payload byte length (fast structural rejection)</item>
    ///   <item>TypeTag [16 bits] — Column type signature (structural fingerprint)</item>
    /// </list>
    /// Collision probability at 6M rows: P ≈ N²/2⁹⁷ ≈ 10⁻¹⁶. Practically collision-free.
    /// PayloadLen and TypeTag provide instant rejection for structurally different rows
    /// before the hash comparison is even reached.
    /// </summary>
    internal readonly struct Fingerprint128 : IEquatable<Fingerprint128>
    {
        public readonly ulong Lo;
        public readonly ulong Hi;

        /// <summary>Construct from raw lo/hi (used by Fnv1aHasher).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fingerprint128(ulong lo, uint guard, ushort payloadLen, ushort typeTag)
        {
            Lo = lo;
            Hi = ((ulong)guard << 32) | ((ulong)payloadLen << 16) | typeTag;
        }

        /// <summary>32-bit secondary hash guard for collision resistance.</summary>
        public uint Guard => (uint)(Hi >> 32);
        /// <summary>Total payload byte length — instant structural rejection.</summary>
        public ushort PayloadLen => (ushort)(Hi >> 16);
        /// <summary>Column type signature — structural fingerprint.</summary>
        public ushort TypeTag => (ushort)Hi;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Fingerprint128 other) => Lo == other.Lo && Hi == other.Hi;
        public override bool Equals(object? obj) => obj is Fingerprint128 f && Equals(f);
        public override int GetHashCode() => HashCode.Combine(Lo, Hi);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dedupUnderlying?.Dispose();
        _dedupSeen?.Dispose();
        _dedupRightIndex?.Dispose();
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
/// Mode for index-based streaming dedup in set operations.
/// </summary>
internal enum SetDedupMode
{
    /// <summary>UNION: emit row if index not yet seen.</summary>
    Union,
    /// <summary>INTERSECT: emit row if index exists in right set and not yet emitted.</summary>
    Intersect,
    /// <summary>EXCEPT: emit row if index NOT in right set and not yet emitted.</summary>
    Except,
}

/// <summary>
/// Pooled open-addressing index set for Fingerprint128 values.
/// Lo and Hi stored in separate contiguous arrays (distributed, cache-friendly probing).
/// Arrays rented from <see cref="ArrayPool{T}.Shared"/>; instances pooled via ThreadStatic.
/// After warmup: zero managed allocation — both arrays and instances are reused.
/// </summary>
internal sealed class IndexSet : IDisposable
{
    // Per-thread instance pool — avoids class allocation after warmup.
    // 2 slots covers INTERSECT/EXCEPT (rightSet + seenSet).
    [ThreadStatic] private static IndexSet? s_pool1;
    [ThreadStatic] private static IndexSet? s_pool2;

    // Maximum capacity: 16M entries. Beyond this, throw to prevent OOM.
    // At 75% load in a 16M table: 2 arrays × 16M × 8 bytes = 256 MB.
    private const int MaxCapacity = 1 << 24;

    private ulong[]? _lo;   // Fingerprint128.Lo — primary index key (probe)
    private ulong[]? _hi;   // Fingerprint128.Hi — packed guard+meta (verification)
    private int _count;
    private int _mask;       // capacity - 1 (power of 2 for branchless modulo)

    private IndexSet() { }

    /// <summary>
    /// Rents an IndexSet from the thread-local pool, or creates a new one.
    /// Arrays from previous use are preserved (pre-sized) — zero allocation after warmup.
    /// </summary>
    internal static IndexSet Rent()
    {
        var set = s_pool1;
        if (set != null) { s_pool1 = null; return set; }
        set = s_pool2;
        if (set != null) { s_pool2 = null; return set; }
        return new IndexSet();
    }

    // Sentinel handling: (0,0) marks empty slots. FNV-1a's non-zero offset basis
    // makes Lo=0 astronomically unlikely, and Hi=0 requires guard=0 AND
    // payloadLen=0 AND typeTag=0 simultaneously. As a safety net, if a real
    // index entry is (0,0), we flip bit 0 of Lo to distinguish from empty.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EscapeSentinel(ref ulong lo, ref ulong hi)
    {
        if (lo == 0 & hi == 0) lo = 1;
    }

    /// <summary>
    /// Adds an entry. Returns true if new, false if already present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Add(in SharcDataReader.Fingerprint128 fp)
    {
        if (_lo == null || _count >= ((_mask + 1) * 3) >> 2) // 75% load factor
            Grow();

        ulong fpLo = fp.Lo, fpHi = fp.Hi;
        EscapeSentinel(ref fpLo, ref fpHi);

        int slot = (int)(fpLo >> 1) & _mask;
        int capacity = _mask + 1;
        // Bounded probe: at 75% load, average probe ≤ 2.5. Capacity bound
        // is a safety net — prevents infinite loop if table is corrupted.
        for (int probe = 0; probe < capacity; probe++)
        {
            ulong lo = _lo![slot];
            ulong hi = _hi![slot];
            // Bitwise & intentional: both sides are trivial ulong comparisons
            // with no side effects; avoids branch prediction overhead.
            if (lo == 0 & hi == 0)
            {
                _lo[slot] = fpLo;
                _hi[slot] = fpHi;
                _count++;
                return true;
            }
            if (lo == fpLo & hi == fpHi)
                return false;
            slot = (slot + 1) & _mask;
        }
        throw new InvalidOperationException("IndexSet probe sequence exhausted.");
    }

    /// <summary>
    /// Checks if an entry is present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(in SharcDataReader.Fingerprint128 fp)
    {
        if (_lo == null || _hi == null) return false;

        ulong fpLo = fp.Lo, fpHi = fp.Hi;
        EscapeSentinel(ref fpLo, ref fpHi);

        int slot = (int)(fpLo >> 1) & _mask;
        int capacity = _mask + 1;
        for (int probe = 0; probe < capacity; probe++)
        {
            ulong lo = _lo[slot];
            ulong hi = _hi[slot];
            if (lo == 0 & hi == 0) return false;
            if (lo == fpLo & hi == fpHi) return true;
            slot = (slot + 1) & _mask;
        }
        return false;
    }

    private void Grow()
    {
        int oldCapacity = _lo != null ? _mask + 1 : 0;
        int newCapacity = oldCapacity == 0 ? 16 : oldCapacity << 1;

        if (newCapacity > MaxCapacity)
            throw new InvalidOperationException(
                $"IndexSet exceeded maximum capacity ({MaxCapacity:N0}).");

        var oldLo = _lo;
        var oldHi = _hi;

        _lo = ArrayPool<ulong>.Shared.Rent(newCapacity);
        _hi = ArrayPool<ulong>.Shared.Rent(newCapacity);
        Array.Clear(_lo, 0, newCapacity);
        Array.Clear(_hi, 0, newCapacity);
        _mask = newCapacity - 1;
        _count = 0;

        // Rehash existing entries into new arrays.
        // New table is at most 37.5% full, so probing always terminates.
        if (oldLo != null)
        {
            int oldMask = oldCapacity - 1;
            for (int i = 0; i <= oldMask; i++)
            {
                ulong lo = oldLo[i];
                ulong hi = oldHi![i];
                if (lo != 0 | hi != 0)
                {
                    int slot = (int)(lo >> 1) & _mask;
                    while (_lo[slot] != 0 | _hi[slot] != 0)
                        slot = (slot + 1) & _mask;
                    _lo[slot] = lo;
                    _hi[slot] = hi;
                    _count++;
                }
            }
            ArrayPool<ulong>.Shared.Return(oldLo);
            ArrayPool<ulong>.Shared.Return(oldHi!);
        }
    }

    /// <summary>
    /// Returns this instance to the thread-local pool for reuse.
    /// Arrays are cleared but kept — next Rent() gets pre-sized arrays.
    /// If pool is full, arrays are returned to ArrayPool.
    /// </summary>
    public void Dispose()
    {
        // Clear data but keep arrays for reuse
        if (_lo != null) Array.Clear(_lo, 0, _mask + 1);
        if (_hi != null) Array.Clear(_hi, 0, _mask + 1);
        _count = 0;

        // Return instance to pool
        if (s_pool1 == null) { s_pool1 = this; return; }
        if (s_pool2 == null) { s_pool2 = this; return; }

        // Pool full — release arrays to ArrayPool
        if (_lo != null) { ArrayPool<ulong>.Shared.Return(_lo); _lo = null; }
        if (_hi != null) { ArrayPool<ulong>.Shared.Return(_hi); _hi = null; }
        _mask = 0;
    }
}
