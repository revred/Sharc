// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive;
using Xunit;

namespace Sharc.Archive.Tests;

public class ArchiveWriterTests : IDisposable
{
    private readonly string _arcPath;
    private readonly SharcDatabase _db;

    public ArchiveWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_writer_{Guid.NewGuid()}.arc");
        _db = ArchiveSchemaBuilder.CreateSchema(_arcPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    private int CountRows(string table)
    {
        int count = 0;
        using var reader = _db.CreateReader(table);
        while (reader.Read()) count++;
        return count;
    }

    // ── Conversations ────────────────────────────────────────────────

    [Fact]
    public void WriteConversation_SingleConversation_InsertsRow()
    {
        using var writer = new ArchiveWriter(_db);
        var record = new ConversationRecord("conv-1", "Test Chat", 1000000, null, "agent-1", "test", null);

        long rowId = writer.WriteConversation(record);

        Assert.True(rowId > 0);
        Assert.Equal(1, CountRows("conversations"));
    }

    [Fact]
    public void WriteConversation_WithMetadata_StoresCborBlob()
    {
        using var writer = new ArchiveWriter(_db);
        var metadata = new Dictionary<string, object?> { ["model"] = "claude-4", ["temp"] = 0.7 };
        var record = new ConversationRecord("conv-2", "Meta Chat", 2000000, null, null, null, metadata);

        writer.WriteConversation(record);

        using var reader = _db.CreateReader("conversations");
        Assert.True(reader.Read());
        Assert.False(reader.IsNull(6)); // metadata column (ordinal 6, id is rowid alias)
    }

    // ── Turns ────────────────────────────────────────────────────────

    [Fact]
    public void WriteTurns_MultipleTurns_InsertsAll()
    {
        using var writer = new ArchiveWriter(_db);
        var turns = new[]
        {
            new TurnRecord("conv-1", 0, "user", "Hello", 1000000, null, null),
            new TurnRecord("conv-1", 1, "assistant", "Hi there!", 1000001, 42, null)
        };

        long[] rowIds = writer.WriteTurns(turns);

        Assert.Equal(2, rowIds.Length);
        Assert.Equal(2, CountRows("turns"));
    }

    [Fact]
    public void WriteTurns_EmptyList_NoOp()
    {
        using var writer = new ArchiveWriter(_db);

        long[] rowIds = writer.WriteTurns(Array.Empty<TurnRecord>());

        Assert.Empty(rowIds);
        Assert.Equal(0, CountRows("turns"));
    }

    // ── Annotations ──────────────────────────────────────────────────

    [Fact]
    public void WriteAnnotation_AnnotateTurn_InsertsRow()
    {
        using var writer = new ArchiveWriter(_db);
        var record = new AnnotationRecord("turn", 1, "quality", "accept", "Good answer", "reviewer-1", 3000000, null);

        long rowId = writer.WriteAnnotation(record);

        Assert.True(rowId > 0);
        Assert.Equal(1, CountRows("annotations"));
    }

    // ── File Annotations ─────────────────────────────────────────────

    [Fact]
    public void WriteFileAnnotation_WithLineRange_InsertsRow()
    {
        using var writer = new ArchiveWriter(_db);
        var record = new FileAnnotationRecord(1, "src/main.cs", "correctness", "Bug here", 10, 20, 4000000, null);

        long rowId = writer.WriteFileAnnotation(record);

        Assert.True(rowId > 0);
        Assert.Equal(1, CountRows("file_annotations"));
    }

    // ── Decisions ────────────────────────────────────────────────────

    [Fact]
    public void WriteDecision_SingleDecision_InsertsRow()
    {
        using var writer = new ArchiveWriter(_db);
        var record = new DecisionRecord("conv-1", 5, "D-001", "Use SharcWriter", "Dogfooding", "accepted", 5000000, null);

        long rowId = writer.WriteDecision(record);

        Assert.True(rowId > 0);
        Assert.Equal(1, CountRows("decisions"));
    }

    // ── Checkpoints ──────────────────────────────────────────────────

    [Fact]
    public void WriteCheckpoint_CapturesState_InsertsRow()
    {
        using var writer = new ArchiveWriter(_db);

        // Add some data first
        writer.WriteConversation(new ConversationRecord("c-1", null, 1000, null, null, null, null));
        writer.WriteTurns(new[] { new TurnRecord("c-1", 0, "user", "test", 1000, null, null) });

        var cp = new CheckpointRecord("cp-1", "milestone-1", 6000000, 1, 1, 0, 0, null);
        long rowId = writer.WriteCheckpoint(cp);

        Assert.True(rowId > 0);
        Assert.Equal(1, CountRows("checkpoints"));
    }

    // ── Round-trip ───────────────────────────────────────────────────

    [Fact]
    public void WriteAndRead_Conversation_DataIntact()
    {
        using var writer = new ArchiveWriter(_db);
        writer.WriteConversation(new ConversationRecord("conv-rt", "Round Trip", 9999, 10000, "agent-x", "cli", null));

        using var reader = _db.CreateReader("conversations");
        Assert.True(reader.Read());
        Assert.Equal("conv-rt", reader.GetString(0));     // conversation_id (id is rowid alias, not exposed)
        Assert.Equal("Round Trip", reader.GetString(1));   // title
        Assert.Equal(9999, reader.GetInt64(2));             // started_at
        Assert.Equal(10000, reader.GetInt64(3));            // ended_at
        Assert.Equal("agent-x", reader.GetString(4));      // agent_id
        Assert.Equal("cli", reader.GetString(5));           // source
    }
}
