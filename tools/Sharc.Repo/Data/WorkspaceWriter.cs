// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Codec;
using Sharc.Core;

namespace Sharc.Repo.Data;

/// <summary>
/// Writes workspace records to a Sharc database using the native
/// <see cref="SharcWriter"/> engine. Metadata BLOBs are encoded with <see cref="SharcCbor"/>.
/// </summary>
public sealed class WorkspaceWriter : IDisposable
{
    private readonly SharcWriter _writer;
    private bool _disposed;

    public WorkspaceWriter(SharcDatabase db)
    {
        _writer = SharcWriter.From(db);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }

    // ── Git History ─────────────────────────────────────────────────

    public long WriteCommit(GitCommitRecord r)
    {
        return _writer.Insert("commits",
            TextVal(r.Sha),
            TextVal(r.AuthorName),
            TextVal(r.AuthorEmail),
            ColumnValue.FromInt64(1, r.AuthoredAt),
            TextVal(r.Message));
    }

    public long WriteFileChange(GitFileChangeRecord r)
    {
        return _writer.Insert("file_changes",
            ColumnValue.FromInt64(1, r.CommitId),
            TextVal(r.Path),
            ColumnValue.FromInt64(1, r.LinesAdded),
            ColumnValue.FromInt64(1, r.LinesDeleted));
    }

    // ── Notes ───────────────────────────────────────────────────────

    public long WriteNote(NoteRecord r)
    {
        return _writer.Insert("notes",
            TextVal(r.Content),
            NullableTextVal(r.Tag),
            NullableTextVal(r.Author),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    // ── File Annotations ────────────────────────────────────────────

    public long WriteFileAnnotation(FileAnnotationRecord r)
    {
        return _writer.Insert("file_annotations",
            TextVal(r.FilePath),
            TextVal(r.AnnotationType),
            NullableTextVal(r.Content),
            NullableInt64Val(r.LineStart),
            NullableInt64Val(r.LineEnd),
            NullableTextVal(r.Author),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    // ── Decisions ───────────────────────────────────────────────────

    public long WriteDecision(DecisionRecord r)
    {
        return _writer.Insert("decisions",
            TextVal(r.DecisionId),
            TextVal(r.Title),
            NullableTextVal(r.Rationale),
            TextVal(r.Status),
            NullableTextVal(r.Author),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    // ── Context Key-Value ───────────────────────────────────────────

    public long WriteContext(ContextEntry r)
    {
        return _writer.Insert("context",
            TextVal(r.Key),
            TextVal(r.Value),
            NullableTextVal(r.Author),
            ColumnValue.FromInt64(1, r.CreatedAt),
            ColumnValue.FromInt64(1, r.UpdatedAt));
    }

    // ── Conversations ───────────────────────────────────────────────

    public long WriteConversationTurn(ConversationTurnRecord r)
    {
        return _writer.Insert("conversations",
            TextVal(r.SessionId),
            TextVal(r.Role),
            TextVal(r.Content),
            NullableTextVal(r.ToolName),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    // ── Workspace Meta ──────────────────────────────────────────────

    public long WriteMeta(string key, string value)
    {
        return _writer.Insert("_workspace_meta",
            TextVal(key),
            TextVal(value));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    internal static ColumnValue TextVal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    internal static ColumnValue NullableTextVal(string? value)
    {
        if (value == null) return ColumnValue.Null();
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    internal static ColumnValue NullableInt64Val(int? value)
    {
        if (!value.HasValue) return ColumnValue.Null();
        return ColumnValue.FromInt64(1, value.Value);
    }

    internal static ColumnValue MetadataVal(IDictionary<string, object?>? metadata)
    {
        if (metadata == null) return ColumnValue.Null();
        var bytes = SharcCbor.Encode(metadata);
        return ColumnValue.Blob(2 * bytes.Length + 12, bytes);
    }
}
