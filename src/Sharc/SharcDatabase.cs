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
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Schema;
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
    private readonly IPageSource _pageSource;
    private readonly DatabaseHeader _header;
    private readonly IBTreeReader _bTreeReader;
    private readonly IRecordDecoder _recordDecoder;
    private readonly SharcSchema _schema;
    private readonly SharcDatabaseInfo _info;
    private bool _disposed;

    /// <summary>
    /// Gets the database schema containing tables, indexes, and views.
    /// </summary>
    public SharcSchema Schema
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _schema;
        }
    }

    /// <summary>
    /// Gets the database header information.
    /// </summary>
    public SharcDatabaseInfo Info
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _info;
        }
    }

    private SharcDatabase(IPageSource pageSource, DatabaseHeader header,
        IBTreeReader bTreeReader, IRecordDecoder recordDecoder,
        SharcSchema schema, SharcDatabaseInfo info)
    {
        _pageSource = pageSource;
        _header = header;
        _bTreeReader = bTreeReader;
        _recordDecoder = recordDecoder;
        _schema = schema;
        _info = info;
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
        if (!File.Exists(path))
            throw new FileNotFoundException("Database file not found.", path);

        options ??= new SharcOpenOptions();

        IPageSource pageSource;
        if (options.PreloadToMemory)
        {
            var data = File.ReadAllBytes(path);
            pageSource = new MemoryPageSource(data);
        }
        else
        {
            pageSource = new FilePageSource(path, options.FileShareMode);
        }

        try
        {
            return CreateFromPageSource(pageSource, options);
        }
        catch
        {
            pageSource.Dispose();
            throw;
        }
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
        if (data.IsEmpty)
            throw new InvalidDatabaseException("Database buffer is empty.");

        if (!DatabaseHeader.HasValidMagic(data.Span))
            throw new InvalidDatabaseException("Invalid SQLite magic string.");

        options ??= new SharcOpenOptions();
        IPageSource pageSource = new MemoryPageSource(data);
        return CreateFromPageSource(pageSource, options);
    }

    private static SharcDatabase CreateFromPageSource(IPageSource rawSource, SharcOpenOptions options)
    {
        IPageSource pageSource = rawSource;

        // Wrap with cache if requested
        if (options.PageCacheSize > 0)
            pageSource = new CachedPageSource(rawSource, options.PageCacheSize);

        var headerSpan = pageSource.GetPage(1);
        var header = DatabaseHeader.Parse(headerSpan);

        // Detect unsupported features
        if (header.IsWalMode)
            throw new UnsupportedFeatureException("WAL journal mode");
        if (header.TextEncoding is 2 or 3)
            throw new UnsupportedFeatureException("UTF-16 text encoding");

        var bTreeReader = new BTreeReader(pageSource, header);
        var recordDecoder = new RecordDecoder();

        // Read schema from page 1
        var schemaReader = new SchemaReader(bTreeReader, recordDecoder);
        var schema = schemaReader.ReadSchema();

        var info = new SharcDatabaseInfo
        {
            PageSize = header.PageSize,
            PageCount = header.PageCount,
            TextEncoding = (SharcTextEncoding)header.TextEncoding,
            SchemaFormat = header.SchemaFormat,
            UserVersion = header.UserVersion,
            ApplicationId = header.ApplicationId,
            SqliteVersion = header.SqliteVersionNumber,
            IsWalMode = header.IsWalMode,
            IsEncrypted = false
        };

        return new SharcDatabase(pageSource, header, bTreeReader, recordDecoder, schema, info);
    }

    /// <summary>
    /// Creates a forward-only reader for the specified table.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <returns>A data reader positioned before the first row.</returns>
    /// <exception cref="KeyNotFoundException">The table does not exist.</exception>
    public SharcDataReader CreateReader(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = _schema.GetTable(tableName);
        if (table.IsWithoutRowId)
            throw new UnsupportedFeatureException("WITHOUT ROWID tables");
        var cursor = _bTreeReader.CreateCursor((uint)table.RootPage);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, null);
    }

    /// <summary>
    /// Creates a reader that scans the table's b-tree with optional column projection.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="columns">Column names to include. Null or empty for all columns.</param>
    /// <returns>A data reader positioned before the first row.</returns>
    public SharcDataReader CreateReader(string tableName, params string[]? columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = _schema.GetTable(tableName);
        if (table.IsWithoutRowId)
            throw new UnsupportedFeatureException("WITHOUT ROWID tables");
        var cursor = _bTreeReader.CreateCursor((uint)table.RootPage);

        int[]? projection = null;
        if (columns is { Length: > 0 })
        {
            projection = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                var col = table.Columns.FirstOrDefault(c =>
                    c.Name.Equals(columns[i], StringComparison.OrdinalIgnoreCase));
                projection[i] = col?.Ordinal
                    ?? throw new ArgumentException($"Column '{columns[i]}' not found in table '{tableName}'.");
            }
        }

        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection);
    }

    /// <summary>
    /// Gets the total number of rows in the specified table.
    /// Requires a full b-tree scan.
    /// </summary>
    public long GetRowCount(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = _schema.GetTable(tableName);
        using var cursor = _bTreeReader.CreateCursor((uint)table.RootPage);
        long count = 0;
        while (cursor.MoveNext())
            count++;
        return count;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pageSource.Dispose();
    }
}
