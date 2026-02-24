// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Diff;

/// <summary>
/// Complete diff result between two arcs, covering schema, ledger, and data layers.
/// </summary>
public sealed class ArcDiffResult
{
    /// <summary>URI or name of the left arc.</summary>
    public required string Left { get; init; }

    /// <summary>URI or name of the right arc.</summary>
    public required string Right { get; init; }

    /// <summary>True if both arcs are identical across all compared layers.</summary>
    public bool AreIdentical =>
        (Schema?.IsIdentical ?? true)
        && (Ledger?.IsIdentical ?? true)
        && Tables.All(t => t.IsIdentical);

    /// <summary>Schema diff. Null if schema comparison was not requested.</summary>
    public SchemaDiff? Schema { get; init; }

    /// <summary>Ledger diff. Null if ledger comparison was not requested.</summary>
    public LedgerDiff? Ledger { get; init; }

    /// <summary>Per-table data diffs. Empty if data comparison was not requested.</summary>
    public required IReadOnlyList<TableDiff> Tables { get; init; }
}
