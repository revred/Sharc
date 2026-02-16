// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Crypto;
using Sharc.Exceptions;
using global::Sharc.Core.Query;

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
    private global::Sharc.Query.QueryPlanCache? _queryCache;

    // ─── Compiled-query cache ─────────────────────────────────────
    // Eliminates per-invocation Expression.Lambda().Compile() overhead
    // by caching the JIT-compiled filter delegate, projection array,
    // and table metadata for each unique (intent, parameters) pair.
    private System.Collections.Concurrent.ConcurrentDictionary<
        global::Sharc.Query.Intent.QueryIntent, CachedReaderInfo>? _readerInfoCache;
    private System.Collections.Concurrent.ConcurrentDictionary<long, IFilterNode>? _paramFilterCache;

    private sealed class CachedReaderInfo
    {
        public required IFilterNode? FilterNode;
        public required int[]? Projection;
        public required TableInfo Table;
        public required int RowidAliasOrdinal;
    }

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
    /// <summary>
    /// Gets the database header information.
    /// </summary>
    public SharcDatabaseInfo Info
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _info with { PageCount = _proxySource.PageCount };
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


    internal SharcDatabase(ProxyPageSource proxySource, IPageSource rawSource, DatabaseHeader header,
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
    /// Creates a new, empty Sharc database valid for both engine and trust usage.
    /// Includes system tables: _sharc_ledger, _sharc_agents.
    /// </summary>
    /// <param name="path">The file path to create.</param>
    /// <returns>An open database instance with Write mode enabled.</returns>
    public static SharcDatabase Create(string path) => SharcDatabaseFactory.Create(path);

    /// <summary>
    /// Opens a SQLite database from a file path.
    /// </summary>
    /// <param name="path">Path to the SQLite database file.</param>
    /// <param name="options">Optional open configuration.</param>
    /// <returns>An open database instance.</returns>
    /// <exception cref="SharcException">The file is not a valid SQLite database.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static SharcDatabase Open(string path, SharcOpenOptions? options = null) => 
        SharcDatabaseFactory.Open(path, options);

    /// <summary>
    /// Opens a SQLite database from an in-memory buffer.
    /// The buffer is not copied; caller must keep it alive for the lifetime of this instance.
    /// </summary>
    /// <param name="data">The raw database bytes.</param>
    /// <param name="options">Optional open configuration.</param>
    /// <returns>An open database instance.</returns>
    public static SharcDatabase OpenMemory(ReadOnlyMemory<byte> data, SharcOpenOptions? options = null) =>
        SharcDatabaseFactory.OpenMemory(data, options);

    /// <summary>
    /// Creates a forward-only reader for the specified table with optional column projection and filtering.
    /// </summary>
    /// <param name="tableName">Name of the table to read.</param>
    /// <param name="columns">Optional column names to include.</param>
    /// <param name="filters">Optional array of <see cref="SharcFilter"/> conditions.</param>
    /// <param name="filter">Optional <see cref="IFilterStar"/> expression for advanced filtering.</param>
    /// <returns>A data reader positioned before the first row.</returns>
    public SharcDataReader CreateReader(string tableName, string[]? columns = null, SharcFilter[]? filters = null, IFilterStar? filter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();
        var table = schema.GetTable(tableName) ?? throw new KeyNotFoundException($"Table '{tableName}' not found.");
        var cursor = CreateTableCursor(table);

        int[]? projection = null;
        if (columns is { Length: > 0 })
        {
            projection = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                int ordinal = table.GetColumnOrdinal(columns[i]);
                projection[i] = ordinal >= 0 ? ordinal : throw new ArgumentException($"Column '{columns[i]}' not found.");
            }
        }

        if (filter != null)
        {
            var node = FilterTreeCompiler.CompileBaked(filter, table.Columns, FindIntegerPrimaryKeyOrdinal(table.Columns));
            return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection, _bTreeReader, table.Indexes, null, node);
        }

        return new SharcDataReader(cursor, _recordDecoder, table.Columns, projection, _bTreeReader, table.Indexes, ResolveFilters(table, filters));
    }

    /// <summary>Creates a reader with column projection.</summary>
    public SharcDataReader CreateReader(string tableName, params string[]? columns) => CreateReader(tableName, columns, null, null);

    /// <summary>Creates a reader with multiple filters.</summary>
    public SharcDataReader CreateReader(string tableName, params SharcFilter[] filters) => CreateReader(tableName, null, filters, null);

    /// <summary>Creates a reader with a FilterStar expression.</summary>
    public SharcDataReader CreateReader(string tableName, IFilterStar filter) => CreateReader(tableName, null, null, filter);

    /// <summary>Creates a reader with column projection and a FilterStar expression.</summary>
    public SharcDataReader CreateReader(string tableName, string[]? columns, IFilterStar filter) => CreateReader(tableName, columns, null, filter);

    /// <summary>
    /// Executes a Sharq query string and returns a data reader.
    /// Compiled plans are cached for zero-overhead repeat queries.
    /// </summary>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT name FROM users WHERE age > 18").</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="Sharc.Query.Sharq.SharqParseException">The query string is invalid.</exception>
    /// <exception cref="ArgumentException">Referenced table or column not found in schema.</exception>
    public SharcDataReader Query(string sharqQuery) => QueryCore(null, sharqQuery, null);

    /// <summary>
    /// Executes a Sharq query string with parameter bindings and returns a data reader.
    /// Compiled plans are cached; parameters are resolved at execution time.
    /// </summary>
    /// <param name="parameters">Named parameters to bind (e.g. { ["minAge"] = 25L }).</param>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT * FROM users WHERE age = $minAge").</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    public SharcDataReader Query(IReadOnlyDictionary<string, object>? parameters, string sharqQuery)
        => QueryCore(parameters, sharqQuery, null);

    /// <summary>
    /// Executes a Sharq query string with agent entitlement enforcement.
    /// The agent's <see cref="Core.Trust.AgentInfo.ReadScope"/> is validated
    /// against the requested table and columns before execution.
    /// </summary>
    /// <param name="sharqQuery">A Sharq SELECT statement.</param>
    /// <param name="agent">The agent whose read scope will be enforced.</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="UnauthorizedAccessException">The agent's scope does not permit this query.</exception>
    public SharcDataReader Query(string sharqQuery, Core.Trust.AgentInfo agent)
        => QueryCore(null, sharqQuery, agent);

    /// <summary>
    /// Executes a Sharq query string with parameter bindings and agent entitlement enforcement.
    /// Compiled plans are cached; parameters are resolved at execution time.
    /// The agent's <see cref="Core.Trust.AgentInfo.ReadScope"/> is validated
    /// against the requested table and columns before execution.
    /// </summary>
    /// <param name="parameters">Named parameters to bind (e.g. { ["minAge"] = 25L }).</param>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT * FROM users WHERE age = $minAge").</param>
    /// <param name="agent">The agent whose read scope will be enforced.</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="UnauthorizedAccessException">The agent's scope does not permit this query.</exception>
    /// <exception cref="ArgumentException">A referenced parameter was not provided.</exception>
    public SharcDataReader Query(
        IReadOnlyDictionary<string, object>? parameters,
        string sharqQuery,
        Core.Trust.AgentInfo agent)
        => QueryCore(parameters, sharqQuery, agent);

    private SharcDataReader QueryCore(
        IReadOnlyDictionary<string, object>? parameters,
        string sharqQuery,
        Core.Trust.AgentInfo? agent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cache = _queryCache ??= new global::Sharc.Query.QueryPlanCache();
        var plan = cache.GetOrCompilePlan(sharqQuery);

        // Enforce entitlements when an agent is specified
        if (agent is not null)
        {
            if (plan.IsCompound || plan.HasCtes)
            {
                var tables = global::Sharc.Query.TableReferenceCollector.Collect(plan);
                Trust.EntitlementEnforcer.EnforceAll(agent, tables);
            }
            else
            {
                var simpleIntent = plan.Simple!;
                Trust.EntitlementEnforcer.Enforce(agent, simpleIntent.TableName,
                    simpleIntent.ColumnsArray);
            }
        }

        // Compound or CTE queries go through the compound executor
        if (plan.IsCompound || plan.HasCtes)
            return global::Sharc.Query.CompoundQueryExecutor.Execute(this, plan, parameters);

        // Simple query — direct execution with compiled-query cache
        var intent = plan.Simple!;
        var reader = CreateReaderFromIntent(intent, parameters);
        return global::Sharc.Query.QueryPostProcessor.Apply(reader, intent);
    }

    /// <summary>
    /// Creates a reader from a compiled <see cref="global::Sharc.Query.Intent.QueryIntent"/>
    /// using cached filter nodes, projections, and table metadata.
    /// Eliminates per-invocation Expression.Lambda().Compile() overhead.
    /// </summary>
    internal SharcDataReader CreateReaderFromIntent(
        global::Sharc.Query.Intent.QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var readerCache = _readerInfoCache ??=
            new System.Collections.Concurrent.ConcurrentDictionary<
                global::Sharc.Query.Intent.QueryIntent, CachedReaderInfo>();

        // Build or retrieve cached reader info (projection, table, filter for non-parameterized)
        if (!readerCache.TryGetValue(intent, out var info))
        {
            var schema = GetSchema();
            var table = schema.GetTable(intent.TableName)
                ?? throw new KeyNotFoundException($"Table '{intent.TableName}' not found.");

            string[]? columns = intent.HasAggregates
                ? global::Sharc.Query.AggregateProjection.Compute(intent)
                : intent.ColumnsArray;

            int[]? projection = null;
            if (columns is { Length: > 0 })
            {
                projection = new int[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                {
                    int ordinal = table.GetColumnOrdinal(columns[i]);
                    projection[i] = ordinal >= 0 ? ordinal
                        : throw new ArgumentException($"Column '{columns[i]}' not found.");
                }
            }

            int rowidAlias = FindIntegerPrimaryKeyOrdinal(table.Columns);

            // Pre-compile filter for non-parameterized intents
            IFilterNode? filterNode = null;
            if (intent.Filter.HasValue && !HasParameterNodes(intent.Filter.Value))
            {
                var filterStar = global::Sharc.Query.IntentToFilterBridge.Build(
                    intent.Filter.Value, null);
                filterNode = FilterTreeCompiler.CompileBaked(filterStar, table.Columns, rowidAlias);
            }

            info = new CachedReaderInfo
            {
                FilterNode = filterNode,
                Projection = projection,
                Table = table,
                RowidAliasOrdinal = rowidAlias,
            };
            readerCache.TryAdd(intent, info);
        }

        // Determine filter node to use
        IFilterNode? node = info.FilterNode;

        if (node == null && intent.Filter.HasValue)
        {
            // Parameterized filter — check param-level cache
            var paramCache = _paramFilterCache ??= new();
            long paramKey = ComputeParamCacheKey(intent, parameters);
            node = paramCache.GetOrAdd(paramKey, _ =>
            {
                var filterStar = global::Sharc.Query.IntentToFilterBridge.Build(
                    intent.Filter.Value, parameters);
                return FilterTreeCompiler.CompileBaked(
                    filterStar, info.Table.Columns, info.RowidAliasOrdinal);
            });
        }

        var cursor = CreateTableCursor(info.Table);
        return new SharcDataReader(cursor, _recordDecoder, info.Table.Columns,
            info.Projection, _bTreeReader, info.Table.Indexes, null, node);
    }

    private static bool HasParameterNodes(global::Sharc.Query.Intent.PredicateIntent predicate)
    {
        foreach (ref readonly var node in predicate.Nodes.AsSpan())
        {
            if (node.Value.Kind == global::Sharc.Query.Intent.IntentValueKind.Parameter)
                return true;
        }
        return false;
    }

    private static long ComputeParamCacheKey(
        global::Sharc.Query.Intent.QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var hc = new HashCode();
        hc.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(intent));
        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                hc.Add(kvp.Key);
                hc.Add(kvp.Value);
            }
        }
        return hc.ToHashCode();
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
            ColumnInfo? col = null;
            // Use loop instead of LINQ FirstOrDefault
            var colCount = table.Columns.Count;
            for (int c = 0; c < colCount; c++)
            {
                if (table.Columns[c].Name.Equals(filters[i].ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    col = table.Columns[c];
                    break;
                }
            }

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
        return schema.GetTable(tableName).Indexes.ToList();
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
