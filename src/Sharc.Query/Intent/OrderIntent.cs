// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// A single ORDER BY directive.
/// </summary>
public readonly struct OrderIntent
{
    /// <summary>Column to sort by.</summary>
    public required string ColumnName { get; init; }

    /// <summary>True for DESC, false for ASC (default).</summary>
    public bool Descending { get; init; }

    /// <summary>True to sort NULLs before non-NULL values.</summary>
    public bool NullsFirst { get; init; }
}
