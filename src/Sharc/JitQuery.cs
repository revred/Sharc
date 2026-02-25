// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

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
public sealed partial class JitQuery : IPreparedReader, IPreparedWriter
{
    // ── Sentinel constants for limit/offset (avoids 16-byte Nullable<long>) ──
    private const long NoLimit = -1;
    private const long NoOffset = -1;

    // ── Shared state (both table-backed and view-backed) ──
    private SharcDatabase? _db;
    private List<IFilterStar>? _filters;
    private long _limit = NoLimit;
    private long _offset = NoOffset;
    private int _filterVersion;
    private int _compiledFilterVersion = -1;
    private int[]? _cachedProjection;
    private string[]? _cachedProjectionColumns;

    // ── Mode-specific state (exactly one is non-null) ──
    internal readonly TableState? _table;
    private ViewState? _view;

    /// <summary>Table-backed state — holds cursor/reader caches, filter nodes, mutation state.</summary>
    internal sealed class TableState
    {
        internal readonly TableInfo Info;
        internal readonly int RowidAliasOrdinal;
        internal IFilterNode? CachedFilterNode;
        internal IBTreeCursor? CachedCursor;
        internal SharcDataReader? CachedReader;
        internal int[]? ReaderProjection; // projection at time of reader creation (reference equality)
        internal Trust.IRowAccessEvaluator? RowAccessEvaluator;
        internal SharcWriteTransaction? Transaction;
        internal Dictionary<string, uint>? RootCache;

        internal TableState(TableInfo table, int rowidAliasOrdinal)
        {
            Info = table;
            RowidAliasOrdinal = rowidAliasOrdinal;
        }
    }

    /// <summary>View-backed state — holds layer source and cached view filters.</summary>
    private sealed class ViewState
    {
        internal readonly ILayer Source;
        internal Func<IRowAccessor, bool>? CachedFilter;
        internal string[]? CachedColumnNames;

        internal ViewState(ILayer source) => Source = source;
    }

    /// <summary>
    /// True when the compiled filter cache is out of date with accumulated filters.
    /// Mirrors the <c>BTreeCursor.IsStale</c> / <c>DataVersion</c> pattern used throughout Sharc.
    /// </summary>
    private bool IsFilterStale => _compiledFilterVersion != _filterVersion;

    /// <summary>The pre-resolved table info, or null when view-backed.</summary>
    internal TableInfo? Table => _table?.Info;

    /// <summary>The rowid alias ordinal for the table, or -1 when view-backed.</summary>
    internal int RowidAliasOrdinal => _table?.RowidAliasOrdinal ?? -1;

    /// <summary>True when accumulated filters, limit, or offset are active.</summary>
    internal bool HasActiveFilters => _filters is { Count: > 0 } || _limit >= 0 || _offset >= 0;

    /// <summary>True when a row-level access evaluator is set.</summary>
    internal bool HasRowAccessEvaluator => _table is { RowAccessEvaluator: not null };

    /// <summary>Creates a table-backed JitQuery.</summary>
    internal JitQuery(SharcDatabase db, TableInfo table, int rowidAliasOrdinal)
    {
        _db = db;
        _table = new TableState(table, rowidAliasOrdinal);
    }

    /// <summary>Creates a layer-backed (view-backed) JitQuery.</summary>
    internal JitQuery(SharcDatabase db, ILayer source)
    {
        _db = db;
        _view = new ViewState(source);
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
        _limit = NoLimit;
        _offset = NoOffset;
        _filterVersion++;
        if (_table != null)
            _table.CachedFilterNode = null;
        if (_view != null)
            _view.CachedFilter = null;
        _cachedProjection = null;
        _cachedProjectionColumns = null;
        return this;
    }

    // ── Row-Level Access ────────────────────────────────────────────

    /// <summary>
    /// Sets the row-level access evaluator for agent entitlement filtering.
    /// When set, rows that fail the evaluator are silently skipped during scans.
    /// </summary>
    internal JitQuery WithRowAccess(Trust.IRowAccessEvaluator? evaluator)
    {
        if (_table == null) return this;
        if (!ReferenceEquals(_table.RowAccessEvaluator, evaluator))
        {
            _table.RowAccessEvaluator = evaluator;
            // Invalidate cached reader — evaluator changed
            if (_table.CachedReader != null)
            {
                _table.CachedReader.DisposeForReal();
                _table.CachedCursor?.Dispose();
                _table.CachedReader = null;
                _table.CachedCursor = null;
            }
        }
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

        if (_view != null)
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

        var ts = _table;
        if (ts == null || (ts.CachedReader == null && _cachedProjection == null))
            throw new InvalidOperationException(
                "QuerySameProjection() requires a prior Query() call to establish the projection.");

        // Reuse the cached projection directly — no params array, no ResolveProjection
        return QueryFromTableDirect();
    }

    /// <summary>
    /// Fast path for QuerySameProjection — skips projection resolution entirely.
    /// </summary>
    private SharcDataReader QueryFromTableDirect()
    {
        var db = _db!;
        var ts = _table!;
        IFilterNode? filterNode = CompileFilters();

        // Fastest path: reuse cached cursor + reader
        if (ts.CachedReader != null && _limit < 0 && _offset < 0)
        {
            ts.CachedReader.ResetForReuse(filterNode);
            return ts.CachedReader;
        }

        // Fall through to full creation with cached projection
        return QueryFromTableCreate(db, filterNode, _cachedProjection);
    }

    private SharcDataReader QueryFromTable(string[]? columns)
    {
        var db = _db!;
        var ts = _table!;

        // Compile accumulated filters
        IFilterNode? filterNode = CompileFilters();

        // Resolve projection
        int[]? projection = ResolveProjection(columns);

        // Fast path: reuse cached cursor + reader if projection is unchanged
        // and no LIMIT/OFFSET (which wraps the reader in post-processing).
        // Reference equality on projection works because ResolveProjection caches its result.
        if (ts.CachedReader != null
            && _limit < 0 && _offset < 0
            && ReferenceEquals(ts.ReaderProjection, projection))
        {
            ts.CachedReader.ResetForReuse(filterNode);
            return ts.CachedReader;
        }

        return QueryFromTableCreate(db, filterNode, projection);
    }

    /// <summary>Creates cursor + reader, caches for reuse. Shared by QueryFromTable and QueryFromTableDirect.</summary>
    private SharcDataReader QueryFromTableCreate(SharcDatabase db, IFilterNode? filterNode, int[]? projection)
    {
        var ts = _table!;

        // Dispose previous cached reader if projection changed
        if (ts.CachedReader != null)
        {
            ts.CachedReader.DisposeForReal();
            ts.CachedCursor?.Dispose();
            ts.CachedReader = null;
            ts.CachedCursor = null;
        }

        var cursor = db.CreateTableCursorForPrepared(ts.Info);

        var reader = new SharcDataReader(cursor, db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = ts.Info.Columns,
            Projection = projection,
            BTreeReader = db.BTreeReaderInternal,
            TableIndexes = ts.Info.Indexes,
            FilterNode = filterNode,
            RowAccessEvaluator = ts.RowAccessEvaluator
        });

        // Apply LIMIT/OFFSET if set — can't cache (post-processor wraps the reader)
        if (_limit >= 0 || _offset >= 0)
        {
            var intent = new QueryIntent
            {
                TableName = ts.Info.Name,
                Limit = _limit >= 0 ? _limit : null,
                Offset = _offset >= 0 ? _offset : null
            };
            return QueryPost.Apply(reader, intent);
        }

        // Cache for reuse on subsequent calls
        reader.MarkReusable();
        ts.CachedCursor = cursor;
        ts.CachedReader = reader;
        ts.ReaderProjection = projection;
        _cachedProjection = projection;

        return reader;
    }

    private SharcDataReader QueryFromView(string[]? columns)
    {
        var db = _db!;
        var vs = _view!;
        var cursor = vs.Source.Open(db);

        // Get column metadata — cache on first call (view columns are stable)
        int fieldCount = cursor.FieldCount;
        string[] allColumnNames;
        if (vs.CachedColumnNames != null)
        {
            allColumnNames = vs.CachedColumnNames;
        }
        else
        {
            allColumnNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                allColumnNames[i] = cursor.GetColumnName(i);
            vs.CachedColumnNames = allColumnNames;
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
                        : throw new ArgumentException($"Column '{columns[i]}' not found in view '{vs.Source.Name}'.");
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
            if (!IsFilterStale && vs.CachedFilter != null)
            {
                jitFilter = vs.CachedFilter;
            }
            else
            {
                IFilterStar expression = _filters.Count == 1
                    ? _filters[0]
                    : FilterStar.And(_filters.ToArray());
                jitFilter = ViewFilterBridge.Convert(expression, allColumnNames);
                vs.CachedFilter = jitFilter;
                _compiledFilterVersion = _filterVersion;
            }
        }
        else if (IsFilterStale)
        {
            vs.CachedFilter = null;
            _compiledFilterVersion = _filterVersion;
        }

        // Stream rows from cursor → QueryValue[] → SharcDataReader
        var rows = StreamViewRows(cursor, fieldCount, projection, jitFilter);
        var reader = new SharcDataReader(rows, outputColumnNames);

        // Apply LIMIT/OFFSET if set
        if (_limit >= 0 || _offset >= 0)
        {
            var intent = new QueryIntent
            {
                TableName = vs.Source.Name,
                Limit = _limit >= 0 ? _limit : null,
                Offset = _offset >= 0 ? _offset : null
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
        var ts = _table!;

        if (ts.Transaction != null)
            return ts.Transaction.Insert(ts.Info.Name, values);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        ts.RootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        long rowId = SharcWriter.InsertCore(tx, ts.Info.Name, values, ts.Info, ts.RootCache);
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
        var ts = _table!;

        if (ts.Transaction != null)
            return ts.Transaction.Delete(ts.Info.Name, rowId);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        ts.RootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        bool found = SharcWriter.DeleteCore(tx, ts.Info.Name, rowId, ts.RootCache);
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
        var ts = _table!;

        if (ts.Transaction != null)
            return ts.Transaction.Update(ts.Info.Name, rowId, values);

        // Auto-commit path
        var db = _db;
        using var tx = db.BeginTransaction();
        ts.RootCache ??= new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        bool found = SharcWriter.UpdateCore(tx, ts.Info.Name, rowId, values, ts.Info, ts.RootCache);
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
        ThrowIfViewBacked();
        _table!.Transaction = tx;
        return this;
    }

    /// <summary>
    /// Detaches the bound transaction, reverting to auto-commit mode for mutations.
    /// </summary>
    public JitQuery DetachTransaction()
    {
        if (_table != null)
            _table.Transaction = null;
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
        var ts = _table!;

        // Compile current filters into a static filter node
        IFilterNode? staticFilter = CompileFilters();

        // Resolve projection
        int[]? projection = ResolveProjection(columns);

        bool needsPostProcessing = _limit >= 0 || _offset >= 0;

        // Build a synthetic QueryIntent from accumulated state
        var intent = new QueryIntent
        {
            TableName = ts.Info.Name,
            Columns = columns is { Length: > 0 } ? columns : null,
            Limit = _limit >= 0 ? _limit : null,
            Offset = _offset >= 0 ? _offset : null
        };

        return new PreparedQuery(db, ts.Info, projection, ts.RowidAliasOrdinal,
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
        if (_view != null)
        {
            // View-backed: probe cursor for column names
            using var probe = _view.Source.Open(_db!);
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
            var tableInfo = _table!.Info;
            viewColumnNames = new string[tableInfo.Columns.Count];
            for (int i = 0; i < viewColumnNames.Length; i++)
                viewColumnNames[i] = tableInfo.Columns[i].Name;
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

        var builder = _view?.Source is SharcView sv
            ? ViewBuilder.From(sv)
            : ViewBuilder.From(_table!.Info.Name);

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
        if (_table != null)
        {
            if (_table.CachedReader != null)
            {
                _table.CachedReader.DisposeForReal();
                _table.CachedReader = null;
            }
            if (_table.CachedCursor != null)
            {
                _table.CachedCursor.Dispose();
                _table.CachedCursor = null;
            }
            _table.ReaderProjection = null;
            _table.RowAccessEvaluator = null;
            _table.Transaction = null;
            _table.RootCache = null;
            _table.CachedFilterNode = null;
        }

        if (_view != null)
        {
            _view.CachedFilter = null;
            _view.CachedColumnNames = null;
            _view = null;
        }

        _filters?.Clear();
        _filters = null;
        _cachedProjection = null;
        _cachedProjectionColumns = null;
        _db = null;
    }

}

