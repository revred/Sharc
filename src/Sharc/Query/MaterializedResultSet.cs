// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query;

/// <summary>
/// A materialized query result containing rows and their column names.
/// Replaces the <c>(List&lt;QueryValue[]&gt; rows, string[] columns)</c> tuple pattern
/// used throughout the compound query, CTE, and set-operation pipeline.
/// </summary>
internal readonly record struct MaterializedResultSet(List<QueryValue[]> Rows, string[] Columns);
