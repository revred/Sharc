// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Schema;
using Sharc.Crypto;
using Sharc.Exceptions;
using global::Sharc.Core.Query;
using Sharc.Query;
using Sharc.Query.Execution;
using Sharc.Query.Optimization;
using Sharc.Query.Sharq;
using Intent = global::Sharc.Query.Intent;

namespace Sharc;

/// <summary>
/// Primary entry point for reading SQLite databases.
/// Supports file-backed and in-memory databases with optional encryption.
/// </summary>
/// <remarks>
/// <b>Thread safety:</b> This class is NOT thread-safe. All operations — including
/// <see cref="Query(string)"/>, <see cref="RegisterView"/>, and <see cref="CreateReader(string, string[])"/> —
/// must be called from a single thread or externally synchronized.
/// </remarks>
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
    private DatabaseHeader _header;
    private uint _lastValidatedCookie;
    private Transaction? _activeTransaction;
    private readonly IBTreeReader _bTreeReader;
    private readonly IRecordDecoder _recordDecoder;
    private readonly SharcDatabaseInfo _info;
    private readonly SharcKeyHandle? _keyHandle;
    private readonly SharcExtensionRegistry _extensions = new();
    private readonly ISharcBufferPool _bufferPool = SharcBufferPool.Shared;
    private SharcSchema? _schema;
    private bool _disposed;
    private QueryPlanCache? _queryCache;
    private readonly ExecutionRouter _executionRouter = new();

    // ─── Registered programmatic views ────────────────────────────
    private readonly Views.ViewResolver _viewResolver;

    // ─── Compiled-query cache ─────────────────────────────────────
    // Eliminates per-invocation delegate construction overhead
    // by caching the closure-composed filter delegate, projection array,
    // and table metadata for each unique (intent, parameters) pair.
    // Dictionary (not ConcurrentDictionary) — SharcDatabase is not thread-safe.
    private Dictionary<Intent.QueryIntent, CachedReaderInfo>? _readerInfoCache;
    private Dictionary<long, IFilterNode>? _paramFilterCache;

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
        _lastValidatedCookie = header.SchemaCookie;
        _bTreeReader = bTreeReader;
        _recordDecoder = recordDecoder;
        _info = info;
        FilePath = filePath;
        _keyHandle = keyHandle;
        _viewResolver = new Views.ViewResolver(this);

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
    /// Enriches ViewInfo objects with structural metadata from CREATE VIEW SQL.
    /// </summary>
    private SharcSchema GetSchema() =>
        _schema ??= EnrichViewMetadata(new SchemaReader(_bTreeReader, _recordDecoder).ReadSchema());

    /// <summary>
    /// Exposes the schema for internal consumers (e.g. <see cref="Views.ViewResolver"/>)
    /// that need access without the <see cref="ObjectDisposedException"/> guard.
    /// </summary>
    internal SharcSchema GetSchemaInternal() => GetSchema();

    /// <summary>
    /// Enriches ViewInfo objects with structural metadata parsed from CREATE VIEW SQL.
    /// Cold path — runs once per schema load.
    /// </summary>
    private static SharcSchema EnrichViewMetadata(SharcSchema schema)
    {
        if (schema.Views.Count == 0) return schema;

        var enrichedViews = new ViewInfo[schema.Views.Count];
        bool anyEnriched = false;

        for (int i = 0; i < schema.Views.Count; i++)
        {
            var view = schema.Views[i];
            var result = ViewSqlScanner.Scan(view.Sql);

            if (result.ParseSucceeded)
            {
                var columns = new ViewColumnInfo[result.Columns.Length];
                for (int c = 0; c < result.Columns.Length; c++)
                {
                    ref var col = ref result.Columns[c];
                    columns[c] = new ViewColumnInfo
                    {
                        SourceName = col.SourceName,
                        DisplayName = col.DisplayName,
                        Ordinal = col.Ordinal
                    };
                }

                enrichedViews[i] = new ViewInfo
                {
                    Name = view.Name,
                    Sql = view.Sql,
                    SourceTables = result.SourceTables,
                    Columns = columns,
                    IsSelectAll = result.IsSelectAll,
                    HasJoin = result.HasJoin,
                    HasFilter = result.HasFilter,
                    ParseSucceeded = true
                };
                anyEnriched = true;
            }
            else
            {
                enrichedViews[i] = view;
            }
        }

        if (!anyEnriched) return schema;

        return new SharcSchema
        {
            Tables = schema.Tables,
            Indexes = schema.Indexes,
            Views = enrichedViews
        };
    }
        
    internal void InvalidateSchema()
    {
        _schema = null;
        _queryCache?.Clear();
        _readerInfoCache?.Clear();
        _paramFilterCache?.Clear();
    }

    private void EnsureSchemaUpToDate()
    {
        // 1. Force the page source to re-read Page 1 by invalidating any cache
        _rawSource.Invalidate(1);

        // 2. Read the 100-byte header from page 1 (use GetPage to avoid size mismatch)
        var page1 = _rawSource.GetPage(1);
        var headerBuf = page1[..SQLiteLayout.DatabaseHeaderSize];

        // 3. Parse and check cookie
        var currentHeader = DatabaseHeader.Parse(headerBuf);
        if (currentHeader.SchemaCookie != _lastValidatedCookie)
        {
            _header = currentHeader;
            _lastValidatedCookie = currentHeader.SchemaCookie;
            InvalidateSchema();
        }
    }

    /// <summary>
    /// Creates a new, empty Sharc database valid for both engine and trust usage.
    /// Includes system tables: _sharc_ledger, _sharc_agents.
    /// </summary>
    /// <param name="path">The file path to create.</param>
    /// <returns>An open database instance with Write mode enabled.</returns>
    public static SharcDatabase Create(string path) => SharcDatabaseFactory.Create(path);

    /// <summary>
    /// Creates a new, empty Sharc database entirely in memory (no filesystem access).
    /// Suitable for Blazor WASM and other environments without file I/O.
    /// </summary>
    /// <returns>An open database instance with Write mode enabled.</returns>
    public static SharcDatabase CreateInMemory() => SharcDatabaseFactory.CreateInMemory();

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
    /// Registers a programmatic <see cref="Views.SharcView"/> by name.
    /// Registered views are accessible via <see cref="OpenView"/> and
    /// resolvable in <see cref="Query(string)"/> SQL statements.
    /// If a view with the same name already exists, it is overwritten.
    /// </summary>
    /// <param name="view">The view to register. Must not be null.</param>
    public void RegisterView(Views.SharcView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _viewResolver.RegisterView(view);
    }

    /// <summary>
    /// Returns the names of all currently registered programmatic views.
    /// </summary>
    public IReadOnlyCollection<string> ListRegisteredViews()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _viewResolver.ListRegisteredViews();
    }

    /// <summary>
    /// Removes a previously registered programmatic view by name.
    /// </summary>
    /// <param name="viewName">The name of the view to remove.</param>
    /// <returns>True if the view was found and removed; false otherwise.</returns>
    public bool UnregisterView(string viewName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _viewResolver.UnregisterView(viewName);
    }

    /// <summary>
    /// Opens a named view as a zero-allocation <see cref="Views.IViewCursor"/>.
    /// Checks registered programmatic views first, then falls back to
    /// auto-promotable SQLite views in <c>sqlite_schema</c>.
    /// </summary>
    /// <param name="viewName">Name of the view to open.</param>
    /// <returns>A forward-only cursor over the view's projected rows.</returns>
    /// <exception cref="KeyNotFoundException">The view does not exist or is not auto-promotable.</exception>
    public Views.IViewCursor OpenView(string viewName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _viewResolver.OpenView(viewName);
    }

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

        // ── ThreadStatic reader pool: zero-alloc reuse for unfiltered, unprojected reads ──
        // Only eligible when no projection and no filters — the common PointLookup/scan case.
        bool isPoolable = columns is null or { Length: 0 }
                       && filters is null or { Length: 0 }
                       && filter is null;

        if (isPoolable)
        {
            uint rootPage = (uint)table.RootPage;
            var pooled = SharcDataReader.TryRentFromPool(rootPage, _recordDecoder);
            if (pooled != null)
            {
                pooled.ReactivateFromPool();
                return pooled;
            }
        }

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
            return new SharcDataReader(cursor, _recordDecoder, new SharcDataReader.CursorReaderConfig
            {
                Columns = table.Columns,
                Projection = projection,
                BTreeReader = _bTreeReader,
                TableIndexes = table.Indexes,
                FilterNode = node
            });
        }

        var reader = new SharcDataReader(cursor, _recordDecoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            Projection = projection,
            BTreeReader = _bTreeReader,
            TableIndexes = table.Indexes,
            Filters = ResolveFilters(table, filters)
        });

        if (isPoolable)
            reader.MarkPoolable((uint)table.RootPage);

        return reader;
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
    /// Creates a pre-resolved reader handle for zero-allocation repeated Seek/Read calls.
    /// Schema, projection, and cursor are resolved once; subsequent <see cref="PreparedReader.CreateReader"/>
    /// calls reuse the same reader and buffers with zero per-call allocation.
    /// </summary>
    /// <param name="tableName">The table to read from.</param>
    /// <param name="columns">Optional column projection. If empty or null, all columns are returned.</param>
    /// <returns>A <see cref="PreparedReader"/> that can produce zero-alloc readers.</returns>
    public PreparedReader PrepareReader(string tableName, params string[]? columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();
        var table = schema.GetTable(tableName) ?? throw new KeyNotFoundException($"Table '{tableName}' not found.");

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

        var config = new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            Projection = projection,
            BTreeReader = _bTreeReader,
            TableIndexes = table.Indexes
        };

        return new PreparedReader(this, table, config);
    }

    /// <summary>
    /// Creates a <see cref="PreparedAgent.Builder"/> for composing read + write steps
    /// into a coordinated, reusable operation.
    /// </summary>
    /// <returns>A fluent builder for constructing a <see cref="PreparedAgent"/>.</returns>
    public PreparedAgent.Builder PrepareAgent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PreparedAgent.Builder();
    }

    /// <summary>
    /// Executes a Sharq query string and returns a data reader.
    /// Compiled plans are cached for zero-overhead repeat queries.
    /// </summary>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT name FROM users WHERE age > 18").</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="Sharc.Query.Sharq.SharqParseException">The query string is invalid.</exception>
    /// <exception cref="ArgumentException">Referenced table or column not found in schema.</exception>
    public SharcDataReader Query(string sharqQuery)
    {
        // Fast hint bypass: detect "CACHED " or "JIT " prefix, route directly to
        // cached PreparedQuery/JitQuery. Eliminates QueryCore frame, GetOrCompilePlan
        // (ConcurrentDictionary), TryRoute switch, and intent-keyed Dictionary overhead.
        // Returns null for unsupported queries (compound/CTE/JOIN) → fall through.
        if (sharqQuery.Length > 4)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            char c = sharqQuery[0];
            if ((c is 'C' or 'c') && sharqQuery.StartsWith("CACHED ", StringComparison.OrdinalIgnoreCase))
            {
                var result = _executionRouter.ExecuteCachedDirect(this, sharqQuery, null);
                if (result != null) return result;
            }
            else if ((c is 'J' or 'j') && sharqQuery.StartsWith("JIT ", StringComparison.OrdinalIgnoreCase))
            {
                var result = _executionRouter.ExecuteJitDirect(this, sharqQuery, null);
                if (result != null) return result;
            }
        }
        return QueryCore(null, sharqQuery, null);
    }

    /// <summary>
    /// Executes a Sharq query string with parameter bindings and returns a data reader.
    /// Compiled plans are cached; parameters are resolved at execution time.
    /// </summary>
    /// <param name="parameters">Named parameters to bind (e.g. { ["minAge"] = 25L }).</param>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT * FROM users WHERE age = $minAge").</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    public SharcDataReader Query(IReadOnlyDictionary<string, object>? parameters, string sharqQuery)
    {
        if (sharqQuery.Length > 4)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            char c = sharqQuery[0];
            if ((c is 'C' or 'c') && sharqQuery.StartsWith("CACHED ", StringComparison.OrdinalIgnoreCase))
            {
                var result = _executionRouter.ExecuteCachedDirect(this, sharqQuery, parameters);
                if (result != null) return result;
            }
            else if ((c is 'J' or 'j') && sharqQuery.StartsWith("JIT ", StringComparison.OrdinalIgnoreCase))
            {
                var result = _executionRouter.ExecuteJitDirect(this, sharqQuery, parameters);
                if (result != null) return result;
            }
        }
        return QueryCore(parameters, sharqQuery, null);
    }

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

    /// <summary>
    /// Pre-compiles a Sharq query into a <see cref="PreparedQuery"/> that can be
    /// executed repeatedly without re-parsing, re-resolving views, or re-compiling filters.
    /// </summary>
    /// <param name="sharqQuery">A Sharq SELECT statement (e.g. "SELECT name FROM users WHERE age = $targetAge").</param>
    /// <returns>A <see cref="PreparedQuery"/> handle. Call <see cref="PreparedQuery.Execute()"/> to run.</returns>
    /// <exception cref="NotSupportedException">
    /// The query contains compound operations (UNION/INTERSECT/EXCEPT), CTEs, or JOINs.
    /// Phase 1 only supports simple single-table SELECT queries.
    /// </exception>
    public PreparedQuery Prepare(string sharqQuery)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSchemaUpToDate();

        var plan = Intent.IntentCompiler.CompilePlan(sharqQuery);

        if (plan.IsCompound || plan.HasCotes)
            throw new NotSupportedException(
                "PreparedQuery does not support compound queries (UNION/INTERSECT/EXCEPT) or CTEs. " +
                "Use db.Query() for these query types.");

        var intent = plan.Simple!;

        if (intent.HasJoins)
            throw new NotSupportedException(
                "PreparedQuery does not support JOIN queries. Use db.Query() for joins.");

        var schema = GetSchema();
        var table = schema.GetTable(intent.TableName)
            ?? throw new KeyNotFoundException($"Table '{intent.TableName}' not found.");

        string[]? columns = intent.HasAggregates
            ? AggregateProjection.Compute(intent)
            : intent.ColumnsArray;

        int[]? projection = null;
        if (columns is { Length: > 0 })
        {
            projection = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                string colName = columns[i];
                int ordinal = table.GetColumnOrdinal(colName);

                if (ordinal < 0 && !string.IsNullOrEmpty(intent.TableAlias) &&
                    colName.StartsWith(intent.TableAlias + ".", StringComparison.OrdinalIgnoreCase))
                {
                    var stripped = colName.Substring(intent.TableAlias.Length + 1);
                    ordinal = table.GetColumnOrdinal(stripped);
                }

                projection[i] = ordinal >= 0 ? ordinal
                    : throw new ArgumentException($"Column '{colName}' not found in table '{table.Name}'.");
            }
        }

        int rowidAlias = FindIntegerPrimaryKeyOrdinal(table.Columns);

        // Pre-compile filter for non-parameterized intents
        IFilterNode? staticFilter = null;
        if (intent.Filter.HasValue && !HasParameterNodes(intent.Filter.Value))
        {
            var filterStar = IntentToFilterBridge.Build(
                intent.Filter.Value, null, intent.TableAlias);
            staticFilter = FilterTreeCompiler.CompileBaked(filterStar, table.Columns, rowidAlias);
        }

        bool needsPostProcessing = intent.HasAggregates
            || intent.IsDistinct
            || intent.OrderBy is { Count: > 0 }
            || intent.Limit.HasValue
            || intent.Offset.HasValue;

        return new PreparedQuery(
            this, table, projection, rowidAlias,
            staticFilter, intent, needsPostProcessing);
    }

    /// <summary>
    /// Core query execution pipeline: parse/cache → resolve views → enforce entitlements → dispatch.
    /// Simple queries go through the compiled-query cache for zero-overhead repeats.
    /// Compound/Cote queries are dispatched to <see cref="CompoundQueryExecutor"/>.
    /// JOIN queries are dispatched to <see cref="JoinExecutor"/>.
    /// </summary>
    private SharcDataReader QueryCore(
        IReadOnlyDictionary<string, object>? parameters,
        string sharqQuery,
        Core.Trust.AgentInfo? agent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Plan cache lookup BEFORE schema check — plan compilation only needs SQL text.
        // CACHED/JIT hints return immediately, skipping EnsureSchemaUpToDate entirely.
        // This eliminates the page-1 invalidation + re-read overhead (~2-3 us) for
        // all hinted queries, since their schema was validated at Prepare/Jit time.
        var cache = _queryCache ??= new QueryPlanCache();
        var plan = cache.GetOrCompilePlan(sharqQuery);

        // Hint routing: CACHED → auto-Prepare, JIT → auto-Jit
        var hinted = _executionRouter.TryRoute(this, plan, sharqQuery, parameters);
        if (hinted != null)
            return hinted;

        // DIRECT path: full schema validation required
        EnsureSchemaUpToDate();

        // Resolve views (recursively rewrites view tables as Cotes)
        // Note: registered views with filters are NOT cacheable in the plan
        // because they require pre-materialization on every call.
        CoteMap? preMaterialized = null;

        // DECISION: effectiveGen = -1 sentinel when no views are registered.
        // This ensures that a cached plan resolved WITH views is invalidated
        // when all views are unregistered, since -1 != any real generation counter.
        int effectiveGen = _viewResolver.EffectiveGeneration;

        if (plan.ResolvedViewPlan != null && plan.ResolvedViewGeneration == effectiveGen)
        {
            plan = plan.ResolvedViewPlan;
        }
        else
        {
            var resolved = _viewResolver.ResolveViews(plan);
            plan.ResolvedViewPlan = resolved;
            plan.ResolvedViewGeneration = effectiveGen;
            plan = resolved;
        }

        // Pre-materialize only views with Func filters (not expressible as SQL).
        // Filter-free views stay as null, enabling the lazy Cote resolution path
        // in CompoundQueryExecutor — ORDER BY column merging and HasJoins guards
        // ensure correctness without forcing full materialization.
        if (_viewResolver.HasRegisteredViews)
            preMaterialized = _viewResolver.PreMaterializeFilteredViews(plan);

        // Enforce entitlements when an agent is specified
        if (agent is not null)
        {
            var tables = TableReferenceCollector.Collect(plan);
            Trust.EntitlementEnforcer.EnforceAll(agent, tables);
        }

        // Compound or Cote queries go through the compound executor
        if (plan.IsCompound || plan.HasCotes)
            return CompoundQueryExecutor.Execute(this, plan, parameters, preMaterialized);

        // Simple query — direct execution with compiled-query cache
        var intent = plan.Simple!;

        if (intent.HasJoins)
            return JoinExecutor.Execute(this, intent, parameters);

        var reader = CreateReaderFromIntent(intent, parameters);
        return QueryPostProcessor.Apply(reader, intent);
    }

    /// <summary>
    /// Creates a <see cref="SharcDataReader"/> from a parsed <see cref="Intent.QueryIntent"/>.
    /// Resolves columns, applies filters, and handles column projection before returning
    /// a cursor positioned at the start of the result set.
    /// </summary>
    internal SharcDataReader CreateReaderFromIntent(
        Intent.QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var readerCache = _readerInfoCache ??= new Dictionary<Intent.QueryIntent, CachedReaderInfo>();

        // Build or retrieve cached reader info (projection, table, filter for non-parameterized)
        if (!readerCache.TryGetValue(intent, out var info))
        {
            var schema = GetSchema();
            var table = schema.GetTable(intent.TableName)
                ?? throw new KeyNotFoundException($"Table '{intent.TableName}' not found.");

            string[]? columns = intent.HasAggregates
                ? AggregateProjection.Compute(intent)
                : intent.ColumnsArray;

            int[]? projection = null;
            if (columns is { Length: > 0 })
            {
                projection = new int[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                {
                    string colName = columns[i];
                    int ordinal = table.GetColumnOrdinal(colName);
                    
                    if (ordinal < 0 && !string.IsNullOrEmpty(intent.TableAlias) && 
                        colName.StartsWith(intent.TableAlias + ".", StringComparison.OrdinalIgnoreCase))
                    {
                         // Strip alias and try again
                         var stripped = colName.Substring(intent.TableAlias.Length + 1);
                         ordinal = table.GetColumnOrdinal(stripped);
                    }

                    projection[i] = ordinal >= 0 ? ordinal
                        : throw new ArgumentException($"Column '{colName}' not found in table '{table.Name}'.");
                }
            }

            int rowidAlias = FindIntegerPrimaryKeyOrdinal(table.Columns);

            // Pre-compile filter for non-parameterized intents
            IFilterNode? filterNode = null;
            if (intent.Filter.HasValue && !HasParameterNodes(intent.Filter.Value))
            {
                var filterStar = IntentToFilterBridge.Build(
                    intent.Filter.Value, null, intent.TableAlias);
                filterNode = FilterTreeCompiler.CompileBaked(filterStar, table.Columns, rowidAlias);
            }

            info = new CachedReaderInfo
            {
                FilterNode = filterNode,
                Projection = projection,
                Table = table,
                RowidAliasOrdinal = rowidAlias,
            };
            readerCache[intent] = info;
        }

        // Determine filter node to use
        IFilterNode? node = info.FilterNode;

        if (node == null && intent.Filter.HasValue)
        {
            // Parameterized filter — check param-level cache
            var paramCache = _paramFilterCache ??= new();
            long paramKey = ComputeParamCacheKey(intent, parameters);
            if (!paramCache.TryGetValue(paramKey, out node))
            {
                var filterStar = IntentToFilterBridge.Build(
                    intent.Filter.Value, parameters, intent.TableAlias);
                node = FilterTreeCompiler.CompileBaked(
                    filterStar, info.Table.Columns, info.RowidAliasOrdinal);
                paramCache[paramKey] = node;
            }
        }

        var cursor = TryCreateIndexSeekCursor(intent, info.Table) ?? CreateTableCursor(info.Table);
        return new SharcDataReader(cursor, _recordDecoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = info.Table.Columns,
            Projection = info.Projection,
            BTreeReader = _bTreeReader,
            TableIndexes = info.Table.Indexes,
            FilterNode = node
        });
    }

    internal static bool HasParameterNodes(Intent.PredicateIntent predicate)
    {
        foreach (ref readonly var node in predicate.Nodes.AsSpan())
        {
            if (node.Value.Kind == Intent.IntentValueKind.Parameter)
                return true;
        }
        return false;
    }

    private static long ComputeParamCacheKey(
        Intent.QueryIntent intent,
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
        return new SharcDataReader(cursor, _recordDecoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = table.Columns,
            Projection = projection,
            BTreeReader = _bTreeReader,
            TableIndexes = tableIndexes,
            FilterNode = filterNode
        });
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

    /// <summary>
    /// Attempts to create an index-accelerated cursor for the given query intent.
    /// Returns null if no suitable index exists or conditions aren't sargable.
    /// </summary>
    private IndexSeekCursor? TryCreateIndexSeekCursor(Intent.QueryIntent intent, TableInfo table)
    {
        if (!intent.Filter.HasValue || table.Indexes.Count == 0)
            return null;

        var conditions = PredicateAnalyzer.ExtractSargableConditions(
            intent.Filter.Value, intent.TableAlias);
        if (conditions.Count == 0)
            return null;

        var plan = IndexSelector.SelectBestIndex(conditions, table.Indexes);
        if (plan.Index == null)
            return null;

        // DECISION: Keep the compiled residual filter even for consumed index conditions.
        // The byte-level filter is near-zero cost and avoids predicate subtraction complexity.
        return new IndexSeekCursor(
            _bTreeReader, _recordDecoder,
            (uint)plan.Index.RootPage, (uint)table.RootPage, plan);
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

    // ─── JitQuery factory ──────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="JitQuery"/> bound to the specified table or view.
    /// Tables take precedence over views with the same name. Pre-resolves
    /// schema at creation time for reuse across all operations.
    /// </summary>
    /// <param name="name">The name of the table or view to bind to.</param>
    /// <returns>A new <see cref="JitQuery"/> instance.</returns>
    /// <exception cref="KeyNotFoundException">Neither a table nor a view with the given name exists.</exception>
    public JitQuery Jit(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var schema = GetSchema();

        // Try table first — tables take precedence over views
        var table = schema.TryGetTable(name);
        if (table != null)
        {
            int rowidAlias = FindIntegerPrimaryKeyOrdinal(table.Columns);
            return new JitQuery(this, table, rowidAlias);
        }

        // Fall back to views (registered first, then SQLite schema)
        var view = _viewResolver.TryResolveView(name)
            ?? throw new KeyNotFoundException($"Table or view '{name}' not found.");
        return new JitQuery(this, view);
    }

    /// <summary>
    /// Creates a <see cref="JitQuery"/> backed by the specified <see cref="Views.ILayer"/>.
    /// Layer-backed JitQuery instances are read-only — mutations throw <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="layer">The layer (view or custom source) to bind to.</param>
    /// <returns>A new layer-backed <see cref="JitQuery"/> instance.</returns>
    public JitQuery Jit(Views.ILayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(layer);
        return new JitQuery(this, layer);
    }

    // ─── Internal accessors for PreparedQuery / JitQuery ────────────
    internal IRecordDecoder Decoder => _recordDecoder;
    internal IBTreeReader BTreeReaderInternal => _bTreeReader;

    internal IndexSeekCursor? TryCreateIndexSeekCursorForPrepared(
        Intent.QueryIntent intent, TableInfo table)
        => TryCreateIndexSeekCursor(intent, table);

    internal IBTreeCursor CreateTableCursorForPrepared(TableInfo table)
        => CreateTableCursor(table);

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

    private static IReadOnlyList<IndexInfo> GetTableIndexes(SharcSchema schema, string tableName)
    {
        return schema.GetTable(tableName).Indexes;
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

    /// <summary>
    /// Begins a transaction that reuses a pooled ShadowPageSource.
    /// A fresh BTreeMutator is created per-transaction to avoid stale freelist state.
    /// </summary>
    internal Transaction BeginPooledTransaction(ShadowPageSource cachedShadow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeTransaction != null)
            throw new InvalidOperationException("A transaction is already active.");

        if (_rawSource is not IWritablePageSource writable)
            throw new NotSupportedException("The database is opened in read-only mode.");

        cachedShadow.Reset();

        _activeTransaction = new Transaction(this, writable, cachedShadow);
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
        _executionRouter.Dispose();
        _activeTransaction?.Dispose();
        _proxySource.Dispose();
        _rawSource.Dispose();
        _keyHandle?.Dispose();
    }
}
