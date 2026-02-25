// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Security;

/// <summary>
/// Multi-layer validation report for an arc file.
/// Contains chain integrity, trust anchor, and limit check results.
/// </summary>
public sealed class ArcValidationReport
{
    /// <summary>Overall validity: true if no violations exist.</summary>
    public bool IsValid => Violations.Count == 0;

    /// <summary>Whether the hash-chain is unbroken (Layer 2).</summary>
    public required bool ChainIntact { get; init; }

    /// <summary>Whether all signers are in the trust anchor set (Layer 3).</summary>
    public required bool AllSignersTrusted { get; init; }

    /// <summary>Agent IDs found in the ledger but not in the trust anchor set.</summary>
    public required IReadOnlyList<string> UnknownSigners { get; init; }

    /// <summary>Hard violations that make the arc invalid.</summary>
    public required IReadOnlyList<string> Violations { get; init; }

    /// <summary>Non-fatal warnings (e.g., unknown signers under WarnUnknown policy).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Number of ledger entries found in the arc.</summary>
    public required int LedgerEntryCount { get; init; }

    /// <summary>Number of agents registered in the arc.</summary>
    public required int AgentCount { get; init; }
}
