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

using Sharc.Core;
using Sharc.Core.Schema;

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
    private readonly IBTreeCursor _cursor;
    private readonly IRecordDecoder _recordDecoder;
    private readonly IReadOnlyList<ColumnInfo> _columns;
    private readonly int[]? _projection;
    private readonly int _rowidAliasOrdinal;
    private ColumnValue[]? _currentRow;
    private ColumnValue[]? _reusableBuffer;
    private bool _disposed;

    // Index seek support
    private readonly IBTreeReader? _bTreeReader;
    private readonly IReadOnlyList<IndexInfo>? _tableIndexes;

    // Row-level filter support
    private readonly ResolvedFilter[]? _filters;

    // Lazy decode support for projection path — avoids decoding TEXT/BLOB
    // body data until the caller actually requests the value.
    private readonly long[]? _serialTypes;
    private readonly int[]? _decodedGenerations;
    private int _decodedGeneration;
    private bool _lazyMode;

    internal SharcDataReader(IBTreeCursor cursor, IRecordDecoder recordDecoder,
        IReadOnlyList<ColumnInfo> columns, int[]? projection,
        IBTreeReader? bTreeReader = null, IReadOnlyList<IndexInfo>? tableIndexes = null,
        ResolvedFilter[]? filters = null)
    {
        _cursor = cursor;
        _recordDecoder = recordDecoder;
        _columns = columns;
        _projection = projection;
        _bTreeReader = bTreeReader;
        _tableIndexes = tableIndexes;
        _filters = filters;

        // Pre-allocate a reusable buffer for decoding â€” avoids per-row ColumnValue[] allocation
        _reusableBuffer = new ColumnValue[columns.Count];

        // Allocate lazy-decode buffers when using projection
        if (projection != null)
        {
            _serialTypes = new long[columns.Count];
            _decodedGenerations = new int[columns.Count];
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
    /// Gets the number of columns in the current result set.
    /// </summary>
    public int FieldCount => _projection?.Length ?? _columns.Count;

    /// <summary>
    /// Gets the rowid of the current row.
    /// </summary>
    public long RowId => _cursor.RowId;

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

        bool found = _cursor.Seek(rowId);

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
            var indexRecord = _recordDecoder.DecodeRecord(indexCursor.Payload);
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
    public bool Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (_cursor.MoveNext())
        {
            DecodeCurrentRow();

            if (_filters is null || EvaluateFilters())
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
            _recordDecoder.DecodeRecord(_cursor.Payload, _reusableBuffer!);
            _lazyMode = false;
        }

        // Resolve INTEGER PRIMARY KEY alias — the record stores NULL,
        // but the real value is the b-tree rowid.
        if (_rowidAliasOrdinal >= 0 && _reusableBuffer![_rowidAliasOrdinal].IsNull)
        {
            _reusableBuffer[_rowidAliasOrdinal] =
                ColumnValue.FromInt64(4, _cursor.RowId);
        }

        return FilterEvaluator.MatchesAll(_filters!, _reusableBuffer!);
    }

    private void DecodeCurrentRow()
    {
        if (_projection != null)
        {
            // Lazy decode: only read serial types (varint parsing, no body decode).
            // Actual column values are decoded on first access via GetColumnValue().
            _recordDecoder.ReadSerialTypes(_cursor.Payload, _serialTypes!);
            // Increment generation instead of Array.Clear â€” O(1) vs O(N)
            _decodedGeneration++;
            _lazyMode = true;
        }
        else
        {
            // Full decode â€” reuse the pre-allocated buffer
            _recordDecoder.DecodeRecord(_cursor.Payload, _reusableBuffer!);
            _lazyMode = false;
        }

        _currentRow = _reusableBuffer;
    }

    /// <summary>
    /// Returns true if the column value is NULL.
    /// </summary>
    public bool IsNull(int ordinal)
    {
        if (_currentRow == null)
            throw new InvalidOperationException("No current row. Call Read() first.");

        int actualOrdinal = _projection != null ? _projection[ordinal] : ordinal;

        // Fast path: in lazy mode, check serial type directly (no body decode needed)
        if (_lazyMode)
        {
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
    public long GetInt64(int ordinal)
    {
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
    public double GetDouble(int ordinal)
    {
        return GetColumnValue(ordinal).AsDouble();
    }

    /// <summary>
    /// Gets a column value as a UTF-8 string.
    /// </summary>
    public string GetString(int ordinal)
    {
        return GetColumnValue(ordinal).AsString();
    }

    /// <summary>
    /// Gets a column value as a byte array (BLOB).
    /// </summary>
    public byte[] GetBlob(int ordinal)
    {
        return GetColumnValue(ordinal).AsBytes().ToArray();
    }

    /// <summary>
    /// Gets a column value as a read-only span of bytes (zero-copy for BLOBs).
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// </summary>
    public ReadOnlySpan<byte> GetBlobSpan(int ordinal)
    {
        return GetColumnValue(ordinal).AsBytes().Span;
    }

    /// <summary>
    /// Gets the column name at the specified ordinal.
    /// </summary>
    public string GetColumnName(int ordinal)
    {
        if (_projection != null)
            return _columns[_projection[ordinal]].Name;
        return _columns[ordinal].Name;
    }

    /// <summary>
    /// Gets the SQLite type affinity of the column value in the current row.
    /// </summary>
    public SharcColumnType GetColumnType(int ordinal)
    {
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

        if (actualOrdinal < 0 || actualOrdinal >= _currentRow.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal,
                "Column ordinal is out of range.");

        // Lazy decode: decode this column on first access (projection path only)
        if (_lazyMode && _decodedGenerations![actualOrdinal] != _decodedGeneration)
        {
            _reusableBuffer![actualOrdinal] = _recordDecoder.DecodeColumn(_cursor.Payload, actualOrdinal);
            _decodedGenerations[actualOrdinal] = _decodedGeneration;
        }

        var value = _currentRow[actualOrdinal];

        // INTEGER PRIMARY KEY columns store NULL in the record; the real value is the rowid.
        if (actualOrdinal == _rowidAliasOrdinal && value.IsNull)
            return ColumnValue.FromInt64(1, _cursor.RowId);

        return value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cursor.Dispose();
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
