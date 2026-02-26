// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo.Data;

/// <summary>
/// Reads knowledge graph records from workspace.arc via full-table scans
/// with optional in-memory filtering. Supports orthogonal slicing by feature,
/// file, doc, and gap analysis.
/// </summary>
public sealed class KnowledgeReader
{
    private readonly SharcDatabase _db;

    public KnowledgeReader(SharcDatabase db)
    {
        _db = db;
    }

    // ── Features ─────────────────────────────────────────────────────

    public IReadOnlyList<FeatureRecord> ReadFeatures(string? layer = null, string? status = null)
    {
        var result = new List<FeatureRecord>();
        using var reader = _db.CreateReader("features");
        while (reader.Read())
        {
            var l = reader.GetString(2); // layer
            var s = reader.GetString(3); // status
            if (layer != null && !string.Equals(l, layer, StringComparison.OrdinalIgnoreCase))
                continue;
            if (status != null && !string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new FeatureRecord(
                Name: reader.GetString(0),
                Description: reader.IsNull(1) ? null : reader.GetString(1),
                Layer: l,
                Status: s,
                CreatedAt: reader.GetInt64(4),
                Metadata: null));
        }
        return result;
    }

    public FeatureRecord? GetFeature(string name)
    {
        using var reader = _db.CreateReader("features");
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), name, StringComparison.OrdinalIgnoreCase))
            {
                return new FeatureRecord(
                    Name: reader.GetString(0),
                    Description: reader.IsNull(1) ? null : reader.GetString(1),
                    Layer: reader.GetString(2),
                    Status: reader.GetString(3),
                    CreatedAt: reader.GetInt64(4),
                    Metadata: null);
            }
        }
        return null;
    }

    // ── Feature Edges ────────────────────────────────────────────────

    public IReadOnlyList<FeatureEdgeRecord> ReadFeatureEdges(
        string? featureName = null, string? targetKind = null, string? targetPath = null)
    {
        var result = new List<FeatureEdgeRecord>();
        using var reader = _db.CreateReader("feature_edges");
        while (reader.Read())
        {
            var fn = reader.GetString(0); // feature_name
            var tp = reader.GetString(1); // target_path
            var tk = reader.GetString(2); // target_kind

            if (featureName != null && !string.Equals(fn, featureName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (targetKind != null && !string.Equals(tk, targetKind, StringComparison.OrdinalIgnoreCase))
                continue;
            if (targetPath != null && !string.Equals(tp, targetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new FeatureEdgeRecord(
                FeatureName: fn,
                TargetPath: tp,
                TargetKind: tk,
                Role: reader.IsNull(3) ? null : reader.GetString(3),
                AutoDetected: reader.GetInt64(4) == 1,
                CreatedAt: reader.GetInt64(5),
                Metadata: null));
        }
        return result;
    }

    // ── File Purposes ────────────────────────────────────────────────

    public IReadOnlyList<FilePurposeRecord> ReadFilePurposes(string? project = null, string? layer = null)
    {
        var result = new List<FilePurposeRecord>();
        using var reader = _db.CreateReader("file_purposes");
        while (reader.Read())
        {
            var p = reader.GetString(2); // project
            var l = reader.IsNull(4) ? null : reader.GetString(4); // layer

            if (project != null && !string.Equals(p, project, StringComparison.OrdinalIgnoreCase))
                continue;
            if (layer != null && !string.Equals(l, layer, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new FilePurposeRecord(
                Path: reader.GetString(0),
                Purpose: reader.GetString(1),
                Project: p,
                Namespace: reader.IsNull(3) ? null : reader.GetString(3),
                Layer: l,
                AutoDetected: reader.GetInt64(5) == 1,
                CreatedAt: reader.GetInt64(6),
                Metadata: null));
        }
        return result;
    }

    public FilePurposeRecord? GetFilePurpose(string path)
    {
        using var reader = _db.CreateReader("file_purposes");
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), path, StringComparison.OrdinalIgnoreCase))
            {
                return new FilePurposeRecord(
                    Path: reader.GetString(0),
                    Purpose: reader.GetString(1),
                    Project: reader.GetString(2),
                    Namespace: reader.IsNull(3) ? null : reader.GetString(3),
                    Layer: reader.IsNull(4) ? null : reader.GetString(4),
                    AutoDetected: reader.GetInt64(5) == 1,
                    CreatedAt: reader.GetInt64(6),
                    Metadata: null);
            }
        }
        return null;
    }

    // ── File Deps ────────────────────────────────────────────────────

    public IReadOnlyList<FileDepRecord> ReadFileDeps(string? sourcePath = null, string? targetPath = null)
    {
        var result = new List<FileDepRecord>();
        using var reader = _db.CreateReader("file_deps");
        while (reader.Read())
        {
            var sp = reader.GetString(0); // source_path
            var tp = reader.GetString(1); // target_path

            if (sourcePath != null && !string.Equals(sp, sourcePath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (targetPath != null && !string.Equals(tp, targetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new FileDepRecord(
                SourcePath: sp,
                TargetPath: tp,
                DepKind: reader.GetString(2),
                AutoDetected: reader.GetInt64(3) == 1,
                CreatedAt: reader.GetInt64(4)));
        }
        return result;
    }

    // ── Gap Analysis ─────────────────────────────────────────────────

    /// <summary>
    /// Returns feature names that have no edges with target_kind='doc'.
    /// </summary>
    public IReadOnlyList<string> FindFeaturesWithoutDocs()
    {
        var allFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ReadFeatures())
            allFeatures.Add(f.Name);

        var documented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ReadFeatureEdges(targetKind: "doc"))
            documented.Add(e.FeatureName);

        var gaps = new List<string>();
        foreach (var name in allFeatures)
        {
            if (!documented.Contains(name))
                gaps.Add(name);
        }
        return gaps;
    }

    /// <summary>
    /// Returns file paths from file_purposes that have no feature_edges pointing to them.
    /// </summary>
    public IReadOnlyList<string> FindOrphanFiles()
    {
        var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fp in ReadFilePurposes())
            allPaths.Add(fp.Path);

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ReadFeatureEdges())
            referenced.Add(e.TargetPath);

        var orphans = new List<string>();
        foreach (var path in allPaths)
        {
            if (!referenced.Contains(path))
                orphans.Add(path);
        }
        return orphans;
    }

    /// <summary>
    /// Returns source file paths from feature_edges that have no corresponding
    /// test edge for the same feature.
    /// </summary>
    public IReadOnlyList<string> FindFilesWithoutTests()
    {
        var sourcesByFeature = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var testedFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in ReadFeatureEdges())
        {
            if (string.Equals(e.TargetKind, "source", StringComparison.OrdinalIgnoreCase))
            {
                if (!sourcesByFeature.TryGetValue(e.FeatureName, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    sourcesByFeature[e.FeatureName] = set;
                }
                set.Add(e.TargetPath);
            }
            else if (string.Equals(e.TargetKind, "test", StringComparison.OrdinalIgnoreCase))
            {
                testedFeatures.Add(e.FeatureName);
            }
        }

        var untested = new List<string>();
        foreach (var (feature, files) in sourcesByFeature)
        {
            if (!testedFeatures.Contains(feature))
            {
                foreach (var f in files)
                    untested.Add(f);
            }
        }
        return untested;
    }
}
