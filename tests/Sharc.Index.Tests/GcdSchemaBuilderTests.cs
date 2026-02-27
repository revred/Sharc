// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Xunit;

namespace Sharc.Index.Tests;

public sealed class GcdSchemaBuilderTests : IDisposable
{
    private readonly string _dbPath;

    public GcdSchemaBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_gcd_test_{Guid.NewGuid():N}.arc");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + ".journal"); } catch { }
    }

    [Fact]
    public void CreateSchema_NewFile_CreatesDatabase()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void CreateSchema_CommitsTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var schema = db.Schema;
        var table = schema.GetTable("commits");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("sha", colNames);
        Assert.Contains("author_name", colNames);
        Assert.Contains("author_email", colNames);
        Assert.Contains("authored_date", colNames);
        Assert.Contains("message", colNames);
    }

    [Fact]
    public void CreateSchema_FilesTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var schema = db.Schema;
        var table = schema.GetTable("files");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("path", colNames);
        Assert.Contains("first_seen_sha", colNames);
        Assert.Contains("last_modified_sha", colNames);
    }

    [Fact]
    public void CreateSchema_FileChangesTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var schema = db.Schema;
        var table = schema.GetTable("file_changes");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("commit_sha", colNames);
        Assert.Contains("path", colNames);
        Assert.Contains("lines_added", colNames);
        Assert.Contains("lines_deleted", colNames);
    }

    [Fact]
    public void CreateSchema_CommitsTable_HasIntegerPrimaryKey()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        // In Sharc, INTEGER PRIMARY KEY = rowid alias (id column)
        // Verify we can insert and read back with proper rowid
        using var sw = SharcWriter.From(db);
        var shaBytes = System.Text.Encoding.UTF8.GetBytes("test_sha");
        var nameBytes = System.Text.Encoding.UTF8.GetBytes("Author");
        var emailBytes = System.Text.Encoding.UTF8.GetBytes("a@b.com");
        var dateBytes = System.Text.Encoding.UTF8.GetBytes("2026-01-01");
        var msgBytes = System.Text.Encoding.UTF8.GetBytes("Test");

        sw.Insert("commits",
            ColumnValue.Text(2 * shaBytes.Length + 13, shaBytes),
            ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes),
            ColumnValue.Text(2 * emailBytes.Length + 13, emailBytes),
            ColumnValue.Text(2 * dateBytes.Length + 13, dateBytes),
            ColumnValue.Text(2 * msgBytes.Length + 13, msgBytes));

        // Full scan — ordinal 0 = sha (first non-id column)
        using var reader = db.CreateReader("commits");
        Assert.True(reader.Read());
        Assert.Equal(1, reader.RowId); // First row gets rowid 1
        Assert.Equal("test_sha", reader.GetString(0));
    }

    [Fact]
    public void CreateSchema_AuthorsTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("authors");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("name", colNames);
        Assert.Contains("email", colNames);
        Assert.Contains("first_commit_sha", colNames);
        Assert.Contains("commit_count", colNames);
    }

    [Fact]
    public void CreateSchema_CommitParentsTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("commit_parents");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("commit_sha", colNames);
        Assert.Contains("parent_sha", colNames);
        Assert.Contains("ordinal", colNames);
    }

    [Fact]
    public void CreateSchema_BranchesTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("branches");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("name", colNames);
        Assert.Contains("head_sha", colNames);
        Assert.Contains("is_remote", colNames);
        Assert.Contains("updated_at", colNames);
    }

    [Fact]
    public void CreateSchema_TagsTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("tags");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("name", colNames);
        Assert.Contains("target_sha", colNames);
        Assert.Contains("tagger_name", colNames);
        Assert.Contains("tagger_email", colNames);
        Assert.Contains("message", colNames);
        Assert.Contains("created_at", colNames);
    }

    [Fact]
    public void CreateSchema_DiffHunksTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("diff_hunks");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("commit_sha", colNames);
        Assert.Contains("path", colNames);
        Assert.Contains("old_start", colNames);
        Assert.Contains("old_lines", colNames);
        Assert.Contains("new_start", colNames);
        Assert.Contains("new_lines", colNames);
        Assert.Contains("content", colNames);
    }

    [Fact]
    public void CreateSchema_BlameLinesTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("blame_lines");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("path", colNames);
        Assert.Contains("line_number", colNames);
        Assert.Contains("commit_sha", colNames);
        Assert.Contains("author_name", colNames);
        Assert.Contains("author_email", colNames);
        Assert.Contains("line_content", colNames);
    }

    [Fact]
    public void CreateSchema_IndexStateTableExists_HasExpectedColumns()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var table = db.Schema.GetTable("_index_state");
        Assert.NotNull(table);
        var colNames = table.Columns.Select(c => c.Name).ToList();
        Assert.Contains("key", colNames);
        Assert.Contains("value", colNames);
    }

    [Fact]
    public void CreateSchema_CalledTwice_Idempotent()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);
        db.Dispose();

        // Reopen and verify it doesn't throw
        using var db2 = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        var schema = db2.Schema;
        Assert.NotNull(schema.GetTable("commits"));
        Assert.NotNull(schema.GetTable("files"));
        Assert.NotNull(schema.GetTable("file_changes"));
        Assert.NotNull(schema.GetTable("authors"));
        Assert.NotNull(schema.GetTable("commit_parents"));
        Assert.NotNull(schema.GetTable("branches"));
        Assert.NotNull(schema.GetTable("tags"));
        Assert.NotNull(schema.GetTable("diff_hunks"));
        Assert.NotNull(schema.GetTable("blame_lines"));
        Assert.NotNull(schema.GetTable("_index_state"));
    }

    /// <summary>
    /// Regression test: CreateSchema returns a writable db. Previously, Program.cs
    /// discarded this db and reopened read-only, causing UnauthorizedAccessException.
    /// </summary>
    [Fact]
    public void CreateSchema_ReturnedDb_IsWritableForInserts()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        // The returned db should support writes without reopening
        using var sw = SharcWriter.From(db);
        var shaBytes = System.Text.Encoding.UTF8.GetBytes("sha_test");
        var nameBytes = System.Text.Encoding.UTF8.GetBytes("Author");
        var emailBytes = System.Text.Encoding.UTF8.GetBytes("a@b.com");
        var dateBytes = System.Text.Encoding.UTF8.GetBytes("2026-01-01");
        var msgBytes = System.Text.Encoding.UTF8.GetBytes("Test commit");

        sw.Insert("commits",
            ColumnValue.Text(2 * shaBytes.Length + 13, shaBytes),
            ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes),
            ColumnValue.Text(2 * emailBytes.Length + 13, emailBytes),
            ColumnValue.Text(2 * dateBytes.Length + 13, dateBytes),
            ColumnValue.Text(2 * msgBytes.Length + 13, msgBytes));

        using var reader = db.CreateReader("commits");
        Assert.True(reader.Read());
        Assert.Equal("sha_test", reader.GetString(0));
    }

    /// <summary>
    /// Regression test: writing to commits then file_changes on the same db instance
    /// must work. Previously failed with CorruptPageException when db was reopened
    /// read-only between schema creation and writing.
    /// </summary>
    [Fact]
    public void CreateSchema_WriteCommitsThenFileChanges_BothTablesPopulated()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);
        using var writer = new CommitWriter(db);

        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "Initial commit")
        ]);
        writer.WriteFileChanges([
            new FileChangeRecord("abc123", "src/file.cs", 10, 5)
        ]);

        int commitCount = 0;
        using (var reader = db.CreateReader("commits"))
            while (reader.Read()) commitCount++;

        int changeCount = 0;
        using (var reader = db.CreateReader("file_changes"))
            while (reader.Read()) changeCount++;

        Assert.Equal(1, commitCount);
        Assert.Equal(1, changeCount);
    }

    /// <summary>
    /// Verifies all 10 tables can be written to from the db returned by CreateSchema
    /// (not just commits — the files table had root page issues before the fix).
    /// </summary>
    [Fact]
    public void CreateSchema_AllTablesWritable_FromReturnedDb()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);
        using var writer = new CommitWriter(db);

        // Write to all writable tables
        writer.WriteCommits([new CommitRecord("sha1", "A", "a@b.com", "2026-01-01", "Msg")]);
        writer.WriteFileChanges([new FileChangeRecord("sha1", "f.cs", 1, 0)]);
        writer.WriteAuthors([new AuthorRecord("A", "a@b.com", "sha1", 1)]);
        writer.WriteCommitParents([new CommitParentRecord("sha1", "sha0", 0)]);
        writer.WriteBranches([new BranchRecord("main", "sha1", 0, 1000)]);
        writer.WriteTags([new TagRecord("v1", "sha1", null, null, null, 1000)]);
        writer.SetIndexState("test_key", "test_value");

        // Verify all inserts succeeded
        Assert.True(CountRowsInTable(db, "commits") > 0);
        Assert.True(CountRowsInTable(db, "file_changes") > 0);
        Assert.True(CountRowsInTable(db, "files") > 0);
        Assert.True(CountRowsInTable(db, "authors") > 0);
        Assert.True(CountRowsInTable(db, "commit_parents") > 0);
        Assert.True(CountRowsInTable(db, "branches") > 0);
        Assert.True(CountRowsInTable(db, "tags") > 0);
        Assert.True(CountRowsInTable(db, "_index_state") > 0);
    }

    private static int CountRowsInTable(SharcDatabase db, string tableName)
    {
        int count = 0;
        using var reader = db.CreateReader(tableName);
        while (reader.Read()) count++;
        return count;
    }

    [Fact]
    public void CreateSchema_AllTenTablesCreated()
    {
        using var db = GcdSchemaBuilder.CreateSchema(_dbPath);

        var schema = db.Schema;
        var tables = schema.Tables.Select(t => t.Name).ToList();
        // Original 3
        Assert.Contains("commits", tables);
        Assert.Contains("files", tables);
        Assert.Contains("file_changes", tables);
        // E-1: 7 new tables
        Assert.Contains("authors", tables);
        Assert.Contains("commit_parents", tables);
        Assert.Contains("branches", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("diff_hunks", tables);
        Assert.Contains("blame_lines", tables);
        Assert.Contains("_index_state", tables);
    }
}
