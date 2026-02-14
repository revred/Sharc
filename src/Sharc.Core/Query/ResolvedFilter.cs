// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Query;

/// <summary>
/// A filter with its column ordinal pre-resolved for efficient per-row evaluation.
/// </summary>
public readonly struct ResolvedFilter
{
    /// <summary>
    /// The zero-based ordinal of the column to filter on.
    /// </summary>
    public required int ColumnOrdinal { get; init; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public required SharcOperator Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public required object? Value { get; init; }
}
