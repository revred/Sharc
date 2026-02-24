// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo;
using Xunit;

namespace Sharc.Repo.Tests;

public class RepoLocatorTests : IDisposable
{
    private readonly string _tempRoot;

    public RepoLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_repo_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FindGitRoot_FromSubdirectory_ReturnsRoot()
    {
        var gitDir = Path.Combine(_tempRoot, ".git");
        Directory.CreateDirectory(gitDir);
        var subDir = Path.Combine(_tempRoot, "src", "deep");
        Directory.CreateDirectory(subDir);

        var result = RepoLocator.FindGitRoot(subDir);

        Assert.Equal(_tempRoot, result);
    }

    [Fact]
    public void FindGitRoot_NoGitDir_ReturnsNull()
    {
        var result = RepoLocator.FindGitRoot(_tempRoot);

        Assert.Null(result);
    }

    [Fact]
    public void FindSharcDir_Initialized_ReturnsPath()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        var sharcDir = Path.Combine(_tempRoot, ".sharc");
        Directory.CreateDirectory(sharcDir);

        var result = RepoLocator.FindSharcDir(_tempRoot);

        Assert.Equal(sharcDir, result);
    }

    [Fact]
    public void FindSharcDir_NotInitialized_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));

        var result = RepoLocator.FindSharcDir(_tempRoot);

        Assert.Null(result);
    }

    [Fact]
    public void GetWorkspacePath_NotInitialized_Throws()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));

        Assert.Throws<InvalidOperationException>(
            () => RepoLocator.GetWorkspacePath(_tempRoot));
    }

    [Fact]
    public void GetWorkspacePath_Initialized_ReturnsCorrectPath()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".sharc"));

        var result = RepoLocator.GetWorkspacePath(_tempRoot);

        Assert.Equal(Path.Combine(_tempRoot, ".sharc", "workspace.arc"), result);
    }

    [Fact]
    public void GetConfigPath_Initialized_ReturnsCorrectPath()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".sharc"));

        var result = RepoLocator.GetConfigPath(_tempRoot);

        Assert.Equal(Path.Combine(_tempRoot, ".sharc", "config.arc"), result);
    }
}
