// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query;

/// <summary>
/// Identifies a table and the optional subset of columns referenced by a query.
/// Used by <see cref="TableReferenceCollector"/> and entitlement enforcement.
/// </summary>
internal readonly record struct TableReference(string Table, string[]? Columns);
