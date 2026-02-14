// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

/// <summary>
/// Comparison operators for row filtering during table scans.
/// </summary>
public enum SharcOperator
{
    /// <summary>Column value equals the filter value.</summary>
    Equal,

    /// <summary>Column value does not equal the filter value.</summary>
    NotEqual,

    /// <summary>Column value is less than the filter value.</summary>
    LessThan,

    /// <summary>Column value is greater than the filter value.</summary>
    GreaterThan,

    /// <summary>Column value is less than or equal to the filter value.</summary>
    LessOrEqual,

    /// <summary>Column value is greater than or equal to the filter value.</summary>
    GreaterOrEqual
}

/// <summary>
/// Defines a single column filter condition for table scans.
/// Multiple filters are combined with AND semantics.
/// </summary>
/// <param name="ColumnName">The column to filter on (case-insensitive match).</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The value to compare against. Supported types: long, int, double, string, null.</param>
public sealed record SharcFilter(string ColumnName, SharcOperator Operator, object? Value);