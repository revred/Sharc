// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Routes queries based on <see cref="ExecutionHint"/> to the optimal execution tier.
/// Owns the cache lifecycle for auto-Prepared and auto-Jit query handles.
/// </summary>
/// <remarks>
/// <para>SRP: QueryCore delegates hint routing here; new hint types extend this class,
/// not <see cref="SharcDatabase"/>.</para>
/// <para>OCP: add a new <see cref="ExecutionHint"/> case without modifying QueryCore.</para>
/// <para>Caches are keyed by <see cref="QueryIntent"/> reference identity — since
/// <see cref="QueryPlanCache"/> returns the same object for the same SQL string,
/// Dictionary uses RuntimeHelpers.GetHashCode + ReferenceEquals for O(1) lookups,
/// matching DIRECT's _readerInfoCache performance.</para>
/// </remarks>
internal sealed class ExecutionRouter : IDisposable
{
    // CACHED hint: QueryIntent → PreparedQuery (reference-equality keyed, O(1) identity lookup)
    private Dictionary<QueryIntent, PreparedQuery>? _cachedQueries;

    // JIT hint: QueryIntent → JitEntry (per-intent, eliminates filter thrashing)
    private Dictionary<QueryIntent, JitEntry>? _jitEntries;

    // Direct SQL string → handle caches: bypass QueryCore, plan cache, TryRoute switch,
    // and intent-keyed Dictionary lookup. Single Dictionary lookup for repeat hinted queries.
    private Dictionary<string, PreparedQuery>? _directCachedCache;
    private Dictionary<string, JitEntry>? _directJitCache;

    /// <summary>
    /// Tracks a cached JitQuery with pre-computed flags for fast repeat execution.
    /// Keyed per-intent (not per-table) so different SQL queries on the same table
    /// each get their own JitQuery with stable filters — no ClearFilters thrashing.
    /// </summary>
    private sealed class JitEntry
    {
        public required JitQuery Jit;
        public long LastParamKey;
        public bool HasParameterizedFilter;
        public bool NeedsPostProcessing;
        // Stored for direct bypass path (avoids re-parsing to get columns/intent)
        public string[]? ColumnsArray;
        public QueryIntent? Intent;
    }

    /// <summary>
    /// Attempts to route a query based on its execution hint.
    /// Returns a reader for CACHED/JIT hints, or <c>null</c> for DIRECT (fallback to QueryCore pipeline).
    /// </summary>
    internal SharcDataReader? TryRoute(
        SharcDatabase db,
        QueryPlan plan,
        string rawSql,
        IReadOnlyDictionary<string, object>? parameters)
    {
        return plan.Hint switch
        {
            ExecutionHint.Cached when CanUseCached(plan)
                => ExecuteCached(db, plan.Simple!, rawSql, parameters),
            ExecutionHint.Jit when CanUseJit(plan)
                => ExecuteJit(db, plan.Simple!, parameters),
            _ => null // DIRECT or unsupported hint → fall through to normal pipeline
        };
    }

    /// <summary>CACHED requires a simple, non-compound, non-CTE query.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseCached(QueryPlan plan)
        => !plan.IsCompound && !plan.HasCotes;

    /// <summary>JIT requires a simple, non-compound, non-CTE, non-JOIN query.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseJit(QueryPlan plan)
        => !plan.IsCompound && !plan.HasCotes
           && plan.Simple is { HasJoins: false };

    /// <summary>
    /// CACHED path: auto-Prepare on first call, cache by intent identity, execute.
    /// </summary>
    private SharcDataReader ExecuteCached(
        SharcDatabase db,
        QueryIntent intent,
        string rawSql,
        IReadOnlyDictionary<string, object>? parameters)
    {
        _cachedQueries ??= new Dictionary<QueryIntent, PreparedQuery>();

        if (!_cachedQueries.TryGetValue(intent, out var prepared))
        {
            prepared = db.Prepare(rawSql);
            _cachedQueries[intent] = prepared;
        }

        return prepared.Execute(parameters);
    }

    /// <summary>
    /// JIT path: auto-Jit on first call per intent, apply filters once, execute.
    /// Per-intent keying eliminates filter thrashing when different SQL queries target
    /// the same table. Parameterized filters are re-applied only when parameters change.
    /// </summary>
    private SharcDataReader ExecuteJit(
        SharcDatabase db,
        QueryIntent intent,
        IReadOnlyDictionary<string, object>? parameters)
    {
        _jitEntries ??= new Dictionary<QueryIntent, JitEntry>();

        if (!_jitEntries.TryGetValue(intent, out var entry))
        {
            // First call for this exact SQL — create JitQuery, apply filters, cache everything
            var jit = db.Jit(intent.TableName);

            if (intent.Filter.HasValue)
            {
                var filterStar = IntentToFilterBridge.Build(
                    intent.Filter.Value, parameters, intent.TableAlias);
                jit.Where(filterStar);
            }

            bool hasParamFilter = intent.Filter.HasValue
                && SharcDatabase.HasParameterNodes(intent.Filter.Value);

            entry = new JitEntry
            {
                Jit = jit,
                LastParamKey = hasParamFilter ? ComputeParamKey(parameters) : 0,
                HasParameterizedFilter = hasParamFilter,
                NeedsPostProcessing = intent.HasAggregates || intent.IsDistinct
                    || intent.OrderBy is { Count: > 0 }
                    || intent.Limit.HasValue || intent.Offset.HasValue,
            };
            _jitEntries[intent] = entry;
        }
        else if (entry.HasParameterizedFilter)
        {
            // Same SQL but parameterized — only recompute if params changed
            long paramKey = ComputeParamKey(parameters);
            if (entry.LastParamKey != paramKey)
            {
                entry.Jit.ClearFilters();
                var filterStar = IntentToFilterBridge.Build(
                    intent.Filter!.Value, parameters, intent.TableAlias);
                entry.Jit.Where(filterStar);
                entry.LastParamKey = paramKey;
            }
        }
        // else: non-parameterized, same intent → filters already applied, nothing to do

        var reader = entry.Jit.Query(intent.ColumnsArray);
        return entry.NeedsPostProcessing
            ? QueryPostProcessor.Apply(reader, intent)
            : reader;
    }

    /// <summary>
    /// Direct CACHED bypass: SQL string → PreparedQuery in one Dictionary lookup.
    /// Eliminates QueryCore frame, GetOrCompilePlan (ConcurrentDictionary), TryRoute switch,
    /// and intent-keyed Dictionary overhead for repeat hinted queries.
    /// Returns <c>null</c> for compound/CTE queries that can't use CACHED.
    /// </summary>
    internal SharcDataReader? ExecuteCachedDirect(
        SharcDatabase db,
        string rawSql,
        IReadOnlyDictionary<string, object>? parameters)
    {
        _directCachedCache ??= new Dictionary<string, PreparedQuery>(StringComparer.Ordinal);

        if (!_directCachedCache.TryGetValue(rawSql, out var prepared))
        {
            // First call: validate the plan supports CACHED before caching
            var plan = IntentCompiler.CompilePlan(rawSql);
            if (!CanUseCached(plan))
                return null; // Fall back to QueryCore

            prepared = db.Prepare(rawSql);
            _directCachedCache[rawSql] = prepared;
        }

        return prepared.Execute(parameters);
    }

    /// <summary>
    /// Direct JIT bypass: SQL string → JitEntry in one Dictionary lookup.
    /// Same benefits as ExecuteCachedDirect but for JIT-hinted queries.
    /// Returns <c>null</c> for compound/CTE/JOIN queries that can't use JIT.
    /// </summary>
    internal SharcDataReader? ExecuteJitDirect(
        SharcDatabase db,
        string rawSql,
        IReadOnlyDictionary<string, object>? parameters)
    {
        _directJitCache ??= new Dictionary<string, JitEntry>(StringComparer.Ordinal);

        if (!_directJitCache.TryGetValue(rawSql, out var entry))
        {
            // First call: parse, validate, create JitQuery, apply filters, cache
            var plan = IntentCompiler.CompilePlan(rawSql);
            if (!CanUseJit(plan))
                return null; // Fall back to QueryCore

            var intent = plan.Simple!;
            var jit = db.Jit(intent.TableName);

            if (intent.Filter.HasValue)
            {
                var filterStar = IntentToFilterBridge.Build(
                    intent.Filter.Value, parameters, intent.TableAlias);
                jit.Where(filterStar);
            }

            bool hasParamFilter = intent.Filter.HasValue
                && SharcDatabase.HasParameterNodes(intent.Filter.Value);

            entry = new JitEntry
            {
                Jit = jit,
                LastParamKey = hasParamFilter ? ComputeParamKey(parameters) : 0,
                HasParameterizedFilter = hasParamFilter,
                NeedsPostProcessing = intent.HasAggregates || intent.IsDistinct
                    || intent.OrderBy is { Count: > 0 }
                    || intent.Limit.HasValue || intent.Offset.HasValue,
                ColumnsArray = intent.ColumnsArray,
                Intent = intent,
            };
            _directJitCache[rawSql] = entry;

            // First call: use Query(columns) to establish projection for future reuse
            var reader = jit.Query(intent.ColumnsArray);
            return entry.NeedsPostProcessing
                ? QueryPostProcessor.Apply(reader, intent)
                : reader;
        }

        if (entry.HasParameterizedFilter)
        {
            long paramKey = ComputeParamKey(parameters);
            if (entry.LastParamKey != paramKey)
            {
                entry.Jit.ClearFilters();
                var filterStar = IntentToFilterBridge.Build(
                    entry.Intent!.Filter!.Value, parameters, entry.Intent.TableAlias);
                entry.Jit.Where(filterStar);
                entry.LastParamKey = paramKey;
            }
        }

        // Repeat calls: use QuerySameProjection() to avoid params string[] allocation
        var rdr = entry.Jit.QuerySameProjection();
        return entry.NeedsPostProcessing
            ? QueryPostProcessor.Apply(rdr, entry.Intent!)
            : rdr;
    }

    private static long ComputeParamKey(IReadOnlyDictionary<string, object>? parameters)
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_cachedQueries != null)
        {
            foreach (var pq in _cachedQueries.Values)
                pq.Dispose();
            _cachedQueries.Clear();
            _cachedQueries = null;
        }

        if (_jitEntries != null)
        {
            foreach (var entry in _jitEntries.Values)
                entry.Jit.Dispose();
            _jitEntries.Clear();
            _jitEntries = null;
        }

        if (_directCachedCache != null)
        {
            foreach (var pq in _directCachedCache.Values)
                pq.Dispose();
            _directCachedCache.Clear();
            _directCachedCache = null;
        }

        if (_directJitCache != null)
        {
            foreach (var entry in _directJitCache.Values)
                entry.Jit.Dispose();
            _directJitCache.Clear();
            _directJitCache = null;
        }
    }
}
