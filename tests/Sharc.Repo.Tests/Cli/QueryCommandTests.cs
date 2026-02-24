// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class QueryCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public QueryCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_query_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
        InitCommand.Run(Array.Empty<string>());
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Run_ValidTable_ReturnsZero()
    {
        int exitCode = QueryCommand.Run(new[] { "notes" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_WithLimit_ReturnsZero()
    {
        // Add some notes first
        NoteCommand.Run(new[] { "Note 1" });
        NoteCommand.Run(new[] { "Note 2" });
        NoteCommand.Run(new[] { "Note 3" });

        int exitCode = QueryCommand.Run(new[] { "notes", "--limit", "2" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_NoTable_ReturnsOne()
    {
        int exitCode = QueryCommand.Run(Array.Empty<string>());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_InvalidTable_ReturnsOne()
    {
        int exitCode = QueryCommand.Run(new[] { "nonexistent_table" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_NotInitialized_ReturnsOne()
    {
        var noInitDir = Path.Combine(Path.GetTempPath(), $"sharc_noquery_{Guid.NewGuid()}");
        Directory.CreateDirectory(noInitDir);
        Directory.CreateDirectory(Path.Combine(noInitDir, ".git"));
        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noInitDir);

        try
        {
            int exitCode = QueryCommand.Run(new[] { "notes" });
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noInitDir, recursive: true); } catch { }
        }
    }
}
