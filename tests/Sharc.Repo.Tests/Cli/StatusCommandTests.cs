// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public class StatusCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public StatusCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_status_{Guid.NewGuid()}");
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
    public void Run_NotInitialized_ReturnsOne()
    {
        int exitCode = StatusCommand.Run(Array.Empty<string>());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_Initialized_ReturnsZero()
    {
        InitCommand.Run(Array.Empty<string>());

        int exitCode = StatusCommand.Run(Array.Empty<string>());

        Assert.Equal(0, exitCode);
    }
}
