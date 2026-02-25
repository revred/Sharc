// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo;
using Sharc.Repo.Cli;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public class InitCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public InitCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_init_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Run_NewRepo_CreatesSharcDir()
    {
        int exitCode = InitCommand.Run(Array.Empty<string>());

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(Path.Combine(_tempRoot, ".sharc")));
    }

    [Fact]
    public void Run_NewRepo_CreatesWorkspaceArc()
    {
        InitCommand.Run(Array.Empty<string>());

        Assert.True(File.Exists(Path.Combine(_tempRoot, ".sharc", "workspace.arc")));
    }

    [Fact]
    public void Run_NewRepo_CreatesConfigArc()
    {
        InitCommand.Run(Array.Empty<string>());

        Assert.True(File.Exists(Path.Combine(_tempRoot, ".sharc", "config.arc")));
    }

    [Fact]
    public void Run_AlreadyInitialized_ReturnsOne()
    {
        InitCommand.Run(Array.Empty<string>());

        int exitCode = InitCommand.Run(Array.Empty<string>());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_NoGitRepo_ReturnsOne()
    {
        var noGitDir = Path.Combine(Path.GetTempPath(), $"sharc_nogit_{Guid.NewGuid()}");
        Directory.CreateDirectory(noGitDir);
        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noGitDir);

        try
        {
            int exitCode = InitCommand.Run(Array.Empty<string>());
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noGitDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_Default_AddsToGitignore()
    {
        InitCommand.Run(Array.Empty<string>());

        var gitignorePath = Path.Combine(_tempRoot, ".gitignore");
        Assert.True(File.Exists(gitignorePath));
        var content = File.ReadAllText(gitignorePath);
        Assert.Contains(".sharc/", content);
    }

    [Fact]
    public void Run_WithTrack_DoesNotModifyGitignore()
    {
        InitCommand.Run(new[] { "--track" });

        var gitignorePath = Path.Combine(_tempRoot, ".gitignore");
        // .gitignore may not exist, or if it does, should not contain .sharc/
        if (File.Exists(gitignorePath))
        {
            var content = File.ReadAllText(gitignorePath);
            Assert.DoesNotContain(".sharc/", content);
        }
    }

    [Fact]
    public void Run_WorkspaceHasMetadata()
    {
        InitCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", "workspace.arc");
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new Sharc.Repo.Data.WorkspaceReader(db);

        Assert.Equal("1", reader.GetMeta("version"));
        Assert.NotNull(reader.GetMeta("created_at"));
        Assert.Equal(_tempRoot, reader.GetMeta("git_root"));
    }
}
