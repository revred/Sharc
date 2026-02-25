// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Repo.Data;

/// <summary>
/// Reads workspace records from a Sharc database via full-table scans
/// with optional in-memory filtering.
/// </summary>
public sealed class WorkspaceReader
{
    private readonly SharcDatabase _db;

    public WorkspaceReader(SharcDatabase db)
    {
        _db = db;
    }

    // ── Notes ───────────────────────────────────────────────────────

    public IReadOnlyList<NoteRecord> ReadNotes(string? tag = null)
    {
        var result = new List<NoteRecord>();
        using var reader = _db.CreateReader("notes");
        while (reader.Read())
        {
            var noteTag = reader.IsNull(1) ? null : reader.GetString(1);
            if (tag != null && !string.Equals(noteTag, tag, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new NoteRecord(
                Content: reader.GetString(0),
                Tag: noteTag,
                Author: reader.IsNull(2) ? null : reader.GetString(2),
                CreatedAt: reader.GetInt64(3),
                Metadata: null));
        }
        return result;
    }

    // ── File Annotations ────────────────────────────────────────────

    public IReadOnlyList<FileAnnotationRecord> ReadFileAnnotations(string? filePath = null, string? type = null)
    {
        var result = new List<FileAnnotationRecord>();
        using var reader = _db.CreateReader("file_annotations");
        while (reader.Read())
        {
            var fp = reader.GetString(0);
            var at = reader.GetString(1);
            if (filePath != null && !string.Equals(fp, filePath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (type != null && !string.Equals(at, type, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new FileAnnotationRecord(
                FilePath: fp,
                AnnotationType: at,
                Content: reader.IsNull(2) ? null : reader.GetString(2),
                LineStart: reader.IsNull(3) ? null : (int)reader.GetInt64(3),
                LineEnd: reader.IsNull(4) ? null : (int)reader.GetInt64(4),
                Author: reader.IsNull(5) ? null : reader.GetString(5),
                CreatedAt: reader.GetInt64(6),
                Metadata: null));
        }
        return result;
    }

    // ── Decisions ───────────────────────────────────────────────────

    public IReadOnlyList<DecisionRecord> ReadDecisions(string? status = null)
    {
        var result = new List<DecisionRecord>();
        using var reader = _db.CreateReader("decisions");
        while (reader.Read())
        {
            var s = reader.GetString(3);
            if (status != null && !string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new DecisionRecord(
                DecisionId: reader.GetString(0),
                Title: reader.GetString(1),
                Rationale: reader.IsNull(2) ? null : reader.GetString(2),
                Status: s,
                Author: reader.IsNull(4) ? null : reader.GetString(4),
                CreatedAt: reader.GetInt64(5),
                Metadata: null));
        }
        return result;
    }

    // ── Context Key-Value ───────────────────────────────────────────

    public IReadOnlyList<ContextEntry> ReadContext(string? key = null)
    {
        var result = new List<ContextEntry>();
        using var reader = _db.CreateReader("context");
        while (reader.Read())
        {
            var k = reader.GetString(0);
            if (key != null && !string.Equals(k, key, StringComparison.Ordinal))
                continue;

            result.Add(new ContextEntry(
                Key: k,
                Value: reader.GetString(1),
                Author: reader.IsNull(2) ? null : reader.GetString(2),
                CreatedAt: reader.GetInt64(3),
                UpdatedAt: reader.GetInt64(4)));
        }
        return result;
    }

    // ── Table Row Count (for status) ────────────────────────────────

    public int CountRows(string tableName)
    {
        int count = 0;
        using var reader = _db.CreateReader(tableName);
        while (reader.Read()) count++;
        return count;
    }

    // ── Workspace Meta ──────────────────────────────────────────────

    public string? GetMeta(string key)
    {
        using var reader = _db.CreateReader("_workspace_meta");
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), key, StringComparison.Ordinal))
                return reader.GetString(1);
        }
        return null;
    }
}
