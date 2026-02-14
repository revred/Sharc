// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc;

// SharcOperator moved to Sharc.Core.Query
using global::Sharc.Core.Query;

/// <summary>
/// Defines a single column filter condition for table scans.
/// Multiple filters are combined with AND semantics.
/// </summary>
/// <param name="ColumnName">The column to filter on (case-insensitive match).</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The value to compare against. Supported types: long, int, double, string, null.</param>
public sealed record SharcFilter(string ColumnName, SharcOperator Operator, object? Value);