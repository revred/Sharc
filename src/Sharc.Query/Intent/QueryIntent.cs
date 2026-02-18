// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Fully resolved query intent — the bridge between parsed Sharq AST and FilterStar execution.
/// External consumers can construct this directly or obtain it via <c>IntentCompiler.Compile()</c>.
/// </summary>
public sealed class QueryIntent
{
    /// <summary>Target table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Optional record ID for single-row fetch (e.g., person:alice).</summary>
    public string? TableRecordId { get; init; }

    /// <summary>Alias of the primary table, if any.</summary>
    public string? TableAlias { get; init; }

    /// <summary>JOIN clauses, or null if none.</summary>
    public IReadOnlyList<JoinIntent>? Joins { get; init; }

    /// <summary>True if the query contains any JOIN operations.</summary>
    public bool HasJoins => Joins is { Count: > 0 };

    /// <summary>Projected column names, or null for all columns (SELECT *).</summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>WHERE filter tree, or null for no filter.</summary>
    public PredicateIntent? Filter { get; init; }

    /// <summary>ORDER BY directives, or null for unordered.</summary>
    public IReadOnlyList<OrderIntent>? OrderBy { get; init; }

    /// <summary>Maximum rows to return.</summary>
    public long? Limit { get; init; }

    /// <summary>Number of rows to skip.</summary>
    public long? Offset { get; init; }

    /// <summary>Whether to deduplicate result rows.</summary>
    public bool IsDistinct { get; init; }

    /// <summary>Aggregate functions in the SELECT list, or null if none.</summary>
    public IReadOnlyList<AggregateIntent>? Aggregates { get; init; }

    /// <summary>GROUP BY column names, or null if no grouping.</summary>
    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>HAVING filter on aggregated results, or null.</summary>
    public PredicateIntent? HavingFilter { get; init; }

    /// <summary>True if the query contains any aggregate functions.</summary>
    public bool HasAggregates => Aggregates is { Count: > 0 };

    // Cached array conversion of Columns — avoids repeated [.. Columns] spread allocations.
    // Safe to cache because QueryIntent is immutable and QueryPlan instances are reused.
    private string[]? _columnsArray;
    internal string[]? ColumnsArray => Columns is { Count: > 0 }
        ? (_columnsArray ??= Columns as string[] ?? [.. Columns])
        : null;
}
