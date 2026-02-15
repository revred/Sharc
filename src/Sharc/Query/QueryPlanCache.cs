// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Sharc.Query.Intent;

namespace Sharc.Query;

/// <summary>
/// Thread-safe cache for compiled query plans. Parse once, evaluate forever.
/// </summary>
internal sealed class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, QueryPlan> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns a cached <see cref="QueryPlan"/> for the given query string,
    /// compiling it on the first call. Supports simple, compound, and CTE queries.
    /// </summary>
    internal QueryPlan GetOrCompilePlan(string query)
    {
        return _cache.GetOrAdd(query, static q => IntentCompiler.CompilePlan(q));
    }

    /// <summary>
    /// Returns a cached <see cref="QueryIntent"/> for simple (non-compound) queries.
    /// </summary>
    internal QueryIntent GetOrCompile(string query)
    {
        var plan = GetOrCompilePlan(query);
        return plan.Simple ?? throw new NotSupportedException(
            "Use GetOrCompilePlan for compound or CTE queries.");
    }

    /// <summary>Number of cached plans.</summary>
    internal int Count => _cache.Count;

    /// <summary>Clears all cached plans.</summary>
    internal void Clear() => _cache.Clear();
}
