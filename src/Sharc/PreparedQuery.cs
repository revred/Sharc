// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Schema;
using Sharc.Query;
using Sharc.Query.Intent;
using Sharc.Query.Optimization;

namespace Sharc;

/// <summary>
/// A pre-compiled query handle that eliminates parse, plan cache, view resolution,
/// and filter compilation overhead on repeated execution. Created via
/// <see cref="SharcDatabase.Prepare(string)"/>.
/// </summary>
/// <remarks>
/// <para>Phase 1 supports simple single-table SELECT queries (with optional WHERE, ORDER BY,
/// LIMIT, GROUP BY). Compound queries, CTEs, and JOINs throw <see cref="NotSupportedException"/>
/// at prepare time.</para>
/// <para>Cursor and reader are cached after the first Execute() call. Subsequent calls
/// reset the cursor via <see cref="IBTreeCursor.Reset"/> and reuse the same ArrayPool
/// buffers, eliminating per-call allocation and setup overhead.</para>
/// <para>This type is <b>not thread-safe</b>. Each <see cref="PreparedQuery"/> instance
/// should be used from a single thread.</para>
/// </remarks>
public sealed class PreparedQuery : IDisposable
{
    private SharcDatabase? _db;

    // Pre-resolved at Prepare() time — never re-computed
    internal readonly TableInfo Table;
    internal readonly int[]? Projection;
    internal readonly int RowidAliasOrdinal;
    internal readonly IFilterNode? StaticFilter;
    internal readonly QueryIntent Intent;
    internal readonly bool NeedsPostProcessing;

    // Pre-resolved at Prepare() time — avoids TryCreateIndexSeekCursor on every Execute()
    private readonly bool _hasIndexSeek;

    // Cached cursor + reader: created on first Execute(), reused via Reset() on subsequent calls.
    // Eliminates cursor allocation, ArrayPool rent/return, INTEGER PK detection, and
    // merged column detection on every call after the first.
    private IBTreeCursor? _cachedCursor;
    private SharcDataReader? _cachedReader;

    // Compact param cache (Dictionary, not ConcurrentDictionary — PreparedQuery is not thread-safe)
    private Dictionary<long, IFilterNode>? _paramCache;

    internal PreparedQuery(
        SharcDatabase db,
        TableInfo table,
        int[]? projection,
        int rowidAliasOrdinal,
        IFilterNode? staticFilter,
        QueryIntent intent,
        bool needsPostProcessing)
    {
        _db = db;
        Table = table;
        Projection = projection;
        RowidAliasOrdinal = rowidAliasOrdinal;
        StaticFilter = staticFilter;
        Intent = intent;
        NeedsPostProcessing = needsPostProcessing;

        // Pre-resolve index seek at Prepare time — determines cursor type once
        var seekCursor = db.TryCreateIndexSeekCursorForPrepared(intent, table);
        if (seekCursor != null)
        {
            _hasIndexSeek = true;
            // We don't cache the seek cursor itself (it can't be reset the same way),
            // but we remember that index seek IS available so we skip the check on Execute.
            seekCursor.Dispose();
        }
    }

    /// <summary>
    /// Executes the prepared query with no parameters and returns a data reader.
    /// Skips SQL parsing, plan cache lookup, view resolution, and filter compilation.
    /// </summary>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="ObjectDisposedException">The prepared query has been disposed.</exception>
    public SharcDataReader Execute()
    {
        return Execute(null);
    }

    /// <summary>
    /// Executes the prepared query with the given parameters and returns a data reader.
    /// On the first call, creates cursor and reader. On subsequent calls, reuses cached
    /// cursor (via Reset) and reader (via ResetForReuse), eliminating all per-call
    /// allocation and setup overhead.
    /// </summary>
    /// <param name="parameters">Named parameters to bind, or null for no parameters.</param>
    /// <returns>A <see cref="SharcDataReader"/> positioned before the first matching row.</returns>
    /// <exception cref="ObjectDisposedException">The prepared query has been disposed.</exception>
    public SharcDataReader Execute(IReadOnlyDictionary<string, object>? parameters)
    {
        ObjectDisposedException.ThrowIf(_db is null, this);

        var db = _db;

        // Determine filter node to use
        IFilterNode? node = StaticFilter;

        if (node == null && Intent.Filter.HasValue)
        {
            // Parameterized filter — check param-level cache
            _paramCache ??= new Dictionary<long, IFilterNode>();
            long paramKey = ComputeParamCacheKey(parameters);
            if (!_paramCache.TryGetValue(paramKey, out node))
            {
                var filterStar = IntentToFilterBridge.Build(
                    Intent.Filter.Value, parameters, Intent.TableAlias);
                node = FilterTreeCompiler.CompileBaked(
                    filterStar, Table.Columns, RowidAliasOrdinal);
                _paramCache[paramKey] = node;
            }
        }

        // Fast path: reuse cached cursor + reader (Reset instead of re-create)
        if (_cachedReader != null)
        {
            _cachedReader.ResetForReuse(node);

            if (NeedsPostProcessing)
                return QueryPostProcessor.Apply(_cachedReader, Intent);

            return _cachedReader;
        }

        // First call: create cursor + reader, cache for reuse
        // For non-index-seek queries (common case), use table cursor with Reset() support.
        // Index seek cursors are re-created each time (their seek position varies).
        IBTreeCursor cursor;
        if (_hasIndexSeek)
        {
            cursor = db.TryCreateIndexSeekCursorForPrepared(Intent, Table)
                ?? db.CreateTableCursorForPrepared(Table);
        }
        else
        {
            cursor = db.CreateTableCursorForPrepared(Table);
        }

        var reader = new SharcDataReader(cursor, db.Decoder, new SharcDataReader.CursorReaderConfig
        {
            Columns = Table.Columns,
            Projection = Projection,
            BTreeReader = db.BTreeReaderInternal,
            TableIndexes = Table.Indexes,
            FilterNode = node
        });

        // Cache for reuse — mark reader as owned so Dispose() resets instead of destroying
        if (!_hasIndexSeek)
        {
            reader.MarkReusable();
            _cachedCursor = cursor;
            _cachedReader = reader;
        }

        if (NeedsPostProcessing)
            return QueryPostProcessor.Apply(reader, Intent);

        return reader;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Release cached cursor + reader resources
        if (_cachedReader != null)
        {
            _cachedReader.DisposeForReal();
            _cachedReader = null;
        }
        if (_cachedCursor != null)
        {
            _cachedCursor.Dispose();
            _cachedCursor = null;
        }

        _paramCache?.Clear();
        _paramCache = null;
        _db = null;
    }

    private static long ComputeParamCacheKey(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null or { Count: 0 })
            return 0;

        var hc = new HashCode();
        foreach (var kvp in parameters)
        {
            hc.Add(kvp.Key);
            hc.Add(kvp.Value);
        }
        return hc.ToHashCode();
    }
}
