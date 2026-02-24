// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Arc;
using Sharc.Arc.Diff;
using Sharc.Arc.Security;

namespace Sharc.Context.Tools;

/// <summary>
/// MCP tools for validating, inspecting, and diffing arc files.
/// All tools catch exceptions internally — never crash the MCP server.
/// </summary>
[McpServerToolType]
public static class ArcTool
{
    /// <summary>
    /// Validates an arc file's format, ledger integrity, and trust anchors.
    /// </summary>
    [McpServerTool, Description(
        "Validate an arc file: checks SQLite format, ledger hash-chain integrity, " +
        "and trust anchor status. Returns a markdown validation report.")]
    public static string ValidateArc(
        [Description("Absolute path to the .arc file to validate")]
        string path)
    {
        try
        {
            if (!File.Exists(path))
                return $"Error: File not found: {path}";

            // Pre-validate header
            byte[] header = new byte[16];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read = fs.Read(header);
                if (read < 16)
                    return "Error: File too small to be a valid arc (< 16 bytes).";
            }

            var preResult = ArcValidator.PreValidate(header);
            if (!preResult.IsValid)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Arc Validation: FAILED (Pre-validation)");
                sb.AppendLine();
                foreach (var v in preResult.Violations)
                    sb.AppendLine($"- {v}");
                return sb.ToString();
            }

            // Full validation
            using var handle = ArcHandle.OpenLocal(path);
            var report = ArcValidator.Validate(handle);

            return FormatReport(path, report);
        }
        catch (Exception ex)
        {
            return $"Error validating arc: {ex.Message}";
        }
    }

    /// <summary>
    /// Diffs two arc files across schema, ledger, and data layers.
    /// </summary>
    [McpServerTool, Description(
        "Compare two arc files. Returns a markdown diff report covering " +
        "schema differences, ledger divergence, and row-level data changes.")]
    public static string DiffArcs(
        [Description("Absolute path to the left/source .arc file")]
        string pathA,
        [Description("Absolute path to the right/target .arc file")]
        string pathB,
        [Description("Diff scope: 'schema', 'ledger', 'data', or 'all' (default: 'all')")]
        string scope = "all")
    {
        try
        {
            if (!File.Exists(pathA))
                return $"Error: File not found: {pathA}";
            if (!File.Exists(pathB))
                return $"Error: File not found: {pathB}";

            using var handleA = ArcHandle.OpenLocal(pathA);
            using var handleB = ArcHandle.OpenLocal(pathB);

            var diffScope = ParseScope(scope);
            var result = ArcDiffer.Diff(handleA, handleB, new ArcDiffOptions { Scope = diffScope });

            return FormatDiff(result);
        }
        catch (Exception ex)
        {
            return $"Error diffing arcs: {ex.Message}";
        }
    }

    /// <summary>
    /// Inspects an arc file's contents: tables, rows, ledger, and agents.
    /// </summary>
    [McpServerTool, Description(
        "Inspect an arc file: lists tables, row counts, ledger entries, " +
        "and registered agents. Returns a markdown summary.")]
    public static string InspectArc(
        [Description("Absolute path to the .arc file to inspect")]
        string path)
    {
        try
        {
            if (!File.Exists(path))
                return $"Error: File not found: {path}";

            using var handle = ArcHandle.OpenLocal(path);

            var sb = new StringBuilder();
            sb.AppendLine($"# Arc: {Path.GetFileName(path)}");
            sb.AppendLine();
            sb.AppendLine($"**Path:** `{path}`");
            sb.AppendLine($"**Size:** {new FileInfo(path).Length:N0} bytes");
            sb.AppendLine();

            // Tables
            var tables = handle.Database.Schema.Tables;
            sb.AppendLine("## Tables");
            sb.AppendLine();
            sb.AppendLine("| Table | Columns | Rows |");
            sb.AppendLine("|-------|---------|------|");

            foreach (var table in tables)
            {
                long rowCount = 0;
                try
                {
                    using var reader = handle.Database.CreateReader(table.Name);
                    while (reader.Read()) rowCount++;
                }
                catch { /* skip unreadable tables */ }

                sb.AppendLine($"| {table.Name} | {table.Columns.Count} | {rowCount} |");
            }

            sb.AppendLine();

            // Agents
            const string agentsTable = "_sharc_agents";
            if (handle.Database.Schema.GetTable(agentsTable) != null)
            {
                sb.AppendLine("## Agents");
                sb.AppendLine();
                sb.AppendLine("| AgentId | Class |");
                sb.AppendLine("|---------|-------|");

                using var reader = handle.Database.CreateReader(agentsTable);
                while (reader.Read())
                {
                    string agentId = reader.GetString(0);
                    long agentClass = reader.GetInt64(1);
                    sb.AppendLine($"| {agentId} | {agentClass} |");
                }
                sb.AppendLine();
            }

            // Ledger summary
            const string ledgerTable = "_sharc_ledger";
            if (handle.Database.Schema.GetTable(ledgerTable) != null)
            {
                int entryCount = 0;
                using var reader = handle.Database.CreateReader(ledgerTable);
                while (reader.Read()) entryCount++;

                sb.AppendLine("## Ledger");
                sb.AppendLine();
                sb.AppendLine($"**Entries:** {entryCount}");
                sb.AppendLine($"**Integrity:** {(handle.VerifyIntegrity() ? "VALID" : "BROKEN")}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error inspecting arc: {ex.Message}";
        }
    }

    // ── Formatting helpers ────────────────────────────────────────────

    private static string FormatReport(string path, ArcValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Arc Validation: {(report.IsValid ? "PASSED" : "FAILED")}");
        sb.AppendLine();
        sb.AppendLine($"**File:** `{path}`");
        sb.AppendLine($"**Chain Intact:** {report.ChainIntact}");
        sb.AppendLine($"**All Signers Trusted:** {report.AllSignersTrusted}");
        sb.AppendLine($"**Ledger Entries:** {report.LedgerEntryCount}");
        sb.AppendLine($"**Agents:** {report.AgentCount}");

        if (report.Violations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Violations");
            foreach (var v in report.Violations)
                sb.AppendLine($"- {v}");
        }

        if (report.UnknownSigners.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Unknown Signers");
            foreach (var s in report.UnknownSigners)
                sb.AppendLine($"- `{s}`");
        }

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (var w in report.Warnings)
                sb.AppendLine($"- {w}");
        }

        return sb.ToString();
    }

    private static string FormatDiff(ArcDiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Arc Diff: {(result.AreIdentical ? "IDENTICAL" : "DIFFERENT")}");
        sb.AppendLine();
        sb.AppendLine($"**Left:** `{result.Left}`");
        sb.AppendLine($"**Right:** `{result.Right}`");

        if (result.Schema != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Schema");
            if (result.Schema.IsIdentical)
            {
                sb.AppendLine("Schemas are identical.");
            }
            else
            {
                if (result.Schema.TablesOnlyInLeft.Count > 0)
                    sb.AppendLine($"- Tables only in left: {string.Join(", ", result.Schema.TablesOnlyInLeft)}");
                if (result.Schema.TablesOnlyInRight.Count > 0)
                    sb.AppendLine($"- Tables only in right: {string.Join(", ", result.Schema.TablesOnlyInRight)}");
                foreach (var mod in result.Schema.ModifiedTables)
                {
                    sb.AppendLine($"- **{mod.TableName}**: ");
                    if (mod.ColumnsOnlyInLeft.Count > 0)
                        sb.AppendLine($"  - Columns only in left: {string.Join(", ", mod.ColumnsOnlyInLeft)}");
                    if (mod.ColumnsOnlyInRight.Count > 0)
                        sb.AppendLine($"  - Columns only in right: {string.Join(", ", mod.ColumnsOnlyInRight)}");
                    foreach (var tc in mod.TypeChanges)
                        sb.AppendLine($"  - `{tc.ColumnName}`: {tc.LeftType} → {tc.RightType}");
                }
            }
        }

        if (result.Ledger != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Ledger");
            if (result.Ledger.IsIdentical)
            {
                sb.AppendLine("Ledgers are identical.");
            }
            else
            {
                sb.AppendLine($"- Common prefix: {result.Ledger.CommonPrefixLength} entries");
                if (result.Ledger.DivergenceSequence.HasValue)
                    sb.AppendLine($"- Divergence at sequence: {result.Ledger.DivergenceSequence}");
                sb.AppendLine($"- Left total: {result.Ledger.LeftTotalCount}, Right total: {result.Ledger.RightTotalCount}");
                sb.AppendLine($"- Left only: {result.Ledger.LeftOnlyCount}, Right only: {result.Ledger.RightOnlyCount}");
            }
        }

        if (result.Tables.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Data");
            sb.AppendLine();
            sb.AppendLine("| Table | Left Rows | Right Rows | Matching | Modified | Left Only | Right Only |");
            sb.AppendLine("|-------|-----------|------------|----------|----------|-----------|------------|");

            foreach (var t in result.Tables)
            {
                sb.AppendLine($"| {t.TableName} | {t.LeftRowCount} | {t.RightRowCount} | " +
                    $"{t.MatchingRowCount} | {t.ModifiedRowCount} | {t.LeftOnlyRowCount} | {t.RightOnlyRowCount} |");
            }
        }

        return sb.ToString();
    }

    private static DiffScope ParseScope(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "schema" => DiffScope.Schema,
            "ledger" => DiffScope.Ledger,
            "data" => DiffScope.Data,
            _ => DiffScope.Schema | DiffScope.Ledger | DiffScope.Data
        };
    }
}
