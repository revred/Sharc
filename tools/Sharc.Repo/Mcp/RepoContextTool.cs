// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Core;
using Sharc.Repo.Data;

namespace Sharc.Repo.Mcp;

/// <summary>
/// MCP tools for notes and key-value context in workspace.arc.
/// </summary>
[McpServerToolType]
public static class RepoContextTool
{
    [McpServerTool, Description(
        "Add a free-form note to the repo workspace. " +
        "Returns confirmation or error message.")]
    public static string AddNote(
        [Description("The note content")] string content,
        [Description("Optional tag (e.g. 'bug', 'todo', 'idea')")] string? tag = null,
        [Description("Optional author name")] string? author = null)
    {
        try
        {
            var wsPath = GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
            using var writer = new WorkspaceWriter(db);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteNote(new NoteRecord(content, tag, author, now, null));

            return "Note added.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "List notes from the repo workspace. " +
        "Returns markdown-formatted notes list.")]
    public static string ListNotes(
        [Description("Optional tag filter")] string? tag = null)
    {
        try
        {
            var wsPath = GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);
            var notes = reader.ReadNotes(tag: tag);

            if (notes.Count == 0)
                return tag != null ? $"No notes with tag '{tag}'." : "No notes.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Notes ({notes.Count})");
            sb.AppendLine();
            foreach (var n in notes)
            {
                sb.Append($"- {n.Content}");
                if (n.Tag != null) sb.Append($" [**{n.Tag}**]");
                if (n.Author != null) sb.Append($" — {n.Author}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Set a key-value context entry in the repo workspace. " +
        "Upserts: updates if key exists, inserts otherwise.")]
    public static string SetContext(
        [Description("Context key")] string key,
        [Description("Context value")] string value,
        [Description("Optional author name")] string? author = null)
    {
        try
        {
            var wsPath = GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Scan-then-update
            long? existingRowId = null;
            using (var r = db.CreateReader("context"))
            {
                while (r.Read())
                {
                    if (string.Equals(r.GetString(0), key, StringComparison.Ordinal))
                    {
                        existingRowId = r.RowId;
                        break;
                    }
                }
            }

            if (existingRowId.HasValue)
            {
                var kBytes = Encoding.UTF8.GetBytes(key);
                var vBytes = Encoding.UTF8.GetBytes(value);
                using var sw = SharcWriter.From(db);
                sw.Update("context", existingRowId.Value,
                    ColumnValue.Text(2 * kBytes.Length + 13, kBytes),
                    ColumnValue.Text(2 * vBytes.Length + 13, vBytes),
                    author != null ? WorkspaceWriter.NullableTextVal(author) : ColumnValue.Null(),
                    ColumnValue.FromInt64(1, now),
                    ColumnValue.FromInt64(1, now));
            }
            else
            {
                using var writer = new WorkspaceWriter(db);
                writer.WriteContext(new ContextEntry(key, value, author, now, now));
            }

            return $"Set {key} = {value}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Get a context value by key from the repo workspace.")]
    public static string GetContext(
        [Description("Context key to look up")] string key)
    {
        try
        {
            var wsPath = GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);
            var results = reader.ReadContext(key: key);

            return results.Count > 0 ? results[0].Value : $"Key not found: {key}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "List all context entries from the repo workspace.")]
    public static string ListContext()
    {
        try
        {
            var wsPath = GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);
            var entries = reader.ReadContext();

            if (entries.Count == 0) return "No context entries.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Context ({entries.Count} entries)");
            sb.AppendLine();
            foreach (var e in entries)
                sb.AppendLine($"- **{e.Key}** = {e.Value}");
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    internal static string? GetWorkspacePath()
        => Environment.GetEnvironmentVariable("SHARC_WORKSPACE");
}
