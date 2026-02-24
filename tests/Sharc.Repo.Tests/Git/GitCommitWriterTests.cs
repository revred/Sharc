// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Git;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Git;

public sealed class GitCommitWriterTests : IDisposable
{
    private readonly string _arcPath;

    public GitCommitWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_gcw_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WriteCommits_EmptyList_WritesNothing()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        gcw.WriteCommits(Array.Empty<GitCommitRecord>());

        var reader = new WorkspaceReader(db);
        Assert.Equal(0, reader.CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_SingleCommit_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        var commit = new GitCommitRecord("abc123", "Alice", "alice@test.com", 1708790400L, "Initial commit");
        gcw.WriteCommits(new[] { commit });

        var reader = new WorkspaceReader(db);
        Assert.Equal(1, reader.CountRows("commits"));
    }

    [Fact]
    public void WriteCommits_MultipleCommits_WritesAll()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        var commits = new[]
        {
            new GitCommitRecord("abc123", "Alice", "alice@test.com", 1708790400L, "First"),
            new GitCommitRecord("def456", "Bob", "bob@test.com", 1708876800L, "Second"),
            new GitCommitRecord("ghi789", "Carol", "carol@test.com", 1708963200L, "Third"),
        };
        gcw.WriteCommits(commits);

        var reader = new WorkspaceReader(db);
        Assert.Equal(3, reader.CountRows("commits"));
    }

    [Fact]
    public void WriteCommitWithFileChanges_Roundtrips()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        var commit = new GitCommitRecord("abc123", "Alice", "alice@test.com", 1708790400L, "Add files");
        gcw.WriteCommits(new[] { commit });

        var changes = new[]
        {
            new ParsedFileChange("src/A.cs", 10, 5),
            new ParsedFileChange("src/B.cs", 3, 0),
        };
        long commitRowId = 1; // first commit gets rowid 1
        gcw.WriteFileChanges(commitRowId, changes);

        var reader = new WorkspaceReader(db);
        Assert.Equal(2, reader.CountRows("file_changes"));
    }

    [Fact]
    public void WriteCommits_DuplicateSha_SkipsDuplicate()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        var commit = new GitCommitRecord("abc123", "Alice", "alice@test.com", 1708790400L, "First");
        gcw.WriteCommits(new[] { commit });

        // Write same SHA again
        gcw.WriteCommits(new[] { commit });

        var reader = new WorkspaceReader(db);
        Assert.Equal(1, reader.CountRows("commits"));
    }

    [Fact]
    public void GetExistingShas_ReturnsAllWrittenShas()
    {
        using var db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        using var gcw = new GitCommitWriter(db);

        var commits = new[]
        {
            new GitCommitRecord("abc123", "Alice", "alice@test.com", 1708790400L, "First"),
            new GitCommitRecord("def456", "Bob", "bob@test.com", 1708876800L, "Second"),
        };
        gcw.WriteCommits(commits);

        var shas = gcw.GetExistingShas();
        Assert.Contains("abc123", shas);
        Assert.Contains("def456", shas);
        Assert.Equal(2, shas.Count);
    }
}
