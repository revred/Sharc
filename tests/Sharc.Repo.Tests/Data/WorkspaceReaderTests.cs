// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Data;

public class WorkspaceReaderTests : IDisposable
{
    private readonly string _arcPath;

    public WorkspaceReaderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_wsr_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ReadNotes_FilterByTag_ReturnsMatches()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteNote(new NoteRecord("note 1", "todo", "user", now, null));
        writer.WriteNote(new NoteRecord("note 2", "bug", "user", now, null));
        writer.WriteNote(new NoteRecord("note 3", "todo", "user", now, null));

        var reader = new WorkspaceReader(db);
        var todos = reader.ReadNotes("todo");
        Assert.Equal(2, todos.Count);
    }

    [Fact]
    public void ReadDecisions_FilterByStatus_ReturnsMatches()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteDecision(new DecisionRecord("d1", "Active", null, "accepted", null, now, null));
        writer.WriteDecision(new DecisionRecord("d2", "Old", null, "superseded", null, now, null));

        var reader = new WorkspaceReader(db);
        var accepted = reader.ReadDecisions("accepted");
        Assert.Single(accepted);
        Assert.Equal("d1", accepted[0].DecisionId);
    }

    [Fact]
    public void ReadFileAnnotations_FilterByType_ReturnsMatches()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteFileAnnotation(new FileAnnotationRecord("a.cs", "todo", "fix", null, null, null, now, null));
        writer.WriteFileAnnotation(new FileAnnotationRecord("b.cs", "bug", "crash", null, null, null, now, null));

        var reader = new WorkspaceReader(db);
        var bugs = reader.ReadFileAnnotations(type: "bug");
        Assert.Single(bugs);
        Assert.Equal("b.cs", bugs[0].FilePath);
    }

    [Fact]
    public void ReadContext_NoFilter_ReturnsAll()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var writer = new WorkspaceWriter(db);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.WriteContext(new ContextEntry("k1", "v1", null, now, now));
        writer.WriteContext(new ContextEntry("k2", "v2", null, now, now));

        var reader = new WorkspaceReader(db);
        var entries = reader.ReadContext();
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void CountRows_EmptyTable_ReturnsZero()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var reader = new WorkspaceReader(db);
        Assert.Equal(0, reader.CountRows("notes"));
    }

    [Fact]
    public void GetMeta_MissingKey_ReturnsNull()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var reader = new WorkspaceReader(db);
        Assert.Null(reader.GetMeta("nonexistent"));
    }
}
