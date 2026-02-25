// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Thread-safe cache for compiled query plans. Parse once, evaluate forever.
/// Eviction: when the cache exceeds <see cref="MaxCapacity"/>, it is cleared
/// entirely. Plans are cheap to recompile; this avoids unbounded memory growth
/// in long-running processes with dynamic query generation.
/// </summary>
internal sealed class QueryPlanCache
{
    /// <summary>
    /// Maximum number of cached plans before eviction.
    /// 1024 plans ≈ 200–400 KB (plan objects are small reference trees).
    /// </summary>
    internal const int MaxCapacity = 1024;

    private readonly ConcurrentDictionary<string, QueryPlan> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns a cached <see cref="QueryPlan"/> for the given query string,
    /// compiling it on the first call. Supports simple, compound, and Cote queries.
    /// </summary>
    internal QueryPlan GetOrCompilePlan(string query)
    {
        if (_cache.Count >= MaxCapacity)
            _cache.Clear();

        return _cache.GetOrAdd(query, static q => IntentCompiler.CompilePlan(q));
    }

    /// <summary>
    /// Returns a cached <see cref="QueryIntent"/> for simple (non-compound) queries.
    /// </summary>
    internal QueryIntent GetOrCompile(string query)
    {
        var plan = GetOrCompilePlan(query);
        return plan.Simple ?? throw new NotSupportedException(
            "Use GetOrCompilePlan for compound or Cote queries.");
    }

    /// <summary>Number of cached plans.</summary>
    internal int Count => _cache.Count;

    /// <summary>Clears all cached plans.</summary>
    internal void Clear() => _cache.Clear();
}
