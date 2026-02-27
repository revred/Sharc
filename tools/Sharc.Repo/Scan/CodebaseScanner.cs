// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Sharc.Repo.Scan;

/// <summary>
/// Orchestrates scanning of the codebase to produce knowledge graph records:
/// features, feature edges, file purposes, and file dependencies.
/// </summary>
public sealed partial class CodebaseScanner
{
    private readonly string _repoRoot;
    private readonly long _now;

    public CodebaseScanner(string repoRoot)
    {
        _repoRoot = repoRoot;
        _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>Complete scan result.</summary>
    public sealed class ScanResult
    {
        public List<FeatureRecord> Features { get; } = [];
        public List<FeatureEdgeRecord> FeatureEdges { get; } = [];
        public List<FilePurposeRecord> FilePurposes { get; } = [];
        public List<FileDepRecord> FileDeps { get; } = [];
    }

    /// <summary>
    /// Runs the full 4-phase scan: features, file purposes, feature edges, dependencies.
    /// </summary>
    public ScanResult Scan()
    {
        var result = new ScanResult();

        // Phase 1: Register all features from the catalog
        ScanFeatures(result);

        // Phase 2: Discover source files and generate file purposes
        ScanFilePurposes(result);

        // Phase 3: Build feature edges (source → feature, test → feature, doc → feature)
        ScanFeatureEdges(result);

        // Phase 4: Scan file dependencies (using directives)
        ScanFileDeps(result);

        return result;
    }

    private void ScanFeatures(ScanResult result)
    {
        foreach (var def in FeatureCatalog.All)
        {
            result.Features.Add(new FeatureRecord(
                def.Name, def.Description, def.Layer, def.Status, _now, null));
        }
    }

    private void ScanFilePurposes(ScanResult result)
    {
        foreach (var dir in new[] { "src", "tests", "tools", "bench" })
        {
            var fullDir = Path.Combine(_repoRoot, dir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.EnumerateFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(_repoRoot, file).Replace('\\', '/');
                var (project, ns, layer) = InferFileMetadata(relativePath);
                var purpose = InferPurpose(relativePath);

                result.FilePurposes.Add(new FilePurposeRecord(
                    relativePath, purpose, project, ns, layer, true, _now, null));
            }
        }
    }

    private void ScanFeatureEdges(ScanResult result)
    {
        // Source file → feature edges
        foreach (var fp in result.FilePurposes)
        {
            var features = FeatureCatalog.MatchFile(fp.Path);
            foreach (var featureName in features)
            {
                string kind = InferEdgeKind(fp.Path);
                result.FeatureEdges.Add(new FeatureEdgeRecord(
                    featureName, fp.Path, kind, null, true, _now, null));
            }
        }

        // Doc files → feature edges (via keyword + file reference scanning)
        foreach (var docDir in new[] { "PRC", "docs" })
        {
            var fullDir = Path.Combine(_repoRoot, docDir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.EnumerateFiles(fullDir, "*.md", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(_repoRoot, file).Replace('\\', '/');
                var content = File.ReadAllText(file);
                var scan = DocScanner.ScanDocument(content);

                foreach (var featureName in scan.MatchedFeatures)
                {
                    result.FeatureEdges.Add(new FeatureEdgeRecord(
                        featureName, relativePath, "doc", null, true, _now, null));
                }
            }
        }
    }

    private void ScanFileDeps(ScanResult result)
    {
        foreach (var fp in result.FilePurposes)
        {
            if (!fp.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            var fullPath = Path.Combine(_repoRoot, fp.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath);
            var usings = ExtractUsingDirectives(content);

            foreach (var ns in usings)
            {
                // Map namespace back to a known project
                var targetProject = MapNamespaceToProject(ns);
                if (targetProject != null && !targetProject.Equals(fp.Project, StringComparison.OrdinalIgnoreCase))
                {
                    result.FileDeps.Add(new FileDepRecord(
                        fp.Path, targetProject, "using", true, _now));
                }
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string InferEdgeKind(string path)
    {
        if (path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
            return "test";
        if (path.StartsWith("bench/", StringComparison.OrdinalIgnoreCase))
            return "bench";
        if (path.StartsWith("PRC/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
            return "doc";
        return "source";
    }

    private static (string project, string? ns, string? layer) InferFileMetadata(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length < 2) return ("Unknown", null, null);

        // src/Sharc.Core/BTree/BTreeReader.cs → project=Sharc.Core, ns=Sharc.Core.BTree
        // tests/Sharc.Tests/BTree/Test.cs → project=Sharc.Tests, ns=Sharc.Tests.BTree
        string project = parts[1];

        // Build namespace from directory parts (excluding file name)
        string? ns = parts.Length > 3
            ? string.Join(".", parts[1..^1])
            : parts[1];

        string? layer = InferLayer(relativePath);

        return (project, ns, layer);
    }

    private static string? InferLayer(string path)
    {
        if (path.Contains("/Crypto/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("src/Sharc.Crypto/", StringComparison.OrdinalIgnoreCase))
            return "crypto";
        if (path.Contains("/Trust/", StringComparison.OrdinalIgnoreCase))
            return "trust";
        if (path.Contains("/BTree/", StringComparison.OrdinalIgnoreCase))
            return "btree";
        if (path.Contains("/IO/", StringComparison.OrdinalIgnoreCase))
            return "io";
        if (path.Contains("/Write/", StringComparison.OrdinalIgnoreCase))
            return "write";
        if (path.Contains("/Graph/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("src/Sharc.Graph", StringComparison.OrdinalIgnoreCase))
            return "graph";
        if (path.StartsWith("src/Sharc.Query/", StringComparison.OrdinalIgnoreCase))
            return "query";
        if (path.StartsWith("src/Sharc.Vector/", StringComparison.OrdinalIgnoreCase))
            return "vector";
        if (path.StartsWith("src/Sharc.Arc/", StringComparison.OrdinalIgnoreCase))
            return "arc";
        if (path.StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
            return "tools";
        if (path.StartsWith("bench/", StringComparison.OrdinalIgnoreCase))
            return "bench";
        if (path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
            return "test";
        return "api";
    }

    private static string InferPurpose(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);

        // Remove common suffixes for cleaner purpose
        if (fileName.EndsWith("Tests", StringComparison.Ordinal)) fileName = fileName[..^5] + " tests";
        if (fileName.EndsWith("Benchmarks", StringComparison.Ordinal)) fileName = fileName[..^10] + " benchmarks";

        // Convert PascalCase to readable form
        return PascalToWords(fileName);
    }

    private static string PascalToWords(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return PascalCaseRegex().Replace(input, " $1").Trim();
    }

    public static List<string> ExtractUsingDirectives(string content)
    {
        var usings = new List<string>();
        var matches = UsingRegex().Matches(content);
        foreach (Match m in matches)
        {
            var ns = m.Groups[1].Value;
            if (ns.StartsWith("Sharc", StringComparison.Ordinal))
            {
                usings.Add(ns);
            }
        }
        return usings;
    }

    private static string? MapNamespaceToProject(string ns)
    {
        // Map Sharc namespaces to project directories
        if (ns.StartsWith("Sharc.Core", StringComparison.Ordinal)) return "Sharc.Core";
        if (ns.StartsWith("Sharc.Crypto", StringComparison.Ordinal)) return "Sharc.Crypto";
        if (ns.StartsWith("Sharc.Graph.Surface", StringComparison.Ordinal)) return "Sharc.Graph.Surface";
        if (ns.StartsWith("Sharc.Graph", StringComparison.Ordinal)) return "Sharc.Graph";
        if (ns.StartsWith("Sharc.Query", StringComparison.Ordinal)) return "Sharc.Query";
        if (ns.StartsWith("Sharc.Vector", StringComparison.Ordinal)) return "Sharc.Vector";
        if (ns.StartsWith("Sharc.Arc", StringComparison.Ordinal)) return "Sharc.Arc";
        if (ns.StartsWith("Sharc.Repo", StringComparison.Ordinal)) return "Sharc.Repo";
        if (ns.StartsWith("Sharc.Index", StringComparison.Ordinal)) return "Sharc.Index";
        if (ns.StartsWith("Sharc", StringComparison.Ordinal)) return "Sharc";
        return null;
    }

    [GeneratedRegex(@"(?<=\p{Ll})(\p{Lu})")]
    private static partial Regex PascalCaseRegex();

    [GeneratedRegex(@"^using\s+(?:static\s+)?(?:global::)?([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();
}
