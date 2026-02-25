// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Diff;

/// <summary>
/// Controls the scope and limits of an arc diff operation.
/// </summary>
public sealed class ArcDiffOptions
{
    /// <summary>Which layers to compare. Default: all.</summary>
    public DiffScope Scope { get; init; } = DiffScope.Schema | DiffScope.Ledger | DiffScope.Data;

    /// <summary>
    /// Optional table name filter. Only tables matching these names will be diffed.
    /// Null means diff all tables.
    /// </summary>
    public IReadOnlyList<string>? TableFilter { get; init; }

    /// <summary>
    /// Maximum number of row-level differences to report per table.
    /// Default: 10,000. Set to -1 for unlimited.
    /// </summary>
    public int MaxRowDiffsPerTable { get; init; } = 10_000;
}

/// <summary>
/// Flags controlling which diff layers to include.
/// </summary>
[Flags]
public enum DiffScope
{
    /// <summary>Compare table/column structure.</summary>
    Schema = 1,

    /// <summary>Compare ledger entries.</summary>
    Ledger = 2,

    /// <summary>Compare row-level data.</summary>
    Data = 4
}
