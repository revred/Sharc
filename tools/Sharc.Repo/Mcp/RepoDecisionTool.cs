// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Repo.Data;

namespace Sharc.Repo.Mcp;

/// <summary>
/// MCP tools for recording and listing architectural decisions.
/// </summary>
[McpServerToolType]
public static class RepoDecisionTool
{
    [McpServerTool, Description(
        "Record an architectural decision in the repo workspace. " +
        "Returns confirmation or error.")]
    public static string RecordDecision(
        [Description("Decision identifier (e.g. 'ADR-001')")] string decisionId,
        [Description("Decision title")] string title,
        [Description("Rationale for the decision")] string? rationale = null,
        [Description("Status: 'accepted', 'proposed', 'deprecated', 'superseded'")] string status = "accepted",
        [Description("Optional author name")] string? author = null)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
            using var writer = new WorkspaceWriter(db);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteDecision(new DecisionRecord(
                decisionId, title, rationale, status, author, now, null));

            return $"Decision {decisionId} recorded.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "List architectural decisions from the repo workspace. " +
        "Returns markdown-formatted decisions list.")]
    public static string ListDecisions(
        [Description("Optional status filter (e.g. 'accepted', 'proposed')")] string? status = null)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);
            var decisions = reader.ReadDecisions(status: status);

            if (decisions.Count == 0)
                return status != null ? $"No decisions with status '{status}'." : "No decisions.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Decisions ({decisions.Count})");
            sb.AppendLine();
            foreach (var d in decisions)
            {
                sb.AppendLine($"## {d.DecisionId}: {d.Title}");
                sb.AppendLine($"**Status:** {d.Status}");
                if (d.Rationale != null) sb.AppendLine($"**Rationale:** {d.Rationale}");
                if (d.Author != null) sb.AppendLine($"**Author:** {d.Author}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
