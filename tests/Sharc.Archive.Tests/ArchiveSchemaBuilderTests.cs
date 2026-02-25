// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive;
using Xunit;

namespace Sharc.Archive.Tests;

public class ArchiveSchemaBuilderTests : IDisposable
{
    private readonly string _arcPath;

    public ArchiveSchemaBuilderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_schema_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateSchema_NewDatabase_CreatesAllSevenTables()
    {
        using var db = ArchiveSchemaBuilder.CreateSchema(_arcPath);

        var tables = db.Schema.Tables;
        var tableNames = tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("conversations", tableNames);
        Assert.Contains("turns", tableNames);
        Assert.Contains("annotations", tableNames);
        Assert.Contains("file_annotations", tableNames);
        Assert.Contains("decisions", tableNames);
        Assert.Contains("checkpoints", tableNames);
        Assert.Contains("_sharc_manifest", tableNames);
    }

    [Fact]
    public void CreateSchema_ExistingDatabase_IsIdempotent()
    {
        using var db1 = ArchiveSchemaBuilder.CreateSchema(_arcPath);
        int count1 = db1.Schema.Tables.Count;
        db1.Dispose();

        using var db2 = ArchiveSchemaBuilder.CreateSchema(_arcPath);
        int count2 = db2.Schema.Tables.Count;

        Assert.Equal(count1, count2);
    }

    [Fact]
    public void CreateSchema_ConversationsTable_HasCorrectColumns()
    {
        using var db = ArchiveSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("conversations");
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal("conversation_id", table.Columns[1].Name);
        Assert.Equal("metadata", table.Columns[7].Name);
    }

    [Fact]
    public void CreateSchema_TurnsTable_HasCorrectColumns()
    {
        using var db = ArchiveSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("turns");
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal("conversation_id", table.Columns[1].Name);
        Assert.Equal("role", table.Columns[3].Name);
        Assert.Equal("content", table.Columns[4].Name);
    }

    [Fact]
    public void CreateSchema_ManifestTable_HasCorrectColumns()
    {
        using var db = ArchiveSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("_sharc_manifest");
        Assert.Equal(9, table.Columns.Count);
        Assert.Equal("fragment_id", table.Columns[1].Name);
        Assert.Equal("ledger_sequence", table.Columns[6].Name);
    }
}
