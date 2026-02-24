// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Data;

public class WorkspaceWriterTests : IDisposable
{
    private readonly string _arcPath;

    public WorkspaceWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_wsw_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WriteNote_ReturnsRowId()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long id = writer.WriteNote(new NoteRecord("test note", "todo", "user", now, null));

        Assert.True(id > 0);
    }

    [Fact]
    public void WriteNote_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteNote(new NoteRecord("content here", "bug", "claude", now, null));

        var reader = new WorkspaceReader(db);
        var notes = reader.ReadNotes();
        Assert.Single(notes);
        Assert.Equal("content here", notes[0].Content);
        Assert.Equal("bug", notes[0].Tag);
        Assert.Equal("claude", notes[0].Author);
    }

    [Fact]
    public void WriteDecision_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteDecision(new DecisionRecord(
            "use-spans", "Use Span<T> for IO", "Reduces allocations",
            "accepted", "ram", now, null));

        var reader = new WorkspaceReader(db);
        var decisions = reader.ReadDecisions();
        Assert.Single(decisions);
        Assert.Equal("use-spans", decisions[0].DecisionId);
        Assert.Equal("Use Span<T> for IO", decisions[0].Title);
        Assert.Equal("accepted", decisions[0].Status);
    }

    [Fact]
    public void WriteContext_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteContext(new ContextEntry("project.language", "C# 13", "user", now, now));

        var reader = new WorkspaceReader(db);
        var entries = reader.ReadContext("project.language");
        Assert.Single(entries);
        Assert.Equal("C# 13", entries[0].Value);
    }

    [Fact]
    public void WriteFileAnnotation_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteFileAnnotation(new FileAnnotationRecord(
            "src/Foo.cs", "todo", "Refactor this", 42, 56, "claude", now, null));

        var reader = new WorkspaceReader(db);
        var anns = reader.ReadFileAnnotations("src/Foo.cs");
        Assert.Single(anns);
        Assert.Equal("todo", anns[0].AnnotationType);
        Assert.Equal(42, anns[0].LineStart);
        Assert.Equal(56, anns[0].LineEnd);
    }

    [Fact]
    public void WriteCommit_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        long id = writer.WriteCommit(new GitCommitRecord(
            "abc123", "Alice", "alice@test.com", 1000000, "Initial commit"));

        Assert.True(id > 0);

        var reader = new WorkspaceReader(db);
        int count = reader.CountRows("commits");
        Assert.Equal(1, count);
    }

    [Fact]
    public void WriteMeta_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        writer.WriteMeta("version", "1");

        var reader = new WorkspaceReader(db);
        Assert.Equal("1", reader.GetMeta("version"));
    }
}
