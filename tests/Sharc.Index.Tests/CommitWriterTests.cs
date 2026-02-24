// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Xunit;

namespace Sharc.Index.Tests;

public sealed class CommitWriterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharcDatabase _db;

    public CommitWriterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_writer_test_{Guid.NewGuid():N}.arc");
        _db = GcdSchemaBuilder.CreateSchema(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + ".journal"); } catch { }
    }

    [Fact]
    public void WriteCommits_SingleCommit_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01T00:00:00", "Initial commit")
        ]);

        Assert.Equal(1, CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_MultipleCommits_InsertsAll()
    {
        using var writer = new CommitWriter(_db);
        var commits = Enumerable.Range(1, 5)
            .Select(i => new CommitRecord($"sha{i}", "Author", "a@b.com", "2026-01-01", $"Commit {i}"))
            .ToList();
        writer.WriteCommits(commits);

        Assert.Equal(5, CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_DuplicateSha_SkipsWithoutError()
    {
        using var writer = new CommitWriter(_db);
        // Use different author names to ensure dedup is by SHA, not author name
        writer.WriteCommits([new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "First")]);
        writer.WriteCommits([new CommitRecord("abc123", "Jane", "jane@example.com", "2026-01-02", "Second")]);

        Assert.Equal(1, CountRows("commits"));
        // Verify the first write's data was kept
        using var reader = _db.CreateReader("commits");
        Assert.True(reader.Read());
        Assert.Equal("abc123", reader.GetString(0)); // sha
        Assert.Equal("John", reader.GetString(1));    // first author, not second
    }

    [Fact]
    public void WriteFileChanges_SingleChange_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "Commit")
        ]);
        writer.WriteFileChanges([
            new FileChangeRecord("abc123", "src/file.cs", 10, 5)
        ]);

        Assert.Equal(1, CountRows("file_changes"));
    }

    [Fact]
    public void WriteFileChanges_MultipleChanges_InsertsAll()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "Commit")
        ]);
        writer.WriteFileChanges([
            new FileChangeRecord("abc123", "src/a.cs", 10, 5),
            new FileChangeRecord("abc123", "src/b.cs", 20, 3),
            new FileChangeRecord("abc123", "src/c.cs", 1, 0)
        ]);

        Assert.Equal(3, CountRows("file_changes"));
    }

    [Fact]
    public void WriteCommits_EmptyList_NoOp()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([]); // should not throw

        Assert.Equal(0, CountRows("commits"));
    }

    [Fact]
    public void WriteFileChanges_UpdatesFilesTable()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("sha1", "John", "john@example.com", "2026-01-01", "First"),
            new CommitRecord("sha2", "John", "john@example.com", "2026-01-02", "Second")
        ]);
        writer.WriteFileChanges([new FileChangeRecord("sha1", "src/file.cs", 10, 0)]);
        writer.WriteFileChanges([new FileChangeRecord("sha2", "src/file.cs", 5, 2)]);

        // files table should have 1 row (upserted)
        Assert.Equal(1, CountRows("files"));

        // Full scan — ordinal 0=path, 1=first_seen_sha, 2=last_modified_sha
        using var reader = _db.CreateReader("files");
        Assert.True(reader.Read());
        Assert.Equal("src/file.cs", reader.GetString(0));
        Assert.Equal("sha1", reader.GetString(1)); // first_seen_sha preserved
        Assert.Equal("sha2", reader.GetString(2)); // last_modified_sha updated
    }

    [Fact]
    public void WriteCommits_VerifiesColumnData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("abc123", "John Doe", "john@example.com", "2026-01-15", "Fix bug")
        ]);

        // Full scan — ordinals are 0-based for non-id columns in B-tree record
        using var reader = _db.CreateReader("commits");
        Assert.True(reader.Read());
        Assert.Equal("abc123", reader.GetString(0));
        Assert.Equal("John Doe", reader.GetString(1));
        Assert.Equal("john@example.com", reader.GetString(2));
        Assert.Equal("2026-01-15", reader.GetString(3));
        Assert.Equal("Fix bug", reader.GetString(4));
    }

    [Fact]
    public void WriteFileChanges_VerifiesChangeData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "Commit")
        ]);
        writer.WriteFileChanges([
            new FileChangeRecord("abc123", "src/file.cs", 42, 7)
        ]);

        // Full scan — ordinal 0=commit_sha, 1=path, 2=lines_added, 3=lines_deleted
        using var reader = _db.CreateReader("file_changes");
        Assert.True(reader.Read());
        Assert.Equal("abc123", reader.GetString(0));
        Assert.Equal("src/file.cs", reader.GetString(1));
        Assert.Equal(42, reader.GetInt64(2));
        Assert.Equal(7, reader.GetInt64(3));
    }

    // ── E-1: Authors table ──────────────────────────────────────────

    [Fact]
    public void WriteAuthors_SingleAuthor_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteAuthors([
            new AuthorRecord("John Doe", "john@example.com", "abc123", 1)
        ]);

        Assert.Equal(1, CountRows("authors"));
    }

    [Fact]
    public void WriteAuthors_VerifiesColumnData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteAuthors([
            new AuthorRecord("Jane Doe", "jane@example.com", "sha42", 17)
        ]);

        using var reader = _db.CreateReader("authors");
        Assert.True(reader.Read());
        Assert.Equal("Jane Doe", reader.GetString(0));
        Assert.Equal("jane@example.com", reader.GetString(1));
        Assert.Equal("sha42", reader.GetString(2));
        Assert.Equal(17, reader.GetInt64(3));
    }

    [Fact]
    public void WriteAuthors_DuplicateEmail_UpsertsCommitCount()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteAuthors([new AuthorRecord("John", "john@example.com", "sha1", 5)]);
        writer.WriteAuthors([new AuthorRecord("John", "john@example.com", "sha1", 10)]);

        Assert.Equal(1, CountRows("authors"));
        using var reader = _db.CreateReader("authors");
        Assert.True(reader.Read());
        Assert.Equal(10, reader.GetInt64(3)); // updated commit_count
    }

    // ── E-1: Commit parents table ────────────────────────────────────

    [Fact]
    public void WriteCommitParents_SingleParent_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommitParents([
            new CommitParentRecord("abc123", "def456", 0)
        ]);

        Assert.Equal(1, CountRows("commit_parents"));
    }

    [Fact]
    public void WriteCommitParents_MergeCommit_InsertsBothParents()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommitParents([
            new CommitParentRecord("merge_sha", "parent1", 0),
            new CommitParentRecord("merge_sha", "parent2", 1)
        ]);

        Assert.Equal(2, CountRows("commit_parents"));
    }

    [Fact]
    public void WriteCommitParents_VerifiesColumnData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteCommitParents([
            new CommitParentRecord("abc123", "def456", 0)
        ]);

        using var reader = _db.CreateReader("commit_parents");
        Assert.True(reader.Read());
        Assert.Equal("abc123", reader.GetString(0));
        Assert.Equal("def456", reader.GetString(1));
        Assert.Equal(0, reader.GetInt64(2));
    }

    // ── E-1: Branches table ──────────────────────────────────────────

    [Fact]
    public void WriteBranches_SingleBranch_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteBranches([
            new BranchRecord("main", "abc123", 0, 1706745600)
        ]);

        Assert.Equal(1, CountRows("branches"));
    }

    [Fact]
    public void WriteBranches_VerifiesColumnData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteBranches([
            new BranchRecord("feature/foo", "sha42", 1, 1706745600)
        ]);

        using var reader = _db.CreateReader("branches");
        Assert.True(reader.Read());
        Assert.Equal("feature/foo", reader.GetString(0));
        Assert.Equal("sha42", reader.GetString(1));
        Assert.Equal(1, reader.GetInt64(2));          // is_remote
        Assert.Equal(1706745600, reader.GetInt64(3));  // updated_at
    }

    [Fact]
    public void WriteBranches_DuplicateName_Upserts()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteBranches([new BranchRecord("main", "sha1", 0, 1000)]);
        writer.WriteBranches([new BranchRecord("main", "sha2", 0, 2000)]);

        Assert.Equal(1, CountRows("branches"));
        using var reader = _db.CreateReader("branches");
        Assert.True(reader.Read());
        Assert.Equal("sha2", reader.GetString(1)); // updated head_sha
    }

    // ── E-1: Tags table ─────────────────────────────────────────────

    [Fact]
    public void WriteTags_SingleTag_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteTags([
            new TagRecord("v1.0", "abc123", "John", "john@example.com", "Release 1.0", 1706745600)
        ]);

        Assert.Equal(1, CountRows("tags"));
    }

    [Fact]
    public void WriteTags_VerifiesColumnData()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteTags([
            new TagRecord("v2.0", "sha42", "Jane", "jane@example.com", "Major release", 1706832000)
        ]);

        using var reader = _db.CreateReader("tags");
        Assert.True(reader.Read());
        Assert.Equal("v2.0", reader.GetString(0));
        Assert.Equal("sha42", reader.GetString(1));
        Assert.Equal("Jane", reader.GetString(2));
        Assert.Equal("jane@example.com", reader.GetString(3));
        Assert.Equal("Major release", reader.GetString(4));
        Assert.Equal(1706832000, reader.GetInt64(5));
    }

    [Fact]
    public void WriteTags_LightweightTag_NullableTaggerFields()
    {
        using var writer = new CommitWriter(_db);
        writer.WriteTags([
            new TagRecord("v0.1", "sha1", null, null, null, 1706745600)
        ]);

        Assert.Equal(1, CountRows("tags"));
        using var reader = _db.CreateReader("tags");
        Assert.True(reader.Read());
        Assert.Equal("v0.1", reader.GetString(0));
        Assert.True(reader.IsNull(2)); // tagger_name
        Assert.True(reader.IsNull(3)); // tagger_email
        Assert.True(reader.IsNull(4)); // message
    }

    // ── E-1/E-3: _index_state table ─────────────────────────────────

    [Fact]
    public void SetIndexState_NewKey_InsertsRow()
    {
        using var writer = new CommitWriter(_db);
        writer.SetIndexState("last_indexed_sha", "abc123");

        Assert.Equal(1, CountRows("_index_state"));
    }

    [Fact]
    public void GetIndexState_ExistingKey_ReturnsValue()
    {
        using var writer = new CommitWriter(_db);
        writer.SetIndexState("last_indexed_sha", "abc123");

        var value = writer.GetIndexState("last_indexed_sha");
        Assert.Equal("abc123", value);
    }

    [Fact]
    public void GetIndexState_MissingKey_ReturnsNull()
    {
        using var writer = new CommitWriter(_db);
        var value = writer.GetIndexState("nonexistent");
        Assert.Null(value);
    }

    [Fact]
    public void SetIndexState_ExistingKey_UpdatesValue()
    {
        using var writer = new CommitWriter(_db);
        writer.SetIndexState("last_indexed_sha", "sha1");
        writer.SetIndexState("last_indexed_sha", "sha2");

        Assert.Equal(1, CountRows("_index_state"));
        Assert.Equal("sha2", writer.GetIndexState("last_indexed_sha"));
    }

    private int CountRows(string table)
    {
        int count = 0;
        using var reader = _db.CreateReader(table);
        while (reader.Read())
            count++;
        return count;
    }
}
