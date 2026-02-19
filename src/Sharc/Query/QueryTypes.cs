// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

global using RowSet = System.Collections.Generic.List<Sharc.Query.QueryValue[]>;
global using CoteMap = System.Collections.Generic.Dictionary<string, Sharc.Query.MaterializedResultSet>;

namespace Sharc.Query;

/// <summary>
/// Identifies a table and the optional subset of columns referenced by a query.
/// Used by <see cref="TableReferenceCollector"/> and entitlement enforcement.
/// </summary>
internal readonly record struct TableReference(string Table, string[]? Columns);

/// <summary>
/// A materialized query result containing rows and their column names.
/// Replaces the <c>(List&lt;QueryValue[]&gt; rows, string[] columns)</c> tuple pattern
/// used throughout the compound query, CTE, and set-operation pipeline.
/// </summary>
internal readonly record struct MaterializedResultSet(RowSet Rows, string[] Columns);
