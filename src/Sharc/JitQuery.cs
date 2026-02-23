/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Schema;
using Sharc.Query;
using Sharc.Query.Intent;
using Sharc.Views;
using QueryPost = global::Sharc.Query.QueryPostProcessor;

namespace Sharc;

/// <summary>
/// A first-class query and mutation handle that accumulates filters,
/// limit/offset, and supports reads, writes, transaction binding, and freezing
/// to an immutable <see cref="PreparedQuery"/>.
/// </summary>
/// <remarks>
/// <para>Created via <see cref="SharcDatabase.Jit(string)"/> (table or view by name),
/// <see cref="SharcDatabase.Jit(ILayer)"/> (layer/view-backed), or
/// <see cref="SharcDatabase.Jit(string)"/> (table-backed). Pre-resolves source schema
/// at creation time and reuses it across all operations.</para>
/// <para>JitQuery is designed to be long-lived — it can be stored as a member variable,
/// field, or global and reused across multiple calls. Use <see cref="ClearFilters"/>
/// to reset accumulated filters for reuse with different criteria.</para>
/// <para>View-backed JitQuery instances are read-only — mutations throw
/// <see cref="NotSupportedException"/>.</para>
/// <para>This type is <b>not thread-safe</b>. Each instance should be used from a single thread.</para>
/// </remarks>
public sealed class JitQuery : IPreparedReader, IPreparedWriter
{
    private SharcDatabase? _db;

    // Pre-resolved at creation (never re-computed) — null when view-backed
    internal readonly TableInfo? Table;
    internal readonly int RowidAliasOrdinal;

    // Layer source — non-null when view/layer-backed (DIP: depends on ILayer, not SharcView)
    private ILayer? _sourceLayer;

    // Mutable filter accumulation
    private List<IFilterStar>? _filters;
    private long? _limit;
    private long? _offset;

    // ── Compiled-state cache (version-based staleness, matches BTreeCursor.IsStale pattern) ──
    // Avoids recompiling filters and re-resolving projections on repeated Query() calls.
    // _filterVersion is bumped on Where() and ClearFilters(); the compiled cache is stale
    // when _compiledFilterVersion != _filterVersion. Monotonic — no bool to get out of sync.
    private int _filterVersion;                            // bumped on every filter mutation
    private int _compiledFilterVersion = -1;               // version at which cache was compiled
    private IFilterNode? _cachedFilterNode;                // table-backed compiled filter
    private Func<IRowAccessor, bool>? _cachedViewFilter;   // view-backed compiled filter
    private string[]? _cachedViewColumnNames;              // view column names (stable per view)
    private int[]? _cachedProjection;                      // last resolved projection ordinals
    private string[]? _cachedProjectionColumns;            // column names that produced _cachedProjection

    /// <summary>
    /// True when the compiled filter cache is out of date with accumulated filters.
    /// Mirrors the <c>BTreeCursor.IsStale</c> / <c>DataVersion</c> pattern used throughout Sharc.
    /// </summary>
    private bool IsFilterStale => _compiledFilterVersion != _filterVersion;

    // Cached cursor + reader for table-backed queries: created on first Query(),
    // reused via Reset() on subsequent calls when projection hasn't changed.
    private IBTreeCursor? _cachedTableCursor;
    private SharcDataReader? _cachedTableReader;
    private int[]? _cachedReaderProjection; // projection at time of reader creation (reference equality)

    // Optional transaction binding for mutations
    private SharcWriteTransaction? _transaction;

    // Root page cache for auto-commit mutations
    private Dictionary<string, uint>? _rootCache;

    /// <summary>Creates a table-backed JitQuery.</summary>
    internal JitQuery(SharcDatabase db, TableInfo table, int rowidAliasOrdinal)
    {
        _db = db;
        Table = table;
        RowidAliasOrdinal = rowidAliasOrdinal;
    }

    /// <summary>Creates a layer-backed (view-backed) JitQuery.</summary>
    internal JitQuery(SharcDatabase db, ILayer source)
    {
        _db = db;
        _sourceLayer = source;
        RowidAliasOrdinal = -1;
    }

    // ── Filter Composition (fluent) ──────────────────────────────────

    /// <summary>
    /// Adds a filter predicate. Multiple calls are AND-composed.
    /// </summary>
    public JitQuery Where(IFilterStar filter)
    {
        _filters ??= new List<IFilterStar>();
        _filters.Add(filter);
        _filterVersion++;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to return from <see cref="Query"/>.
    /// </summary>
    public JitQuery WithLimit(long limit)
    {
        _limit = limit;
        return this;
    }

    /// <summary>
    /// Sets the number of rows to skip from <see cref="Query"/>.
    /// </summary>
    public JitQuery WithOffset(long offset)
    {
        _offset = offset;
        return this;
    }

    /// <summary>
    /// Resets all accumulated filters, limit, and offset for reuse.
    /// </summary>
    public JitQuery ClearFilters()
    {
        _filters?.Clear();
        _limit = null;
        _offset = null;
        _filterVersion++;
        _cachedFilterNode = null;
        _cachedViewFilter = null;
        _cachedProjection = null;
        _cachedProjectionColumns = null;
        return this;
    }

    // ── Read ─────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a scan with accumulated filters and returns a data reader.
    /// Optionally projects to the specified columns.
    /// </summary>
    /// <param name="columns">Column names to project, or empty/null for all columns.</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="ObjectDisposedException">The JitQuery has been disposed.</exception>
    public SharcDataReader Query(params string[]? columns)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);

        if (_sourceLayer != null)
            return QueryFromView(columns);

        return QueryFromTable(columns);
    }

    /// <summary>
    /// Implements <see cref="IPreparedReader.Execute"/>. Delegates to <see cref="Query(string[])"/>
    /// with no projection (all columns).
    /// </summary>
    SharcDataReader IPreparedReader.Execute() => Query();

    /// <summary>
    /// Re-executes the query with the same projection as the last <see cref="Query"/> call.
    /// Eliminates the <c>params string[]</c> allocation (48 B) and projection resolution overhead.
    /// Requires at least one prior <see cref="Query"/> call to establish the projection.
    /// </summary>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="ObjectDisposedException">The JitQuery has been disposed.</exception>
    /// <exception cref="InvalidOperationException">No prior Query() call to establish projection.</exception>
    public SharcDataReader QuerySameProjection()
    {
        ObjectDisposedException.ThrowIf(_db is null, this);

        if (_cachedTableReader == null && _cachedReaderProjection == null && _cachedProjection == null)
            throw new InvalidOperationException(
                "QuerySameProjection() requires a prior Query() call to establish the projection.");

        // Reuse the cached projection directly — no params array, no ResolveProjection
        return QueryFromTableDirect();
    }

    /// <summary>
    /// Fast path for QuerySameProjection — skips projection resolution entirely.
    /// Uses the cached <see cref="_cachedReaderProjection"/> (or <see cref="_cachedProjection"/>).
    /// </summary>
    private SharcDataReader QueryFromTableDirect()
    {
        var db = _db!;
        IFilterNode? filterNode = CompileFilters();

        // Fastest path: reuse cached cursor + reader (same as QueryFromTable fast path)
        if (_cachedTableReader != null && !_limit.HasValue && !_offset.HasValue)
        {
            _cachedTableReader.ResetForReuse(filterNode);
            return _cachedTableReader;
        }

        // Fall through to full creation with cached projection
        int[]? projection = _cachedReaderProjection ?? _cachedProjection;
        return QueryFromTableCreate(db, filterNode, projection);
    }

    private SharcDataReader QueryFromTable(string[]? columns)
    {
        var db = _db!;

        // Compile accumulated filters
        IFilterNode? filterNode = CompileFilters();

        // Resolve projection
        int[]? projection = ResolveProjection(columns);

        // Fast path: reuse cached cursor + reader if projection is unchanged
        // and no LIMIT/OFFSET (which wraps the reader in post-processing).
        // Reference equality on projection works because ResolveProjection caches its result.
        if (_cachedTableReader != null
            && !_limit.HasValue && !_offset.HasValue
            && ReferenceEquals(_cachedReaderProjection, projection))
        {
            _cachedTableReader.ResetForReuse(filterNode);
            return _cachedTableReader;
        }

        return QueryFromTableCreate(db, filterNode, projection);
    }

    /// <summary>Creates cursor + reader, caches for reuse. Shared by QueryFromTable and QueryFromTableDirect.</summary>
    private SharcDataReader QueryFromTableCreate(SharcDatabase db, IFilterNode? filterNode, int[]? projection)
    {
        // Dispose previous cached reader if projection changed
        if (_cachedTableReader != null)
        {
            _cachedTableReader.DisposeForReal();
            _cachedTableCursor?.Dispose();
            _cachedTableReader = null;
            _cachedTableCursor = null;
        }

        var cursor = db.CreateTableCursorForPrepared(Table!);

        var reader = new SharcDataReader(cursor, db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = Table!.Columns,
            Projection = projection,
            BTreeReader = db.BTreeReaderInternal,
            TableIndexes = Table.Indexes,
            FilterNode = filterNode
        });

        // Apply LIMIT/OFFSET if set — can't cache (post-processor wraps the reader)
        if (_limit.HasValue || _offset.HasValue)
        {
            var intent = new QueryIntent
            {
                TableName = Table.Name,
                Limit = _limit,
                Offset = _offset
            };
            return QueryPost.Apply(reader, intent);
        }

        // Cache for reuse on subsequent calls
        reader.MarkReusable();
        _cachedTableCursor = cursor;
        _cachedTableReader = reader;
        _cachedReaderProjection = projection;

        return reader;
    }

    private SharcDataReader QueryFromView(string[]? columns)
    {
        var db = _db!;
        var cursor = _sourceLayer!.Open(db);

        // Get column metadata — cache on first call (view columns are stable)
        int fieldCount = cursor.FieldCount;
        string[] allColumnNames;
        if (_cachedViewColumnNames != null)
        {
            allColumnNames = _cachedViewColumnNames;
        }
        else
        {
            allColumnNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                allColumnNames[i] = cursor.GetColumnName(i);
            _cachedViewColumnNames = allColumnNames;
        }

        // Resolve projection — cache if same columns as last call
        string[] outputColumnNames;
        int[]? projection = null;
        if (columns is { Length: > 0 })
        {
            if (_cachedProjection != null && ProjectionColumnsMatch(columns, _cachedProjectionColumns))
            {
                projection = _cachedProjection;
            }
            else
            {
                projection = new int[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                {
                    int found = -1;
                    for (int j = 0; j < allColumnNames.Length; j++)
                    {
                        if (allColumnNames[j].Equals(columns[i], StringComparison.OrdinalIgnoreCase))
                        { found = j; break; }
                    }
                    projection[i] = found >= 0
                        ? found
                        : throw new ArgumentException($"Column '{columns[i]}' not found in view '{_sourceLayer.Name}'.");
                }
                _cachedProjection = projection;
                _cachedProjectionColumns = columns;
            }
            outputColumnNames = columns;
        }
        else
        {
            outputColumnNames = allColumnNames;
        }

        // Build JitQuery filter — cache via version-based staleness
        Func<IRowAccessor, bool>? jitFilter = null;
        if (_filters is { Count: > 0 })
        {
            if (!IsFilterStale && _cachedViewFilter != null)
            {
                jitFilter = _cachedViewFilter;
            }
            else
            {
                IFilterStar expression = _filters.Count == 1
                    ? _filters[0]
                    : FilterStar.And(_filters.ToArray());
                jitFilter = ViewFilterBridge.Convert(expression, allColumnNames);
                _cachedViewFilter = jitFilter;
                _compiledFilterVersion = _filterVersion;
            }
        }
        else if (IsFilterStale)
        {
            _cachedViewFilter = null;
            _compiledFilterVersion = _filterVersion;
        }

        // Stream rows from cursor → QueryValue[] → SharcDataReader
        var rows = StreamViewRows(cursor, fieldCount, projection, jitFilter);
        var reader = new SharcDataReader(rows, outputColumnNames);

        // Apply LIMIT/OFFSET if set
        if (_limit.HasValue || _offset.HasValue)
        {
            var intent = new QueryIntent
            {
                TableName = _sourceLayer.Name,
                Limit = _limit,
                Offset = _offset
            };
            return QueryPost.Apply(reader, intent);
        }

        return reader;
    }

    private static IEnumerable<QueryValue[]> StreamViewRows(
        IViewCursor cursor, int fieldCount, int[]? projection,
        Func<IRowAccessor, bool>? filter)
    {
        try
        {
            while (cursor.MoveNext())
            {
                if (filter != null && !filter(cursor))
                    continue;

                int outputCount = projection?.Length ?? fieldCount;
                var row = new QueryValue[outputCount];
                for (int i = 0; i < outputCount; i++)
                {
                    int srcOrdinal = projection != null ? projection[i] : i;
                    if (cursor.IsNull(srcOrdinal))
                    {
                        row[i] = QueryValue.Null;
                        continue;
                    }
                    row[i] = cursor.GetColumnType(srcOrdinal) switch
                    {
                        SharcColumnType.Integral => QueryValue.FromInt64(cursor.GetInt64(srcOrdinal)),
                        SharcColumnType.Real => QueryValue.FromDouble(cursor.GetDouble(srcOrdinal)),
                        SharcColumnType.Text => QueryValue.FromString(cursor.GetString(srcOrdinal)),
                        SharcColumnType.Blob => QueryValue.FromBlob(cursor.GetBlob(srcOrdinal)),
                        _ => QueryValue.Null,
                    };
                }
                yield return row;
            }
        }
        finally
        {
            cursor.Dispose();
        }
    }

    // ── Mutations ────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a record into the bound table.
    /// If a transaction is bound via <see cref="WithTransaction"/>, uses that transaction.
    /// Otherwise auto-commits.
    /// </summary>
    /// <param name="values">Column values for the new row.</param>
    /// <returns>The assigned rowid.</returns>
    /// <exception cref="NotSupportedException">The JitQuery is backed by a view.</exception>
    public long Insert(params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);
        ThrowIfViewBacked();

        if (_transaction != null)
            return _transaction.Insert(Table!.Name, values);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        _rootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        long rowId = SharcWriter.InsertCore(tx, Table!.Name, values, Table, _rootCache);
        tx.Commit();
        return rowId;
    }

    /// <summary>
    /// Deletes a record by rowid from the bound table.
    /// If a transaction is bound via <see cref="WithTransaction"/>, uses that transaction.
    /// Otherwise auto-commits.
    /// </summary>
    /// <param name="rowId">The rowid of the row to delete.</param>
    /// <returns>True if the row existed and was removed.</returns>
    /// <exception cref="NotSupportedException">The JitQuery is backed by a view.</exception>
    public bool Delete(long rowId)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);
        ThrowIfViewBacked();

        if (_transaction != null)
            return _transaction.Delete(Table!.Name, rowId);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        _rootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        bool found = SharcWriter.DeleteCore(tx, Table!.Name, rowId, _rootCache);
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Updates a record by rowid in the bound table.
    /// If a transaction is bound via <see cref="WithTransaction"/>, uses that transaction.
    /// Otherwise auto-commits.
    /// </summary>
    /// <param name="rowId">The rowid of the row to update.</param>
    /// <param name="values">New column values for the row.</param>
    /// <returns>True if the row existed and was updated.</returns>
    /// <exception cref="NotSupportedException">The JitQuery is backed by a view.</exception>
    public bool Update(long rowId, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);
        ThrowIfViewBacked();

        if (_transaction != null)
            return _transaction.Update(Table!.Name, rowId, values);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        _rootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        bool found = SharcWriter.UpdateCore(tx, Table!.Name, rowId, values, Table, _rootCache);
        tx.Commit();
        return found;
    }

    // ── Transaction Binding ──────────────────────────────────────────

    /// <summary>
    /// Binds this JitQuery to an explicit transaction. All subsequent mutations
    /// will use this transaction until <see cref="DetachTransaction"/> is called.
    /// </summary>
    public JitQuery WithTransaction(SharcWriteTransaction tx)
    {
        _transaction = tx;
        return this;
    }

    /// <summary>
    /// Detaches the bound transaction, reverting to auto-commit mode for mutations.
    /// </summary>
    public JitQuery DetachTransaction()
    {
        _transaction = null;
        return this;
    }

    // ── Freeze ───────────────────────────────────────────────────────

    /// <summary>
    /// Freezes the current accumulated state (filters, projection) into an immutable
    /// <see cref="PreparedQuery"/> for maximum repeated execution speed.
    /// </summary>
    /// <param name="columns">Column names to project, or empty/null for all columns.</param>
    /// <returns>A new <see cref="PreparedQuery"/> capturing the current JitQuery state.</returns>
    /// <exception cref="NotSupportedException">The JitQuery is backed by a view.</exception>
    public PreparedQuery ToPrepared(params string[]? columns)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);
        ThrowIfViewBacked();
        var db = _db;

        // Compile current filters into a static filter node
        IFilterNode? staticFilter = CompileFilters();

        // Resolve projection
        int[]? projection = ResolveProjection(columns);

        bool needsPostProcessing = _limit.HasValue || _offset.HasValue;

        // Build a synthetic QueryIntent from accumulated state
        var intent = new QueryIntent
        {
            TableName = Table!.Name,
            Columns = columns is { Length: > 0 } ? columns : null,
            Limit = _limit,
            Offset = _offset
        };

        return new PreparedQuery(db, Table, projection, RowidAliasOrdinal,
            staticFilter, intent, needsPostProcessing);
    }

    // ── View Export ──────────────────────────────────────────────────

    /// <summary>
    /// Exports the current accumulated state (filters, projection) as a transient
    /// <see cref="SharcView"/>. The returned view is detached — it is not automatically
    /// registered with the database. Register it via <see cref="SharcDatabase.RegisterView"/>
    /// if needed, or let it be garbage-collected when no longer referenced.
    /// </summary>
    /// <param name="name">Name for the transient view.</param>
    /// <param name="columns">Column names to project, or empty/null for all columns.</param>
    /// <returns>A new <see cref="SharcView"/> capturing the current JitQuery state.</returns>
    public SharcView AsView(string name, params string[]? columns)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);

        // Determine the view's column names for filter ordinal resolution.
        // The filter runs against the view cursor, whose ordinals are positional
        // (0, 1, 2, ...) based on the projected columns, NOT the table ordinals.
        string[] viewColumnNames;
        if (_sourceLayer != null)
        {
            // View-backed: probe cursor for column names
            using var probe = _sourceLayer.Open(_db!);
            viewColumnNames = new string[probe.FieldCount];
            for (int i = 0; i < viewColumnNames.Length; i++)
                viewColumnNames[i] = probe.GetColumnName(i);
        }
        else if (columns is { Length: > 0 })
        {
            // Table-backed with projection: view columns match the projection
            viewColumnNames = columns;
        }
        else
        {
            // Table-backed, all columns: view columns match table columns
            viewColumnNames = new string[Table!.Columns.Count];
            for (int i = 0; i < viewColumnNames.Length; i++)
                viewColumnNames[i] = Table.Columns[i].Name;
        }

        // Build filter predicate from accumulated filters
        Func<IRowAccessor, bool>? filter = null;
        if (_filters is { Count: > 0 })
        {
            IFilterStar expression = _filters.Count == 1
                ? _filters[0]
                : FilterStar.And(_filters.ToArray());
            filter = ViewFilterBridge.Convert(expression, viewColumnNames);
        }

        var builder = _sourceLayer is SharcView sv
            ? ViewBuilder.From(sv)
            : ViewBuilder.From(Table!.Name);

        if (columns is { Length: > 0 })
            builder.Select(columns);

        if (filter != null)
            builder.Where(filter);

        builder.Named(name);

        return builder.Build();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Release cached cursor + reader resources
        if (_cachedTableReader != null)
        {
            _cachedTableReader.DisposeForReal();
            _cachedTableReader = null;
        }
        if (_cachedTableCursor != null)
        {
            _cachedTableCursor.Dispose();
            _cachedTableCursor = null;
        }

        _filters?.Clear();
        _filters = null;
        _transaction = null;
        _rootCache = null;
        _sourceLayer = null;
        _cachedFilterNode = null;
        _cachedViewFilter = null;
        _cachedViewColumnNames = null;
        _cachedProjection = null;
        _cachedProjectionColumns = null;
        _cachedReaderProjection = null;
        _db = null;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private void ThrowIfViewBacked()
    {
        if (_sourceLayer != null)
            throw new NotSupportedException(
                "JitQuery backed by a view does not support mutations. Use a table-backed JitQuery.");
    }

    private IFilterNode? CompileFilters()
    {
        if (_filters is null or { Count: 0 })
        {
            _cachedFilterNode = null;
            _compiledFilterVersion = _filterVersion;
            return null;
        }

        // Cache hit: filters unchanged since last compilation
        if (!IsFilterStale && _cachedFilterNode != null)
            return _cachedFilterNode;

        IFilterStar expression = _filters.Count == 1
            ? _filters[0]
            : FilterStar.And(_filters.ToArray());

        _cachedFilterNode = FilterTreeCompiler.CompileBaked(expression, Table!.Columns, RowidAliasOrdinal);
        _compiledFilterVersion = _filterVersion;
        return _cachedFilterNode;
    }

    private int[]? ResolveProjection(string[]? columns)
    {
        if (columns is not { Length: > 0 })
            return null;

        // Cache hit: same columns as last call
        if (_cachedProjection != null && ProjectionColumnsMatch(columns, _cachedProjectionColumns))
            return _cachedProjection;

        var projection = new int[columns.Length];
        for (int i = 0; i < columns.Length; i++)
            projection[i] = ResolveColumnOrdinal(columns[i]);

        _cachedProjection = projection;
        _cachedProjectionColumns = columns;
        return projection;
    }

    private static bool ProjectionColumnsMatch(string[]? a, string[]? b)
    {
        if (a is null || b is null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private int ResolveColumnOrdinal(string columnName)
    {
        for (int i = 0; i < Table!.Columns.Count; i++)
        {
            if (Table.Columns[i].Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return Table.Columns[i].Ordinal;
        }
        throw new ArgumentException($"Column '{columnName}' not found in table '{Table.Name}'.");
    }
}
