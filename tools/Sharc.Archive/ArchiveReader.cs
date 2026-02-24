// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Codec;
using Sharc.Core;

namespace Sharc.Archive;

/// <summary>
/// Reads conversation archive records from a Sharc database using
/// full-scan with optional in-memory filtering. Archive files are
/// small enough that sequential scan is the simplest correct approach.
/// </summary>
public sealed class ArchiveReader
{
    private readonly SharcDatabase _db;

    public ArchiveReader(SharcDatabase db)
    {
        _db = db;
    }

    public IReadOnlyList<ConversationRecord> ReadConversations(string? conversationId = null)
    {
        var results = new List<ConversationRecord>();
        using var reader = _db.CreateReader("conversations");
        while (reader.Read())
        {
            var convId = reader.GetString(0);
            if (conversationId != null && convId != conversationId)
                continue;

            results.Add(new ConversationRecord(
                convId,
                reader.IsNull(1) ? null : reader.GetString(1),
                reader.GetInt64(2),
                reader.IsNull(3) ? null : reader.GetInt64(3),
                reader.IsNull(4) ? null : reader.GetString(4),
                reader.IsNull(5) ? null : reader.GetString(5),
                reader.IsNull(6) ? null : DecodeMetadata(reader, 6)));
        }
        return results;
    }

    public IReadOnlyList<TurnRecord> ReadTurns(string? conversationId = null)
    {
        var results = new List<TurnRecord>();
        using var reader = _db.CreateReader("turns");
        while (reader.Read())
        {
            var convId = reader.GetString(0);
            if (conversationId != null && convId != conversationId)
                continue;

            results.Add(new TurnRecord(
                convId,
                (int)reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.IsNull(5) ? null : (int)reader.GetInt64(5),
                reader.IsNull(6) ? null : DecodeMetadata(reader, 6)));
        }
        return results;
    }

    public IReadOnlyList<AnnotationRecord> ReadAnnotations()
    {
        var results = new List<AnnotationRecord>();
        using var reader = _db.CreateReader("annotations");
        while (reader.Read())
        {
            results.Add(new AnnotationRecord(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsNull(3) ? null : reader.GetString(3),
                reader.IsNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.IsNull(7) ? null : DecodeMetadata(reader, 7)));
        }
        return results;
    }

    public IReadOnlyList<FileAnnotationRecord> ReadFileAnnotations()
    {
        var results = new List<FileAnnotationRecord>();
        using var reader = _db.CreateReader("file_annotations");
        while (reader.Read())
        {
            results.Add(new FileAnnotationRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsNull(3) ? null : reader.GetString(3),
                reader.IsNull(4) ? null : (int)reader.GetInt64(4),
                reader.IsNull(5) ? null : (int)reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsNull(7) ? null : DecodeMetadata(reader, 7)));
        }
        return results;
    }

    public IReadOnlyList<DecisionRecord> ReadDecisions(string? conversationId = null)
    {
        var results = new List<DecisionRecord>();
        using var reader = _db.CreateReader("decisions");
        while (reader.Read())
        {
            var convId = reader.GetString(0);
            if (conversationId != null && convId != conversationId)
                continue;

            results.Add(new DecisionRecord(
                convId,
                reader.IsNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.IsNull(7) ? null : DecodeMetadata(reader, 7)));
        }
        return results;
    }

    public IReadOnlyList<CheckpointRecord> ReadCheckpoints()
    {
        var results = new List<CheckpointRecord>();
        using var reader = _db.CreateReader("checkpoints");
        while (reader.Read())
        {
            results.Add(new CheckpointRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                (int)reader.GetInt64(3),
                (int)reader.GetInt64(4),
                (int)reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsNull(7) ? null : DecodeMetadata(reader, 7)));
        }
        return results;
    }

    private static Dictionary<string, object?>? DecodeMetadata(SharcDataReader reader, int ordinal)
    {
        var blob = reader.GetBlob(ordinal);
        return SharcCbor.Decode(blob);
    }
}
