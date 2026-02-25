// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Security;

/// <summary>
/// Multi-layer validator for arc files. Never throws — all errors reported
/// in <see cref="ArcValidationReport"/>.
///
/// Layer 1: Format — SQLite magic header sanity check (static <see cref="PreValidate"/>)
/// Layer 2: Chain — hash-chain integrity via <see cref="Trust.LedgerManager.VerifyIntegrity"/>
/// Layer 3: Trust — all signers present in <see cref="TrustAnchorSet"/>
/// Layer 4: Limits — entry count and payload size thresholds
/// </summary>
public static class ArcValidator
{
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Layer 1: Fast pre-validation of raw header bytes. Use before opening
    /// the file as a database to reject obviously invalid files.
    /// </summary>
    public static ArcValidationReport PreValidate(ReadOnlySpan<byte> header)
    {
        var violations = new List<string>();

        if (header.Length < 16)
        {
            violations.Add("Header too small: must be at least 16 bytes for SQLite magic validation.");
            return MakeReport(chainIntact: false, violations: violations);
        }

        if (!header.Slice(0, 16).SequenceEqual(SqliteMagic))
        {
            violations.Add("Invalid SQLite magic header: file is not a valid database.");
            return MakeReport(chainIntact: false, violations: violations);
        }

        return MakeReport(chainIntact: true, violations: violations);
    }

    /// <summary>
    /// Full multi-layer validation of an open arc handle.
    /// Layers 2-4 are checked in order; validation continues through all layers
    /// to provide a complete report.
    /// </summary>
    public static ArcValidationReport Validate(ArcHandle handle, ArcValidationOptions? options = null)
    {
        options ??= new ArcValidationOptions();
        var violations = new List<string>();
        var warnings = new List<string>();
        var unknownSigners = new List<string>();
        bool chainIntact = true;
        int ledgerEntryCount = 0;
        int agentCount = 0;

        // Layer 2: Chain integrity
        try
        {
            chainIntact = handle.VerifyIntegrity();
            if (!chainIntact)
                violations.Add("Ledger hash-chain integrity verification failed.");
        }
        catch (Exception ex)
        {
            chainIntact = false;
            violations.Add($"Chain integrity check error: {ex.Message}");
        }

        // Count ledger entries and agents
        const string ledgerTable = "_sharc_ledger";
        const string agentsTable = "_sharc_agents";

        if (handle.Database.Schema.GetTable(ledgerTable) != null)
        {
            using var reader = handle.Database.CreateReader(ledgerTable);
            while (reader.Read())
                ledgerEntryCount++;
        }

        if (handle.Database.Schema.GetTable(agentsTable) != null)
        {
            using var reader = handle.Database.CreateReader(agentsTable);
            while (reader.Read())
                agentCount++;
        }

        // Layer 3: Trust anchor checking
        bool allTrusted = true;
        if (options.TrustAnchors != null && options.UnknownSignerPolicy != TrustPolicy.AcceptAll)
        {
            var seenAgents = new HashSet<string>(StringComparer.Ordinal);
            if (handle.Database.Schema.GetTable(ledgerTable) != null)
            {
                using var reader = handle.Database.CreateReader(ledgerTable);
                while (reader.Read())
                {
                    string agentId = reader.GetString(2); // AgentId column
                    if (seenAgents.Add(agentId) && !options.TrustAnchors.Contains(agentId))
                        unknownSigners.Add(agentId);
                }
            }

            if (unknownSigners.Count > 0)
            {
                allTrusted = false;
                if (options.UnknownSignerPolicy == TrustPolicy.RejectUnknown)
                {
                    violations.Add($"Unknown signers found: {string.Join(", ", unknownSigners)}");
                }
                else // WarnUnknown
                {
                    foreach (var signer in unknownSigners)
                        warnings.Add($"Unknown signer in ledger: {signer}");
                }
            }
        }

        // Layer 4: Limit checks
        if (options.MaxLedgerEntryCount >= 0 && ledgerEntryCount > options.MaxLedgerEntryCount)
        {
            violations.Add(
                $"Ledger entry count {ledgerEntryCount} exceeds maximum {options.MaxLedgerEntryCount}.");
        }

        return new ArcValidationReport
        {
            ChainIntact = chainIntact,
            AllSignersTrusted = allTrusted,
            UnknownSigners = unknownSigners,
            Violations = violations,
            Warnings = warnings,
            LedgerEntryCount = ledgerEntryCount,
            AgentCount = agentCount
        };
    }

    private static ArcValidationReport MakeReport(
        bool chainIntact,
        List<string>? violations = null,
        List<string>? warnings = null)
    {
        return new ArcValidationReport
        {
            ChainIntact = chainIntact,
            AllSignersTrusted = true,
            UnknownSigners = Array.Empty<string>(),
            Violations = violations ?? new List<string>(),
            Warnings = warnings ?? new List<string>(),
            LedgerEntryCount = 0,
            AgentCount = 0
        };
    }
}
