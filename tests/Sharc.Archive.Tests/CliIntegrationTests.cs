// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive;
using Xunit;

namespace Sharc.Archive.Tests;

public class CliIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
            try { File.Delete(f + ".journal"); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath(string ext = ".arc")
    {
        var p = Path.Combine(Path.GetTempPath(), $"sharc_cli_{Guid.NewGuid()}{ext}");
        _tempFiles.Add(p);
        return p;
    }

    // ── Help ─────────────────────────────────────────────────────────

    [Fact]
    public void Main_NoArgs_ReturnsZero()
    {
        int exitCode = Program.Main(Array.Empty<string>());

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_Help_ReturnsZero()
    {
        int exitCode = Program.Main(new[] { "--help" });

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_UnknownCommand_ReturnsOne()
    {
        int exitCode = Program.Main(new[] { "bogus" });

        Assert.Equal(1, exitCode);
    }

    // ── Capture ──────────────────────────────────────────────────────

    [Fact]
    public void Capture_ValidJsonl_CreatesArchive()
    {
        var inputPath = TempPath(".jsonl");
        var outputPath = TempPath(".arc");

        File.WriteAllLines(inputPath, new[]
        {
            """{"conversation_id": "test-conv", "title": "Test"}""",
            """{"role": "user", "content": "Hello"}""",
            """{"role": "assistant", "content": "Hi there!", "token_count": 10}"""
        });

        int exitCode = Program.Main(new[] { "capture", "--input", inputPath, "--output", outputPath });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));

        // Verify data was written
        using var db = SharcDatabase.Open(outputPath, new SharcOpenOptions { Writable = false });
        var reader = new ArchiveReader(db);
        var convs = reader.ReadConversations();
        Assert.Single(convs);
        Assert.Equal("test-conv", convs[0].ConversationId);

        var turns = reader.ReadTurns();
        Assert.Equal(2, turns.Count);
    }

    [Fact]
    public void Capture_MissingArgs_ReturnsOne()
    {
        int exitCode = Program.Main(new[] { "capture" });

        Assert.Equal(1, exitCode);
    }

    // ── Annotate ─────────────────────────────────────────────────────

    [Fact]
    public void Annotate_ValidTarget_CreatesAnnotation()
    {
        var archivePath = TempPath(".arc");
        using (var db = ArchiveSchemaBuilder.CreateSchema(archivePath))
        {
            using var writer = new ArchiveWriter(db);
            writer.WriteConversation(new ConversationRecord("c-1", null, 1000, null, null, null, null));
            writer.WriteTurns(new[] { new TurnRecord("c-1", 0, "user", "test", 1000, null, null) });
        }

        int exitCode = Program.Main(new[]
        {
            "annotate", "--archive", archivePath, "--target", "turn:1",
            "--type", "quality", "--verdict", "accept", "--annotator", "rev-1"
        });

        Assert.Equal(0, exitCode);

        using var db2 = SharcDatabase.Open(archivePath, new SharcOpenOptions { Writable = false });
        var reader = new ArchiveReader(db2);
        var anns = reader.ReadAnnotations();
        Assert.Single(anns);
        Assert.Equal("accept", anns[0].Verdict);
    }

    // ── Review ───────────────────────────────────────────────────────

    [Fact]
    public void Review_ExistingArchive_ReturnsZero()
    {
        var archivePath = TempPath(".arc");
        using (var db = ArchiveSchemaBuilder.CreateSchema(archivePath))
        {
            using var writer = new ArchiveWriter(db);
            writer.WriteConversation(new ConversationRecord("c-1", "Chat", 1000, null, null, null, null));
        }

        int exitCode = Program.Main(new[] { "review", "--archive", archivePath });

        Assert.Equal(0, exitCode);
    }

    // ── Checkpoint ───────────────────────────────────────────────────

    [Fact]
    public void Checkpoint_ExistingArchive_CreatesCheckpoint()
    {
        var archivePath = TempPath(".arc");
        using (var db = ArchiveSchemaBuilder.CreateSchema(archivePath))
        {
            using var writer = new ArchiveWriter(db);
            writer.WriteConversation(new ConversationRecord("c-1", null, 1000, null, null, null, null));
            writer.WriteTurns(new[] { new TurnRecord("c-1", 0, "user", "test", 1000, null, null) });
        }

        int exitCode = Program.Main(new[] { "checkpoint", "--archive", archivePath, "--label", "milestone-1" });

        Assert.Equal(0, exitCode);

        using var db2 = SharcDatabase.Open(archivePath, new SharcOpenOptions { Writable = false });
        var reader = new ArchiveReader(db2);
        var cps = reader.ReadCheckpoints();
        Assert.Single(cps);
        Assert.Equal("milestone-1", cps[0].Label);
        Assert.Equal(1, cps[0].ConversationCount);
        Assert.Equal(1, cps[0].TurnCount);
    }

    // ── InputParser ──────────────────────────────────────────────────

    [Fact]
    public void ParseJsonLines_WithMetadata_ExtractsConversationId()
    {
        var lines = new[]
        {
            """{"conversation_id": "abc", "title": "Test Chat"}""",
            """{"role": "user", "content": "Hello"}""",
            """{"role": "assistant", "content": "World"}"""
        };

        var (conv, turns) = InputParser.ParseJsonLines(lines, "agent-x", "cli");

        Assert.Equal("abc", conv.ConversationId);
        Assert.Equal("Test Chat", conv.Title);
        Assert.Equal("agent-x", conv.AgentId);
        Assert.Equal(2, turns.Count);
        Assert.Equal("user", turns[0].Role);
        Assert.Equal("Hello", turns[0].Content);
    }

    [Fact]
    public void ParseJsonLines_NoMetadata_GeneratesConversationId()
    {
        var lines = new[]
        {
            """{"role": "user", "content": "Just a turn"}"""
        };

        var (conv, turns) = InputParser.ParseJsonLines(lines);

        Assert.NotEmpty(conv.ConversationId);
        Assert.Single(turns);
    }
}
