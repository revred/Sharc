// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Index.Tests;

public sealed class CommitWriterTests : IDisposable
{
    private readonly string _dbPath;

    public CommitWriterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_writer_test_{Guid.NewGuid():N}.db");
        GcdSchemaBuilder.CreateSchema(_dbPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void WriteCommits_SingleCommit_InsertsRow()
    {
        using var writer = new CommitWriter(_dbPath);
        writer.WriteCommits([
            new CommitRecord("abc123", "John", "john@example.com", "2026-01-01T00:00:00", "Initial commit")
        ]);

        Assert.Equal(1, CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_MultipleCommits_InsertsAll()
    {
        using var writer = new CommitWriter(_dbPath);
        var commits = Enumerable.Range(1, 5)
            .Select(i => new CommitRecord($"sha{i}", "Author", "a@b.com", "2026-01-01", $"Commit {i}"))
            .ToList();
        writer.WriteCommits(commits);

        Assert.Equal(5, CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_DuplicateSha_SkipsWithoutError()
    {
        using var writer = new CommitWriter(_dbPath);
        var commit = new CommitRecord("abc123", "John", "john@example.com", "2026-01-01", "Commit");
        writer.WriteCommits([commit]);
        writer.WriteCommits([commit]); // duplicate

        Assert.Equal(1, CountRows("commits"));
    }

    [Fact]
    public void WriteFileChanges_SingleChange_InsertsRow()
    {
        using var writer = new CommitWriter(_dbPath);
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
        using var writer = new CommitWriter(_dbPath);
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
        using var writer = new CommitWriter(_dbPath);
        writer.WriteCommits([]); // should not throw

        Assert.Equal(0, CountRows("commits"));
    }

    [Fact]
    public void WriteFileChanges_UpdatesFilesTable()
    {
        using var writer = new CommitWriter(_dbPath);
        writer.WriteCommits([
            new CommitRecord("sha1", "John", "john@example.com", "2026-01-01", "First"),
            new CommitRecord("sha2", "John", "john@example.com", "2026-01-02", "Second")
        ]);
        writer.WriteFileChanges([new FileChangeRecord("sha1", "src/file.cs", 10, 0)]);
        writer.WriteFileChanges([new FileChangeRecord("sha2", "src/file.cs", 5, 2)]);

        // files table should have 1 row (upserted), with last_modified_sha = sha2
        Assert.Equal(1, CountRows("files"));
        var lastSha = QueryScalar("SELECT last_modified_sha FROM files WHERE path = 'src/file.cs'");
        Assert.Equal("sha2", lastSha);
    }

    private int CountRows(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? QueryScalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }
}
