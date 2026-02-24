// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Repo.Data;

namespace Sharc.Repo.Mcp;

/// <summary>
/// MCP tools for file annotations in workspace.arc.
/// </summary>
[McpServerToolType]
public static class RepoAnnotateTool
{
    [McpServerTool, Description(
        "Annotate a file in the repo workspace with a note, todo, bug, review, or important marker. " +
        "Returns confirmation or error.")]
    public static string AnnotateFile(
        [Description("Relative file path to annotate")] string filePath,
        [Description("Annotation content")] string content,
        [Description("Annotation type: 'note', 'todo', 'bug', 'review', 'important'")] string type = "note",
        [Description("Optional start line number")] int? lineStart = null,
        [Description("Optional end line number")] int? lineEnd = null,
        [Description("Optional author name")] string? author = null)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = true });
            using var writer = new WorkspaceWriter(db);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteFileAnnotation(new FileAnnotationRecord(
                filePath, type, content, lineStart, lineEnd, author, now, null));

            return $"Annotated {filePath}.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "List file annotations from the repo workspace. " +
        "Can filter by file path and annotation type.")]
    public static string ListAnnotations(
        [Description("Optional file path filter")] string? filePath = null,
        [Description("Optional annotation type filter")] string? type = null)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new WorkspaceReader(db);
            var annotations = reader.ReadFileAnnotations(filePath: filePath, type: type);

            if (annotations.Count == 0) return "No annotations.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Annotations ({annotations.Count})");
            sb.AppendLine();
            foreach (var a in annotations)
            {
                sb.Append($"- **{a.FilePath}** [{a.AnnotationType}]");
                if (a.LineStart.HasValue)
                    sb.Append(a.LineEnd.HasValue && a.LineEnd != a.LineStart
                        ? $" L{a.LineStart}-{a.LineEnd}"
                        : $" L{a.LineStart}");
                sb.AppendLine($": {a.Content}");
            }
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
