// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Codec;
using Sharc.Core;

namespace Sharc.Archive;

/// <summary>
/// Writes conversation archive records to a Sharc database using
/// the native <see cref="SharcWriter"/> engine. All metadata BLOBs
/// are encoded with <see cref="SharcCbor"/>.
/// </summary>
public sealed class ArchiveWriter : IDisposable
{
    private readonly SharcWriter _writer;
    private bool _disposed;

    public ArchiveWriter(SharcDatabase db)
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

    public long WriteConversation(ConversationRecord r)
    {
        return _writer.Insert("conversations",
            TextVal(r.ConversationId),
            NullableTextVal(r.Title),
            ColumnValue.FromInt64(1, r.StartedAt),
            NullableInt64Val(r.EndedAt),
            NullableTextVal(r.AgentId),
            NullableTextVal(r.Source),
            MetadataVal(r.Metadata));
    }

    public long[] WriteTurns(IReadOnlyList<TurnRecord> turns)
    {
        if (turns.Count == 0) return Array.Empty<long>();

        var rowIds = new long[turns.Count];
        for (int i = 0; i < turns.Count; i++)
        {
            var t = turns[i];
            rowIds[i] = _writer.Insert("turns",
                TextVal(t.ConversationId),
                ColumnValue.FromInt64(1, t.TurnIndex),
                TextVal(t.Role),
                TextVal(t.Content),
                ColumnValue.FromInt64(1, t.CreatedAt),
                NullableInt64Val(t.TokenCount),
                MetadataVal(t.Metadata));
        }
        return rowIds;
    }

    public long WriteAnnotation(AnnotationRecord r)
    {
        return _writer.Insert("annotations",
            TextVal(r.TargetType),
            ColumnValue.FromInt64(1, r.TargetId),
            TextVal(r.AnnotationType),
            NullableTextVal(r.Verdict),
            NullableTextVal(r.Content),
            TextVal(r.AnnotatorId),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    public long WriteFileAnnotation(FileAnnotationRecord r)
    {
        return _writer.Insert("file_annotations",
            ColumnValue.FromInt64(1, r.TurnId),
            TextVal(r.FilePath),
            TextVal(r.AnnotationType),
            NullableTextVal(r.Content),
            NullableInt64Val(r.LineStart),
            NullableInt64Val(r.LineEnd),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    public long WriteDecision(DecisionRecord r)
    {
        return _writer.Insert("decisions",
            TextVal(r.ConversationId),
            NullableInt64Val(r.TurnId),
            TextVal(r.DecisionId),
            TextVal(r.Title),
            NullableTextVal(r.Rationale),
            TextVal(r.Status),
            ColumnValue.FromInt64(1, r.CreatedAt),
            MetadataVal(r.Metadata));
    }

    public long WriteCheckpoint(CheckpointRecord r)
    {
        return _writer.Insert("checkpoints",
            TextVal(r.CheckpointId),
            TextVal(r.Label),
            ColumnValue.FromInt64(1, r.CreatedAt),
            ColumnValue.FromInt64(1, r.ConversationCount),
            ColumnValue.FromInt64(1, r.TurnCount),
            ColumnValue.FromInt64(1, r.AnnotationCount),
            ColumnValue.FromInt64(1, r.LedgerSequence),
            MetadataVal(r.Metadata));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ColumnValue TextVal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    private static ColumnValue NullableTextVal(string? value)
    {
        if (value == null) return ColumnValue.Null();
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(2 * bytes.Length + 13, bytes);
    }

    private static ColumnValue NullableInt64Val(long? value)
    {
        if (!value.HasValue) return ColumnValue.Null();
        return ColumnValue.FromInt64(1, value.Value);
    }

    private static ColumnValue NullableInt64Val(int? value)
    {
        if (!value.HasValue) return ColumnValue.Null();
        return ColumnValue.FromInt64(1, value.Value);
    }

    private static ColumnValue MetadataVal(IDictionary<string, object?>? metadata)
    {
        if (metadata == null) return ColumnValue.Null();
        var bytes = SharcCbor.Encode(metadata);
        return ColumnValue.Blob(2 * bytes.Length + 12, bytes);
    }
}
