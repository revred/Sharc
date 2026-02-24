// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Schema;

public class WorkspaceSchemaBuilderTests : IDisposable
{
    private readonly string _arcPath;

    public WorkspaceSchemaBuilderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_ws_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateSchema_NewDatabase_CreatesAllEightTables()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var tableNames = db.Schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("commits", tableNames);
        Assert.Contains("file_changes", tableNames);
        Assert.Contains("notes", tableNames);
        Assert.Contains("file_annotations", tableNames);
        Assert.Contains("decisions", tableNames);
        Assert.Contains("context", tableNames);
        Assert.Contains("conversations", tableNames);
        Assert.Contains("_workspace_meta", tableNames);
    }

    [Fact]
    public void CreateSchema_ExistingDatabase_IsIdempotent()
    {
        using var db1 = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        int count1 = db1.Schema.Tables.Count;
        db1.Dispose();

        using var db2 = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        int count2 = db2.Schema.Tables.Count;

        Assert.Equal(count1, count2);
    }

    [Fact]
    public void CreateSchema_CommitsTable_HasCorrectColumns()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("commits");
        Assert.Equal(6, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal("sha", table.Columns[1].Name);
        Assert.Equal("message", table.Columns[5].Name);
    }

    [Fact]
    public void CreateSchema_NotesTable_HasCorrectColumns()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("notes");
        Assert.Equal(6, table.Columns.Count);
        Assert.Equal("content", table.Columns[1].Name);
        Assert.Equal("tag", table.Columns[2].Name);
        Assert.Equal("metadata", table.Columns[5].Name);
    }

    [Fact]
    public void CreateSchema_ContextTable_HasCorrectColumns()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("context");
        Assert.Equal(6, table.Columns.Count);
        Assert.Equal("key", table.Columns[1].Name);
        Assert.Equal("value", table.Columns[2].Name);
        Assert.Equal("updated_at", table.Columns[5].Name);
    }

    [Fact]
    public void CreateSchema_DecisionsTable_HasCorrectColumns()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("decisions");
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal("decision_id", table.Columns[1].Name);
        Assert.Equal("status", table.Columns[4].Name);
        Assert.Equal("metadata", table.Columns[7].Name);
    }
}
