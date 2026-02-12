/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Schema;

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

    // Lazy decode support for projection path — avoids decoding TEXT/BLOB
    // body data until the caller actually requests the value.
    private readonly long[]? _serialTypes;
    private readonly bool[]? _decodedFlags;
    private bool _lazyMode;

    internal SharcDataReader(IBTreeCursor cursor, IRecordDecoder recordDecoder,
        IReadOnlyList<ColumnInfo> columns, int[]? projection)
    {
        _cursor = cursor;
        _recordDecoder = recordDecoder;
        _columns = columns;
        _projection = projection;

        // Pre-allocate a reusable buffer for decoding — avoids per-row ColumnValue[] allocation
        _reusableBuffer = new ColumnValue[columns.Count];

        // Allocate lazy-decode buffers when using projection
        if (projection != null)
        {
            _serialTypes = new long[columns.Count];
            _decodedFlags = new bool[columns.Count];
        }

        // Detect INTEGER PRIMARY KEY (rowid alias) — SQLite stores NULL in the record
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
    /// Advances the reader to the next row.
    /// </summary>
    /// <returns>True if there is another row; false if the end has been reached.</returns>
    public bool Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cursor.MoveNext())
        {
            _currentRow = null;
            _lazyMode = false;
            return false;
        }

        if (_projection != null)
        {
            // Lazy decode: only read serial types (varint parsing, no body decode).
            // Actual column values are decoded on first access via GetColumnValue().
            _recordDecoder.ReadSerialTypes(_cursor.Payload, _serialTypes!);
            Array.Clear(_decodedFlags!);
            _lazyMode = true;
        }
        else
        {
            // Full decode — reuse the pre-allocated buffer
            _recordDecoder.DecodeRecord(_cursor.Payload, _reusableBuffer!);
            _lazyMode = false;
        }

        _currentRow = _reusableBuffer;
        return true;
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
            // INTEGER PRIMARY KEY stores NULL in record; real value is rowid — not actually null
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
            ColumnStorageClass.Integer => SharcColumnType.Integer,
            ColumnStorageClass.Float => SharcColumnType.Float,
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
            ColumnStorageClass.Integer => val.AsInt64(),
            ColumnStorageClass.Float => val.AsDouble(),
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
        if (_lazyMode && !_decodedFlags![actualOrdinal])
        {
            _reusableBuffer![actualOrdinal] = _recordDecoder.DecodeColumn(_cursor.Payload, actualOrdinal);
            _decodedFlags[actualOrdinal] = true;
        }

        var value = _currentRow[actualOrdinal];

        // INTEGER PRIMARY KEY columns store NULL in the record; the real value is the rowid.
        if (actualOrdinal == _rowidAliasOrdinal && value.IsNull)
            return ColumnValue.Integer(1, _cursor.RowId);

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
    Integer = 1,

    /// <summary>IEEE 754 64-bit float.</summary>
    Float = 2,

    /// <summary>UTF-8 text string.</summary>
    Text = 3,

    /// <summary>Binary large object.</summary>
    Blob = 4
}
