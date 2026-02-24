// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Git;
using Xunit;

namespace Sharc.Repo.Tests.Git;

public sealed class GitLogWalkerTests
{
    // ── ParseCommitLine ──────────────────────────────────────────────

    [Fact]
    public void ParseCommitLine_StandardFormat_ReturnsRecord()
    {
        var result = RepoGitLogWalker.ParseCommitLine(
            "abc123def456|John Doe|john@example.com|1708790400|Fix bug in parser");

        Assert.NotNull(result);
        Assert.Equal("abc123def456", result!.Sha);
        Assert.Equal("John Doe", result!.AuthorName);
        Assert.Equal("john@example.com", result!.AuthorEmail);
        Assert.Equal(1708790400L, result!.AuthoredAt);
        Assert.Equal("Fix bug in parser", result!.Message);
    }

    [Fact]
    public void ParseCommitLine_MessageWithPipes_PreservesFullMessage()
    {
        var result = RepoGitLogWalker.ParseCommitLine(
            "abc123|John|john@example.com|1708790400|Fix A | B | C pipeline");

        Assert.NotNull(result);
        Assert.Equal("Fix A | B | C pipeline", result!.Message);
    }

    [Fact]
    public void ParseCommitLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(RepoGitLogWalker.ParseCommitLine(""));
        Assert.Null(RepoGitLogWalker.ParseCommitLine("   "));
    }

    [Fact]
    public void ParseCommitLine_MalformedLine_ReturnsNull()
    {
        Assert.Null(RepoGitLogWalker.ParseCommitLine("only|three|parts"));
    }

    [Fact]
    public void ParseCommitLine_NonNumericTimestamp_ReturnsNull()
    {
        Assert.Null(RepoGitLogWalker.ParseCommitLine(
            "abc123|John|john@example.com|not-a-number|Fix bug"));
    }

    // ── ParseDiffStatLine ────────────────────────────────────────────

    [Fact]
    public void ParseDiffStatLine_StandardFormat_ReturnsRecord()
    {
        var result = RepoGitLogWalker.ParseDiffStatLine("10\t5\tsrc/Sharc/SharcDatabase.cs");

        Assert.NotNull(result);
        Assert.Equal("src/Sharc/SharcDatabase.cs", result.Value.Path);
        Assert.Equal(10, result.Value.LinesAdded);
        Assert.Equal(5, result.Value.LinesDeleted);
    }

    [Fact]
    public void ParseDiffStatLine_BinaryFile_ReturnsZeros()
    {
        var result = RepoGitLogWalker.ParseDiffStatLine("-\t-\timages/logo.png");

        Assert.NotNull(result);
        Assert.Equal("images/logo.png", result.Value.Path);
        Assert.Equal(0, result.Value.LinesAdded);
        Assert.Equal(0, result.Value.LinesDeleted);
    }

    [Fact]
    public void ParseDiffStatLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(RepoGitLogWalker.ParseDiffStatLine(""));
        Assert.Null(RepoGitLogWalker.ParseDiffStatLine("   "));
    }

    [Fact]
    public void ParseDiffStatLine_Rename_ParsesPath()
    {
        var result = RepoGitLogWalker.ParseDiffStatLine("0\t0\t{old => new}/file.cs");

        Assert.NotNull(result);
        Assert.Equal("{old => new}/file.cs", result.Value.Path);
    }

    // ── ParseCommitBlock ─────────────────────────────────────────────

    [Fact]
    public void ParseGitOutput_MultipleCommits_ParsesAll()
    {
        var output = """
            abc123|Alice|alice@test.com|1708790400|First commit
            def456|Bob|bob@test.com|1708876800|Second commit
            ghi789|Carol|carol@test.com|1708963200|Third commit
            """;

        var commits = RepoGitLogWalker.ParseCommitLines(output);

        Assert.Equal(3, commits.Count);
        Assert.Equal("abc123", commits[0].Sha);
        Assert.Equal("def456", commits[1].Sha);
        Assert.Equal("ghi789", commits[2].Sha);
    }

    [Fact]
    public void ParseDiffStatOutput_MultipleFiles_ParsesAll()
    {
        var output = "10\t5\tsrc/A.cs\n3\t1\tsrc/B.cs\n-\t-\timage.png\n";

        var changes = RepoGitLogWalker.ParseDiffStatLines(output);

        Assert.Equal(3, changes.Count);
        Assert.Equal("src/A.cs", changes[0].Path);
        Assert.Equal("src/B.cs", changes[1].Path);
        Assert.Equal("image.png", changes[2].Path);
    }
}
