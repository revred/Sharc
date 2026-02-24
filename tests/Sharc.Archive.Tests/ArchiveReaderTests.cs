// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive;
using Xunit;

namespace Sharc.Archive.Tests;

public class ArchiveReaderTests : IDisposable
{
    private readonly string _arcPath;
    private readonly SharcDatabase _db;

    public ArchiveReaderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_reader_{Guid.NewGuid()}.arc");
        _db = ArchiveSchemaBuilder.CreateSchema(_arcPath);
        SeedData();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    private void SeedData()
    {
        using var writer = new ArchiveWriter(_db);

        // Two conversations
        writer.WriteConversation(new ConversationRecord("conv-1", "First Chat", 1000, 2000, "agent-a", "cli", null));
        writer.WriteConversation(new ConversationRecord("conv-2", "Second Chat", 3000, null, "agent-b", "web",
            new Dictionary<string, object?> { ["model"] = "claude-4" }));

        // Turns for conv-1
        writer.WriteTurns(new[]
        {
            new TurnRecord("conv-1", 0, "user", "Hello", 1000, null, null),
            new TurnRecord("conv-1", 1, "assistant", "Hi there!", 1001, 42, null),
            new TurnRecord("conv-1", 2, "user", "Bye", 1002, null, null)
        });

        // Turn for conv-2
        writer.WriteTurns(new[]
        {
            new TurnRecord("conv-2", 0, "user", "Question?", 3000, 10, null)
        });

        // Annotations
        writer.WriteAnnotation(new AnnotationRecord("turn", 1, "quality", "accept", "Good", "reviewer-1", 5000, null));
        writer.WriteAnnotation(new AnnotationRecord("turn", 2, "quality", "reject", "Bad", "reviewer-1", 5001, null));

        // File annotation
        writer.WriteFileAnnotation(new FileAnnotationRecord(1, "src/main.cs", "correctness", "Bug", 10, 20, 6000, null));

        // Decisions
        writer.WriteDecision(new DecisionRecord("conv-1", 2, "D-001", "Use Sharc", "Dogfooding", "accepted", 7000, null));

        // Checkpoint
        writer.WriteCheckpoint(new CheckpointRecord("cp-1", "milestone-1", 8000, 2, 4, 2, 0, null));
    }

    // ── ReadConversations ─────────────────────────────────────────────

    [Fact]
    public void ReadConversations_All_ReturnsBoth()
    {
        var reader = new ArchiveReader(_db);
        var convs = reader.ReadConversations();

        Assert.Equal(2, convs.Count);
    }

    [Fact]
    public void ReadConversations_FilterById_ReturnsMatchingOnly()
    {
        var reader = new ArchiveReader(_db);
        var convs = reader.ReadConversations("conv-2");

        Assert.Single(convs);
        Assert.Equal("conv-2", convs[0].ConversationId);
        Assert.Equal("Second Chat", convs[0].Title);
        Assert.Null(convs[0].EndedAt);
    }

    [Fact]
    public void ReadConversations_WithMetadata_DecodesBlob()
    {
        var reader = new ArchiveReader(_db);
        var convs = reader.ReadConversations("conv-2");

        Assert.Single(convs);
        Assert.NotNull(convs[0].Metadata);
        Assert.Equal("claude-4", convs[0].Metadata!["model"]);
    }

    // ── ReadTurns ─────────────────────────────────────────────────────

    [Fact]
    public void ReadTurns_ForConversation_ReturnsMatchingOnly()
    {
        var reader = new ArchiveReader(_db);
        var turns = reader.ReadTurns("conv-1");

        Assert.Equal(3, turns.Count);
        Assert.Equal("Hello", turns[0].Content);
        Assert.Equal("Hi there!", turns[1].Content);
        Assert.Equal(42, turns[1].TokenCount);
    }

    [Fact]
    public void ReadTurns_All_ReturnsFour()
    {
        var reader = new ArchiveReader(_db);
        var turns = reader.ReadTurns();

        Assert.Equal(4, turns.Count);
    }

    // ── ReadAnnotations ───────────────────────────────────────────────

    [Fact]
    public void ReadAnnotations_All_ReturnsTwo()
    {
        var reader = new ArchiveReader(_db);
        var anns = reader.ReadAnnotations();

        Assert.Equal(2, anns.Count);
        Assert.Equal("accept", anns[0].Verdict);
        Assert.Equal("reject", anns[1].Verdict);
    }

    // ── ReadFileAnnotations ───────────────────────────────────────────

    [Fact]
    public void ReadFileAnnotations_All_ReturnsOne()
    {
        var reader = new ArchiveReader(_db);
        var fas = reader.ReadFileAnnotations();

        Assert.Single(fas);
        Assert.Equal("src/main.cs", fas[0].FilePath);
        Assert.Equal(10, fas[0].LineStart);
        Assert.Equal(20, fas[0].LineEnd);
    }

    // ── ReadDecisions ─────────────────────────────────────────────────

    [Fact]
    public void ReadDecisions_ForConversation_ReturnsMatching()
    {
        var reader = new ArchiveReader(_db);
        var decs = reader.ReadDecisions("conv-1");

        Assert.Single(decs);
        Assert.Equal("D-001", decs[0].DecisionId);
        Assert.Equal("Use Sharc", decs[0].Title);
    }

    // ── ReadCheckpoints ───────────────────────────────────────────────

    [Fact]
    public void ReadCheckpoints_All_ReturnsOne()
    {
        var reader = new ArchiveReader(_db);
        var cps = reader.ReadCheckpoints();

        Assert.Single(cps);
        Assert.Equal("cp-1", cps[0].CheckpointId);
        Assert.Equal("milestone-1", cps[0].Label);
        Assert.Equal(2, cps[0].ConversationCount);
        Assert.Equal(4, cps[0].TurnCount);
    }

    // ── Empty archive ─────────────────────────────────────────────────

    [Fact]
    public void ReadAll_EmptyArchive_ReturnsEmptyLists()
    {
        var emptyPath = Path.Combine(Path.GetTempPath(), $"sharc_empty_{Guid.NewGuid()}.arc");
        try
        {
            using var emptyDb = ArchiveSchemaBuilder.CreateSchema(emptyPath);
            var reader = new ArchiveReader(emptyDb);

            Assert.Empty(reader.ReadConversations());
            Assert.Empty(reader.ReadTurns());
            Assert.Empty(reader.ReadAnnotations());
            Assert.Empty(reader.ReadFileAnnotations());
            Assert.Empty(reader.ReadDecisions());
            Assert.Empty(reader.ReadCheckpoints());
        }
        finally
        {
            try { File.Delete(emptyPath); } catch { }
            try { File.Delete(emptyPath + ".journal"); } catch { }
        }
    }
}
