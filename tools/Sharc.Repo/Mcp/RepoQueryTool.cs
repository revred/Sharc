// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Repo.Data;

namespace Sharc.Repo.Mcp;

/// <summary>
/// MCP tools for generic queries and workspace status.
/// </summary>
[McpServerToolType]
public static class RepoQueryTool
{
    [McpServerTool, Description(
        "Query a workspace table with optional row limit. " +
        "Valid tables: commits, file_changes, notes, file_annotations, decisions, context, conversations.")]
    public static string QueryWorkspace(
        [Description("Table name to query")] string table,
        [Description("Maximum rows to return (default 50)")] int limit = 50)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });

            using var reader = db.CreateReader(table);
            int fieldCount = reader.FieldCount;
            int rowCount = 0;
            var sb = new StringBuilder();

            while (reader.Read() && rowCount < limit)
            {
                var parts = new string[fieldCount];
                for (int c = 0; c < fieldCount; c++)
                    parts[c] = reader.IsNull(c) ? "(null)" : reader.GetString(c);
                sb.AppendLine(string.Join(" | ", parts));
                rowCount++;
            }

            return rowCount > 0
                ? $"# {table} ({rowCount} rows)\n\n{sb}"
                : $"(no rows in {table})";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Get workspace status: table counts, last indexed commit, and config summary. " +
        "Returns a markdown status report.")]
    public static string GetStatus()
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            var sb = new StringBuilder();
            sb.AppendLine("# Workspace Status");
            sb.AppendLine();

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);

            sb.AppendLine("| Table | Rows |");
            sb.AppendLine("|-------|------|");
            var tables = new[] { "commits", "file_changes", "notes", "file_annotations",
                "decisions", "context", "conversations" };
            foreach (var t in tables)
            {
                try { sb.AppendLine($"| {t} | {reader.CountRows(t)} |"); }
                catch { sb.AppendLine($"| {t} | - |"); }
            }

            sb.AppendLine();
            var lastSha = reader.GetMeta("last_indexed_sha");
            sb.AppendLine(lastSha != null
                ? $"**Last indexed:** `{lastSha}`"
                : "**Git history not yet indexed.** Run `sharc update`.");

            // Config summary
            var configPath = Environment.GetEnvironmentVariable("SHARC_CONFIG");
            if (configPath != null && File.Exists(configPath))
            {
                using var configDb = SharcDatabase.Open(configPath, new SharcOpenOptions { Writable = false });
                using var cw = new ConfigWriter(configDb);
                var entries = cw.GetAll();
                sb.AppendLine();
                sb.AppendLine("## Config");
                sb.AppendLine();
                foreach (var (key, value) in entries)
                    sb.AppendLine($"- **{key}** = {value}");
            }

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
