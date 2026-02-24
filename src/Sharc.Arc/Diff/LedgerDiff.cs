// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Diff;

/// <summary>
/// Ledger-level diff between two arcs: compares hash-chain entries sequentially.
/// </summary>
public sealed class LedgerDiff
{
    /// <summary>True if both arcs have identical ledger chains.</summary>
    public bool IsIdentical => LeftOnlyCount == 0
        && RightOnlyCount == 0
        && !DivergenceSequence.HasValue;

    /// <summary>Number of ledger entries that match in both arcs (common prefix).</summary>
    public required int CommonPrefixLength { get; init; }

    /// <summary>Sequence number where the chains diverge. Null if no divergence.</summary>
    public required long? DivergenceSequence { get; init; }

    /// <summary>Number of entries only in the left arc (after common prefix).</summary>
    public required int LeftOnlyCount { get; init; }

    /// <summary>Number of entries only in the right arc (after common prefix).</summary>
    public required int RightOnlyCount { get; init; }

    /// <summary>Total entry count in the left arc's ledger.</summary>
    public required int LeftTotalCount { get; init; }

    /// <summary>Total entry count in the right arc's ledger.</summary>
    public required int RightTotalCount { get; init; }
}
