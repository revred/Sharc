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

using System.Text;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Crypto;
using Sharc.Exceptions;

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
    private readonly ProxyPageSource _proxySource;
    private readonly IPageSource _rawSource;
    private readonly DatabaseHeader _header;
    private Transaction? _activeTransaction;
    private readonly IBTreeReader _bTreeReader;
    private readonly IRecordDecoder _recordDecoder;
    private readonly SharcDatabaseInfo _info;
    private readonly SharcKeyHandle? _keyHandle;
    private readonly SharcExtensionRegistry _extensions = new();
    private readonly ISharcBufferPool _bufferPool = SharcBufferPool.Shared;
    private SharcSchema? _schema;
    private bool _disposed;

    /// <summary>
    /// Gets the extension registry for this database instance.
    /// </summary>
    public SharcExtensionRegistry Extensions => _extensions;

    /// <summary>
    /// Gets the buffer pool used for temporary allocations.
    /// </summary>
    public ISharcBufferPool BufferPool => _bufferPool;

    /// <summary>
    /// Gets the file path of the database, or null if it is an in-memory database.
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Gets the database schema containing tables, indexes, and views.
    /// </summary>
    public SharcSchema Schema
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetSchema();
        }
    }

    /// <summary>
    /// Gets the record decoder for this database.
    /// </summary>
    public IRecordDecoder RecordDecoder => _recordDecoder;

    /// <summary>
    /// Gets the usable page size (PageSize - ReservedBytes).
    /// </summary>
    public int UsablePageSize => _header.UsablePageSize;

    /// <summary>
    /// Gets the database header.
    /// </summary>
    public DatabaseHeader Header => _header;

    /// <summary>
    /// Gets the underlying page source.
    /// </summary>
    public IPageSource PageSource => _proxySource;

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

    /// <summary>
    /// Gets the internal B-tree reader, allowing advanced consumers (e.g. graph stores)
    /// to share the same page source without duplicating it.
    /// </summary>
    public IBTreeReader BTreeReader
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bTreeReader;
        }
    }


    private SharcDatabase(ProxyPageSource proxySource, IPageSource rawSource, DatabaseHeader header,
        IBTreeReader bTreeReader, IRecordDecoder recordDecoder,
        SharcDatabaseInfo info, string? filePath = null, SharcKeyHandle? keyHandle = null)
    {
        _proxySource = proxySource;
        _rawSource = rawSource;
        _header = header;
        _bTreeReader = bTreeReader;
        _recordDecoder = recordDecoder;
        _info = info;
        FilePath = filePath;
        _keyHandle = keyHandle;

        // Register default extensions
        if (_recordDecoder is ISharcExtension ext)
        {
            _extensions.Register(ext);
            ext.OnRegister(this);
        }
    }

    /// <summary>
    /// Lazily loads the schema on first access. Avoids ~15-28 KB allocation on
    /// <c>OpenMemory()</c> when the caller only needs <see cref="Info"/> or <see cref="BTreeReader"/>.
    /// </summary>
    private SharcSchema GetSchema() =>
        _schema ??= new SchemaReader(_bTreeReader, _recordDecoder).ReadSchema();

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
        options ??= new SharcOpenOptions();

        // Recovery: Check for journal file
        var journalPath = path + ".journal";
        if (File.Exists(journalPath))
        {
            // We found a journal! This means the previous session crashed during commit.
            // Recover original state before opening.
            RollbackJournal.Recover(path, journalPath);
            File.Delete(journalPath);
        }

        // Read the first 6 bytes with sharing to detect encrypted vs plain SQLite
        bool isEncryptedFile;
        {
            Span<byte> magic = stackalloc byte[6];
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read,
                options.FileShareMode);
            int read = probe.Read(magic);
            isEncryptedFile = read >= 6 && EncryptionHeader.HasMagic(magic);
        }

        if (isEncryptedFile)
        {
            byte[] fileData;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                options.FileShareMode))
            {
                fileData = new byte[fs.Length];
                fs.ReadExactly(fileData);
            }
            return OpenEncrypted(fileData, options);
        }

        IPageSource pageSource;
        if (options.PreloadToMemory)
        {
            var data = File.ReadAllBytes(path);
            pageSource = new MemoryPageSource(data);
        }
        else
        {
            pageSource = new FilePageSource(path, options.FileShareMode, allowWrites: options.Writable);
        }

        try
        {
            // Check for WAL mode and wrap page source if a WAL file exists
            var headerSpan = pageSource.GetPage(1);
            var header = DatabaseHeader.Parse(headerSpan);

            if (header.IsWalMode)
            {
                var walPath = path + "-wal";
                if (File.Exists(walPath))
                {
                    // Read WAL file with ReadWrite sharing since another process
                    // (e.g., SQLite writer) may hold a lock on it.
                    byte[] walData;
                    using (var walStream = new FileStream(walPath, FileMode.Open,
                        FileAccess.Read, FileShare.ReadWrite))
                    {
                        walData = new byte[walStream.Length];
                        walStream.ReadExactly(walData);
                    }

                    if (walData.Length >= WalHeader.HeaderSize)
                    {
                        var walFrameMap = WalReader.ReadFrameMap(walData, header.PageSize);
                        if (walFrameMap.Count > 0)
                        {
                            pageSource = new WalPageSource(pageSource, walData, walFrameMap);
                        }
                    }
                }
            }

            return CreateFromPageSource(pageSource, options, filePath: path);
        }
        catch
        {
            pageSource.Dispose();
            throw;
        }
    }

    private static SharcDatabase OpenEncrypted(byte[] fileData, SharcOpenOptions options)
    {
        var password = options.Encryption?.Password
            ?? throw new SharcCryptoException("Password required for encrypted database.");

        var encHeader = EncryptionHeader.Parse(fileData);
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var keyHandle = SharcKeyHandle.DeriveKey(
            passwordBytes, encHeader.Salt.Span,
            encHeader.TimeCost, encHeader.MemoryCostKiB, encHeader.Parallelism);

        try
        {
            // Verify the key against the stored verification hash
            var computedHmac = keyHandle.ComputeHmac(encHeader.Salt.Span);
            if (!computedHmac.AsSpan().SequenceEqual(encHeader.VerificationHash.Span))
                throw new SharcCryptoException("Wrong password or corrupted encryption header.");

            var transform = new AesGcmPageTransform(keyHandle);
            int encryptedPageSize = transform.TransformedPageSize(encHeader.PageSize);

            // Create page source over the encrypted data (after the 128-byte header)
            var encryptedData = new ReadOnlyMemory<byte>(fileData, EncryptionHeader.HeaderSize,
                fileData.Length - EncryptionHeader.HeaderSize);
            var innerSource = new MemoryPageSource(encryptedData, encryptedPageSize, encHeader.PageCount);

            var decryptingSource = new DecryptingPageSource(innerSource, transform, encHeader.PageSize);

            try
            {
                // Transfer keyHandle ownership to SharcDatabase
                return CreateFromPageSource(decryptingSource, options,
                    isEncrypted: true, keyHandle: keyHandle);
            }
            catch
            {
                decryptingSource.Dispose();
                throw;
            }
        }
        catch
        {
            keyHandle.Dispose();
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

    private static SharcDatabase CreateFromPageSource(IPageSource rawSource, SharcOpenOptions options,
        bool isEncrypted = false, string? filePath = null, SharcKeyHandle? keyHandle = null)
    {
        IPageSource pageSource = rawSource;

        // Wrap with cache if requested
        if (options.PageCacheSize > 0)
            pageSource = new CachedPageSource(rawSource, options.PageCacheSize);

        var headerSpan = pageSource.GetPage(1);
        var header = DatabaseHeader.Parse(headerSpan);

        // Detect unsupported features
        if (header.TextEncoding is 2 or 3)
            throw new UnsupportedFeatureException("UTF-16 text encoding");

        var proxySource = new ProxyPageSource(pageSource);
        var bTreeReader = new BTreeReader(proxySource, header);
        var recordDecoder = new RecordDecoder();

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
            IsEncrypted = isEncrypted
        };

        return new SharcDatabase(proxySource, pageSource, header, bTreeReader, recordDecoder, info, filePath, keyHandle);
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
        var schema = GetSchema();
        var table = schema.GetTable(tableName);
        var cursor = CreateTableCursor(table);
        var tableIndexes = GetTableIndexes(schema, tableName);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, null,
            _bTreeReader, tableIndexes);
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
        var schema = GetSchema();
        var table = schema.GetTable(tableName);
        var cursor = CreateTableCursor(table);

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

        var tableIndexes = GetTableIndexes(schema, tableName);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection,
            _bTreeReader, tableIndexes);
    }

    /// <summary>
    /// Creates a reader that scans the table with row-level filters applied (AND semantics).
    /// Rows that do not match all filters are skipped during iteration.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="filters">Filter conditions. All must match for a row to be returned.</param>
    /// <returns>A data reader positioned before the first matching row.</returns>
    public SharcDataReader CreateReader(string tableName, params SharcFilter[] filters)
    {
        return CreateReader(tableName, null, filters);
    }

    /// <summary>
    /// Creates a reader with both column projection and row-level filters.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="columns">Column names to include. Null or empty for all columns.</param>
    /// <param name="filters">Filter conditions (AND semantics). Null or empty for no filtering.</param>
    /// <returns>A data reader positioned before the first matching row.</returns>
    public SharcDataReader CreateReader(string tableName, string[]? columns, SharcFilter[]? filters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();
        var table = schema.GetTable(tableName);
        var cursor = CreateTableCursor(table);

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

        ResolvedFilter[]? resolved = ResolveFilters(table, filters);
        var tableIndexes = GetTableIndexes(schema, tableName);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection,
            _bTreeReader, tableIndexes, resolved);
    }

    /// <summary>
    /// Creates a reader with byte-level row filtering using the FilterStar expression tree.
    /// Rows that do not match the filter are skipped without full record decoding.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="filter">FilterStar expression tree.</param>
    /// <returns>A data reader positioned before the first matching row.</returns>
    public SharcDataReader CreateReader(string tableName, IFilterStar filter)
    {
        return CreateReader(tableName, null, filter);
    }

    /// <summary>
    /// Creates a reader with column projection and byte-level row filtering.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="columns">Column names to include. Null or empty for all columns.</param>
    /// <param name="filter">FilterStar expression tree.</param>
    /// <returns>A data reader positioned before the first matching row.</returns>
    public SharcDataReader CreateReader(string tableName, string[]? columns, IFilterStar filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();
        var table = schema.GetTable(tableName);
        var cursor = CreateTableCursor(table);

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

        int rowidAlias = FindIntegerPrimaryKeyOrdinal(table.Columns);
        var filterNode = FilterTreeCompiler.CompileBaked(filter, table.Columns, rowidAlias);
        var tableIndexes = GetTableIndexes(schema, tableName);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection,
            _bTreeReader, tableIndexes, filters: null, filterNode: filterNode);
    }

    /// <summary>
    /// Internal overload to support pre-compiled filter nodes with projection.
    /// </summary>
    internal SharcDataReader CreateReader(string tableName, string[]? columns, IFilterNode filterNode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();
        var table = schema.GetTable(tableName);
        var cursor = CreateTableCursor(table);

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

        var tableIndexes = GetTableIndexes(schema, tableName);
        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection,
            _bTreeReader, tableIndexes, filters: null, filterNode: filterNode);
    }

    /// <summary>
    /// Internal overload to support pre-compiled filter nodes for high-performance usage.
    /// </summary>
    internal SharcDataReader CreateReader(string tableName, IFilterNode filterNode)
    {
        return CreateReader(tableName, null, filterNode);
    }

    private static ResolvedFilter[]? ResolveFilters(TableInfo table, SharcFilter[]? filters)
    {
        if (filters is not { Length: > 0 })
            return null;

        var resolved = new ResolvedFilter[filters.Length];
        for (int i = 0; i < filters.Length; i++)
        {
            var col = table.Columns.FirstOrDefault(c =>
                c.Name.Equals(filters[i].ColumnName, StringComparison.OrdinalIgnoreCase));
            resolved[i] = new ResolvedFilter
            {
                ColumnOrdinal = col?.Ordinal
                    ?? throw new ArgumentException(
                        $"Filter column '{filters[i].ColumnName}' not found in table '{table.Name}'."),
                Operator = filters[i].Operator,
                Value = filters[i].Value
            };
        }
        return resolved;
    }

    private IBTreeCursor CreateTableCursor(TableInfo table)
    {
        if (table.IsWithoutRowId)
        {
            var indexCursor = _bTreeReader.CreateIndexCursor((uint)table.RootPage);
            int intPkOrdinal = FindIntegerPrimaryKeyOrdinal(table.Columns);
            return new WithoutRowIdCursorAdapter(indexCursor, _recordDecoder, intPkOrdinal);
        }
        return _bTreeReader.CreateCursor((uint)table.RootPage);
    }

    private static int FindIntegerPrimaryKeyOrdinal(IReadOnlyList<ColumnInfo> columns)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].IsPrimaryKey &&
                columns[i].DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                return columns[i].Ordinal;
        }
        return -1;
    }

    private static List<IndexInfo> GetTableIndexes(SharcSchema schema, string tableName)
    {
        return schema.Indexes
            .Where(i => i.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets the total number of rows in the specified table.
    /// Requires a full b-tree scan.
    /// </summary>
    public long GetRowCount(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = GetSchema().GetTable(tableName);
        using var cursor = CreateTableCursor(table);
        long count = 0;
        while (cursor.MoveNext())
            count++;
        return count;
    }

    /// <summary>
    /// Begins a new transaction for atomic writes.
    /// Only one transaction can be active at a time.
    /// </summary>
    public Transaction BeginTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeTransaction != null)
            throw new InvalidOperationException("A transaction is already active.");

        if (_rawSource is not IWritablePageSource writable)
            throw new NotSupportedException("The database is opened in read-only mode.");

        _activeTransaction = new Transaction(this, writable);
        _proxySource.SetTarget(_activeTransaction.PageSource);
        return _activeTransaction;
    }

    internal void EndTransaction(Transaction transaction)
    {
        if (_activeTransaction == transaction)
        {
            _activeTransaction = null;
            _proxySource.SetTarget(_rawSource);
        }
    }

    /// <summary>
    /// Writes a page to the database. If a transaction is active, the write is buffered.
    /// Otherwise, it is written directly (auto-commit behavior can be added later).
    /// </summary>
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeTransaction != null)
        {
            _activeTransaction.WritePage(pageNumber, source);
        }
        else if (_rawSource is IWritablePageSource writable)
        {
            writable.WritePage(pageNumber, source);
            writable.Flush();
        }
        else
        {
            throw new NotSupportedException("The database is opened in read-only mode.");
        }
    }

    /// <summary>
    /// Reads a page from the database. If a transaction is active, it may return a buffered write.
    /// </summary>
    public ReadOnlySpan<byte> ReadPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _proxySource.GetPage(pageNumber);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeTransaction?.Dispose();
        _proxySource.Dispose();
        _rawSource.Dispose();
        _keyHandle?.Dispose();
    }
}
