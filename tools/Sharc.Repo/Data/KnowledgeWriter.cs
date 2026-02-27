// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using static Sharc.Repo.Data.WorkspaceWriter;

namespace Sharc.Repo.Data;

/// <summary>
/// Writes knowledge graph records (features, edges, file purposes, deps) to workspace.arc.
/// Uses scan-before-insert deduplication for features (by name) and file_purposes (by path).
/// </summary>
public sealed class KnowledgeWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly SharcWriter _writer;
    private readonly HashSet<string> _knownFeatures;
    private readonly HashSet<string> _knownFilePurposes;
    private bool _disposed;

    public KnowledgeWriter(SharcDatabase db)
    {
        _db = db;
        _writer = SharcWriter.From(db);
        _knownFeatures = LoadKnownNames("features");
        _knownFilePurposes = LoadKnownPaths("file_purposes");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    // ── Features ─────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a feature record. Duplicates by name are silently skipped.
    /// </summary>
    public long WriteFeature(FeatureRecord r)
    {
        if (_knownFeatures.Contains(r.Name))
            return -1;

        long id = _writer.Insert("features",
            TextVal(r.Name),
            NullableTextVal(r.Description),
            TextVal(r.Layer),
            TextVal(r.Status),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));

        _knownFeatures.Add(r.Name);
        return id;
    }

    // ── Feature Edges ────────────────────────────────────────────────

    /// <summary>
    /// Inserts a feature-to-file cross-reference edge.
    /// </summary>
    public long WriteFeatureEdge(FeatureEdgeRecord r)
    {
        return _writer.Insert("feature_edges",
            TextVal(r.FeatureName),
            TextVal(r.TargetPath),
            TextVal(r.TargetKind),
            NullableTextVal(r.Role),
            ColumnValue.FromInt64(1, r.AutoDetected ? 1 : 0),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    // ── File Purposes ────────────────────────────────────────────────

    /// <summary>
    /// Inserts a file purpose record. Duplicates by path are silently skipped.
    /// </summary>
    public long WriteFilePurpose(FilePurposeRecord r)
    {
        if (_knownFilePurposes.Contains(r.Path))
            return -1;

        long id = _writer.Insert("file_purposes",
            TextVal(r.Path),
            TextVal(r.Purpose),
            TextVal(r.Project),
            NullableTextVal(r.Namespace),
            NullableTextVal(r.Layer),
            ColumnValue.FromInt64(1, r.AutoDetected ? 1 : 0),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));

        _knownFilePurposes.Add(r.Path);
        return id;
    }

    // ── File Deps ────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a file dependency record.
    /// </summary>
    public long WriteFileDep(FileDepRecord r)
    {
        return _writer.Insert("file_deps",
            TextVal(r.SourcePath),
            TextVal(r.TargetPath),
            TextVal(r.DepKind),
            ColumnValue.FromInt64(1, r.AutoDetected ? 1 : 0),
            ColumnValue.FromInt64(1, r.CreatedAt));
    }

    // ── Bulk Clear (for --full re-scan) ──────────────────────────────

    /// <summary>
    /// Clears all auto-detected entries from knowledge tables.
    /// Manual entries (auto_detected=0) are preserved.
    /// </summary>
    public void ClearAutoDetected()
    {
        ClearAutoDetectedFromTable("feature_edges", autoDetectedOrdinal: 4);
        ClearAutoDetectedFromTable("file_purposes", autoDetectedOrdinal: 5);
        ClearAutoDetectedFromTable("file_deps", autoDetectedOrdinal: 3);
        ClearAllFromTable("features");
        _knownFeatures.Clear();
        _knownFilePurposes.Clear();
    }

    private void ClearAutoDetectedFromTable(string tableName, int autoDetectedOrdinal)
    {
        var rowsToDelete = new List<long>();
        using (var reader = _db.CreateReader(tableName))
        {
            while (reader.Read())
            {
                if (reader.GetInt64(autoDetectedOrdinal) == 1)
                    rowsToDelete.Add(reader.RowId);
            }
        }

        foreach (long rowId in rowsToDelete)
            _writer.Delete(tableName, rowId);
    }

    private void ClearAllFromTable(string tableName)
    {
        var rowsToDelete = new List<long>();
        using (var reader = _db.CreateReader(tableName))
        {
            while (reader.Read())
                rowsToDelete.Add(reader.RowId);
        }

        foreach (long rowId in rowsToDelete)
            _writer.Delete(tableName, rowId);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private HashSet<string> LoadKnownNames(string tableName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = _db.CreateReader(tableName);
            while (reader.Read())
                names.Add(reader.GetString(0)); // ordinal 0 = name
        }
        catch { /* table may be empty */ }
        return names;
    }

    private HashSet<string> LoadKnownPaths(string tableName)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = _db.CreateReader(tableName);
            while (reader.Read())
                paths.Add(reader.GetString(0)); // ordinal 0 = path
        }
        catch { /* table may be empty */ }
        return paths;
    }
}
