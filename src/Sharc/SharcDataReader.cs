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
    private bool _disposed;

    /// <summary>
    /// Gets the number of columns in the current result set.
    /// </summary>
    public int FieldCount => throw new NotImplementedException();

    /// <summary>
    /// Gets the rowid of the current row.
    /// </summary>
    public long RowId => throw new NotImplementedException();

    /// <summary>
    /// Advances the reader to the next row.
    /// </summary>
    /// <returns>True if there is another row; false if the end has been reached.</returns>
    public bool Read()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns true if the column value is NULL.
    /// </summary>
    public bool IsNull(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets a column value as a 64-bit signed integer.
    /// </summary>
    public long GetInt64(int ordinal)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets a column value as a UTF-8 string.
    /// </summary>
    public string GetString(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets a column value as a byte array (BLOB).
    /// </summary>
    public byte[] GetBlob(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets a column value as a read-only span of bytes (zero-copy for BLOBs).
    /// The span is valid only until the next call to <see cref="Read"/>.
    /// </summary>
    public ReadOnlySpan<byte> GetBlobSpan(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the column name at the specified ordinal.
    /// </summary>
    public string GetColumnName(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the SQLite type affinity of the column value in the current row.
    /// </summary>
    public SharcColumnType GetColumnType(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the column value as a boxed object. Returns DBNull.Value for NULL.
    /// Prefer typed accessors for zero-allocation reads.
    /// </summary>
    public object GetValue(int ordinal)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
