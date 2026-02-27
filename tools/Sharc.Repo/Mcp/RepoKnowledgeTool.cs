// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Sharc.Repo.Data;

namespace Sharc.Repo.Mcp;

/// <summary>
/// MCP tools for querying the knowledge graph: features, file purposes, edges, and gap analysis.
/// </summary>
[McpServerToolType]
public static class RepoKnowledgeTool
{
    [McpServerTool, Description(
        "List features from the knowledge graph with optional layer/status filters. " +
        "Returns a markdown table of features.")]
    public static string ListFeatures(
        [Description("Filter by layer (e.g. 'core', 'crypto', 'api', 'write', 'trust', 'graph', 'query', 'vector')")] string? layer = null,
        [Description("Filter by status (e.g. 'complete', 'active', 'planned')")] string? status = null)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new KnowledgeReader(db);
            var features = reader.ReadFeatures(layer, status);

            if (features.Count == 0)
                return "No features found. Run `sharc scan` first.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Features ({features.Count})");
            sb.AppendLine();
            sb.AppendLine("| Name | Layer | Status | Description |");
            sb.AppendLine("|------|-------|--------|-------------|");

            foreach (var f in features)
                sb.AppendLine($"| {f.Name} | {f.Layer} | {f.Status} | {f.Description ?? ""} |");

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Show full cross-reference for a feature: source files, tests, docs, benchmarks, and dependencies. " +
        "Returns markdown with categorized file lists.")]
    public static string ShowFeature(
        [Description("Feature name (e.g. 'encryption', 'btree-read', 'write-engine')")] string name)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new KnowledgeReader(db);

            var feature = reader.GetFeature(name);
            if (feature == null) return $"Feature not found: {name}";

            var sb = new StringBuilder();
            sb.AppendLine($"# {feature.Name}");
            sb.AppendLine();
            sb.AppendLine($"- **Description:** {feature.Description ?? "(none)"}");
            sb.AppendLine($"- **Layer:** {feature.Layer}");
            sb.AppendLine($"- **Status:** {feature.Status}");

            var edges = reader.ReadFeatureEdges(featureName: name);
            AppendEdgeSection(sb, "Source Files", edges.Where(e => e.TargetKind == "source"));
            AppendEdgeSection(sb, "Test Files", edges.Where(e => e.TargetKind == "test"));
            AppendEdgeSection(sb, "Documentation", edges.Where(e => e.TargetKind == "doc"));
            AppendEdgeSection(sb, "Benchmarks", edges.Where(e => e.TargetKind == "bench"));

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Show what a specific file does: purpose, features it belongs to, dependencies. " +
        "Returns markdown with file metadata and cross-references.")]
    public static string ShowFile(
        [Description("Relative file path (e.g. 'src/Sharc.Crypto/AesGcmCipher.cs')")] string path)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new KnowledgeReader(db);

            var fp = reader.GetFilePurpose(path);
            var sb = new StringBuilder();

            if (fp != null)
            {
                sb.AppendLine($"# {fp.Path}");
                sb.AppendLine();
                sb.AppendLine($"- **Purpose:** {fp.Purpose}");
                sb.AppendLine($"- **Project:** {fp.Project}");
                if (fp.Namespace != null) sb.AppendLine($"- **Namespace:** {fp.Namespace}");
                if (fp.Layer != null) sb.AppendLine($"- **Layer:** {fp.Layer}");
            }
            else
            {
                sb.AppendLine($"# {path}");
                sb.AppendLine();
                sb.AppendLine("(no file purpose recorded — run `sharc scan`)");
            }

            // Features this file belongs to
            var edges = reader.ReadFeatureEdges(targetPath: path);
            if (edges.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Features");
                sb.AppendLine();
                foreach (var e in edges)
                    sb.AppendLine($"- **{e.FeatureName}** ({e.TargetKind})");
            }

            // Dependencies
            var deps = reader.ReadFileDeps(sourcePath: path);
            if (deps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Dependencies");
                sb.AppendLine();
                foreach (var d in deps)
                    sb.AppendLine($"- {d.TargetPath} ({d.DepKind})");
            }

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Find coverage gaps in the knowledge graph. " +
        "Returns files without tests, features without docs, and orphan files.")]
    public static string FindGaps(
        [Description("Gap kind: 'tests' (source files in untested features), 'docs' (features without docs), 'orphans' (files not mapped to features), or 'all'")] string kind = "all")
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new KnowledgeReader(db);

            var sb = new StringBuilder();
            sb.AppendLine("# Gap Analysis");
            sb.AppendLine();

            bool showDocs = kind is "all" or "docs";
            bool showTests = kind is "all" or "tests";
            bool showOrphans = kind is "all" or "orphans";
            bool anyGaps = false;

            if (showDocs)
            {
                var gapped = reader.FindFeaturesWithoutDocs();
                if (gapped.Count > 0)
                {
                    anyGaps = true;
                    sb.AppendLine($"## Features Without Documentation ({gapped.Count})");
                    sb.AppendLine();
                    foreach (var name in gapped)
                        sb.AppendLine($"- {name}");
                    sb.AppendLine();
                }
            }

            if (showTests)
            {
                var untested = reader.FindFilesWithoutTests();
                if (untested.Count > 0)
                {
                    anyGaps = true;
                    sb.AppendLine($"## Source Files in Untested Features ({untested.Count})");
                    sb.AppendLine();
                    foreach (var path in untested)
                        sb.AppendLine($"- {path}");
                    sb.AppendLine();
                }
            }

            if (showOrphans)
            {
                var orphans = reader.FindOrphanFiles();
                if (orphans.Count > 0)
                {
                    anyGaps = true;
                    sb.AppendLine($"## Orphan Files ({orphans.Count})");
                    sb.AppendLine();
                    foreach (var path in orphans)
                        sb.AppendLine($"- {path}");
                    sb.AppendLine();
                }
            }

            if (!anyGaps)
                sb.AppendLine("No gaps found.");

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool, Description(
        "Trace dependency impact: find all files that depend on a given project/path. " +
        "Useful for understanding blast radius of changes.")]
    public static string TraceDeps(
        [Description("Target project or path to trace dependents for (e.g. 'Sharc.Core')")] string target)
    {
        try
        {
            var wsPath = RepoContextTool.GetWorkspacePath();
            if (wsPath == null) return "Error: SHARC_WORKSPACE not set.";

            using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
            var reader = new KnowledgeReader(db);

            var deps = reader.ReadFileDeps(targetPath: target);
            if (deps.Count == 0)
                return $"No files depend on '{target}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"# Dependents of {target} ({deps.Count})");
            sb.AppendLine();

            foreach (var d in deps)
                sb.AppendLine($"- {d.SourcePath} ({d.DepKind})");

            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static void AppendEdgeSection(StringBuilder sb, string title, IEnumerable<FeatureEdgeRecord> edges)
    {
        var list = edges.ToList();
        if (list.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"## {title} ({list.Count})");
        sb.AppendLine();
        foreach (var e in list)
            sb.AppendLine($"- {e.TargetPath}");
    }
}
