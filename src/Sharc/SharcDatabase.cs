using Sharc.Core;
using Sharc.Exceptions;
using Sharc.Schema;

namespace Sharc;

/// <summary>
/// Primary entry point for reading SQLite databases.
/// Supports file-backed and in-memory databases with optional encryption.
/// </summary>
/// <example>
/// <code>
/// // File-backed
/// using var db = SharcDatabase.Open("mydata.db");
///
/// // In-memory
/// byte[] bytes = File.ReadAllBytes("mydata.db");
/// using var db = SharcDatabase.OpenMemory(bytes);
///
/// // Encrypted
/// using var db = SharcDatabase.Open("encrypted.db", new SharcOpenOptions
/// {
///     Encryption = new SharcEncryptionOptions { Password = "secret" }
/// });
///
/// // Read data
/// foreach (var table in db.Schema.Tables)
/// {
///     using var reader = db.CreateReader(table.Name);
///     while (reader.Read())
///     {
///         var value = reader.GetString(0);
///     }
/// }
/// </code>
/// </example>
public sealed class SharcDatabase : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets the database schema containing tables, indexes, and views.
    /// </summary>
    public SharcSchema Schema => throw new NotImplementedException();

    /// <summary>
    /// Gets the database header information.
    /// </summary>
    public SharcDatabaseInfo Info => throw new NotImplementedException();

    private SharcDatabase()
    {
    }

    /// <summary>
    /// Opens a SQLite database from a file path.
    /// </summary>
    /// <param name="path">Path to the SQLite database file.</param>
    /// <param name="options">Optional open configuration.</param>
    /// <returns>An open database instance.</returns>
    /// <exception cref="SharcException">The file is not a valid SQLite database.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static SharcDatabase Open(string path, SharcOpenOptions? options = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Opens a SQLite database from an in-memory buffer.
    /// The buffer is not copied; caller must keep it alive for the lifetime of this instance.
    /// </summary>
    /// <param name="data">The raw database bytes.</param>
    /// <param name="options">Optional open configuration.</param>
    /// <returns>An open database instance.</returns>
    public static SharcDatabase OpenMemory(ReadOnlyMemory<byte> data, SharcOpenOptions? options = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates a forward-only reader for the specified table.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <returns>A data reader positioned before the first row.</returns>
    /// <exception cref="SharcException">The table does not exist.</exception>
    public SharcDataReader CreateReader(string tableName)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates a reader that scans the table's b-tree with optional column projection.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="columns">Column names to include. Null for all columns.</param>
    /// <returns>A data reader positioned before the first row.</returns>
    public SharcDataReader CreateReader(string tableName, params string[]? columns)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the total number of rows in the specified table.
    /// Requires a full b-tree scan if no cached count is available.
    /// </summary>
    public long GetRowCount(string tableName)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Dispose page source, cache, key handle
    }
}
