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

    // ── Knowledge Graph tables ────────────────────────────────────────

    [Fact]
    public void CreateSchema_FeaturesTable_Exists()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("features");
        Assert.Equal(7, table.Columns.Count);
        Assert.Equal("name", table.Columns[1].Name);
        Assert.Equal("layer", table.Columns[3].Name);
    }

    [Fact]
    public void CreateSchema_FeatureEdgesTable_Exists()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var table = db.Schema.GetTable("feature_edges");
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal("feature_name", table.Columns[1].Name);
        Assert.Equal("target_kind", table.Columns[3].Name);
    }

    // ── Knowledge Graph indexes ──────────────────────────────────────

    [Fact]
    public void CreateSchema_HasKnowledgeGraphIndexes()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var indexNames = db.Schema.Indexes.Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("idx_features_name", indexNames);
        Assert.Contains("idx_file_purposes_path", indexNames);
        Assert.Contains("idx_feature_edges_feature", indexNames);
        Assert.Contains("idx_feature_edges_target", indexNames);
        Assert.Contains("idx_file_deps_source", indexNames);
        Assert.Contains("idx_file_deps_target", indexNames);
        Assert.Contains("idx_file_deps_kind", indexNames);
    }

    [Fact]
    public void CreateSchema_FeaturesIndex_IsUnique()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);

        var idx = db.Schema.Indexes.First(i => i.Name == "idx_features_name");
        Assert.True(idx.IsUnique);
        Assert.Equal("features", idx.TableName);
    }

    [Fact]
    public void CreateSchema_ExistingDatabase_IndexesAreIdempotent()
    {
        using var db1 = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        int indexCount1 = db1.Schema.Indexes.Count;
        db1.Dispose();

        using var db2 = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        int indexCount2 = db2.Schema.Indexes.Count;

        Assert.Equal(indexCount1, indexCount2);
    }
}
