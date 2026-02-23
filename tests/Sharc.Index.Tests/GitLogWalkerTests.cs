// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Index.Tests;

public sealed class GitLogWalkerTests
{
    [Fact]
    public void ParseCommitLine_StandardFormat_ReturnsCommitRecord()
    {
        var result = GitLogWalker.ParseCommitLine(
            "abc123def456|John Doe|john@example.com|2026-02-12T10:00:00+00:00|Fix bug in parser");

        Assert.NotNull(result);
        Assert.Equal("abc123def456", result.Sha);
        Assert.Equal("John Doe", result.AuthorName);
        Assert.Equal("john@example.com", result.AuthorEmail);
        Assert.Equal("2026-02-12T10:00:00+00:00", result.AuthoredDate);
        Assert.Equal("Fix bug in parser", result.Message);
    }

    [Fact]
    public void ParseCommitLine_MessageWithPipes_PreservesMessage()
    {
        var result = GitLogWalker.ParseCommitLine(
            "abc123|John|john@example.com|2026-01-01T00:00:00|Fix A | B | C pipeline");

        Assert.NotNull(result);
        Assert.Equal("Fix A | B | C pipeline", result.Message);
    }

    [Fact]
    public void ParseCommitLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(GitLogWalker.ParseCommitLine(""));
        Assert.Null(GitLogWalker.ParseCommitLine("   "));
    }

    [Fact]
    public void ParseCommitLine_MalformedLine_ReturnsNull()
    {
        Assert.Null(GitLogWalker.ParseCommitLine("only|three|parts"));
    }

    [Fact]
    public void ParseDiffStatLine_StandardFormat_ReturnsFileChange()
    {
        var result = GitLogWalker.ParseDiffStatLine("abc123", "10\t5\tsrc/Sharc/SharcDatabase.cs");

        Assert.NotNull(result);
        Assert.Equal("abc123", result.CommitSha);
        Assert.Equal("src/Sharc/SharcDatabase.cs", result.Path);
        Assert.Equal(10, result.LinesAdded);
        Assert.Equal(5, result.LinesDeleted);
    }

    [Fact]
    public void ParseDiffStatLine_BinaryFile_ReturnsZeros()
    {
        var result = GitLogWalker.ParseDiffStatLine("abc123", "-\t-\timages/logo.png");

        Assert.NotNull(result);
        Assert.Equal("images/logo.png", result.Path);
        Assert.Equal(0, result.LinesAdded);
        Assert.Equal(0, result.LinesDeleted);
    }

    [Fact]
    public void ParseDiffStatLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(GitLogWalker.ParseDiffStatLine("abc123", ""));
        Assert.Null(GitLogWalker.ParseDiffStatLine("abc123", "   "));
    }

    [Fact]
    public void ParseDiffStatLine_Rename_ParsesPath()
    {
        var result = GitLogWalker.ParseDiffStatLine("abc123", "0\t0\t{old => new}/file.cs");

        Assert.NotNull(result);
        Assert.Equal("{old => new}/file.cs", result.Path);
    }
}
