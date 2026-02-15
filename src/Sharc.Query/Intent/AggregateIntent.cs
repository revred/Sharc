// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Aggregate function type.
/// </summary>
public enum AggregateFunction : byte
{
    /// <summary>COUNT(*) — counts all rows.</summary>
    CountStar,

    /// <summary>COUNT(column) — counts non-null values.</summary>
    Count,

    /// <summary>SUM(column) — numeric sum.</summary>
    Sum,

    /// <summary>AVG(column) — numeric average.</summary>
    Avg,

    /// <summary>MIN(column) — minimum value.</summary>
    Min,

    /// <summary>MAX(column) — maximum value.</summary>
    Max,
}

/// <summary>
/// Describes a single aggregate operation in a SELECT list.
/// </summary>
public readonly struct AggregateIntent
{
    /// <summary>The aggregate function to apply.</summary>
    public AggregateFunction Function { get; init; }

    /// <summary>The column to aggregate, or null for COUNT(*).</summary>
    public string? ColumnName { get; init; }

    /// <summary>Output column name (e.g., "COUNT(*)" or user alias).</summary>
    public string Alias { get; init; }

    /// <summary>Index in the output column list.</summary>
    public int OutputOrdinal { get; init; }
}
