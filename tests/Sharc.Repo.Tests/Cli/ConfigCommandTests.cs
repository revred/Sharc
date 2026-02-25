// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public class ConfigCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public ConfigCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_config_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);

        // Initialize
        InitCommand.Run(Array.Empty<string>());
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Run_NoArgs_ListsAllConfig_ReturnsZero()
    {
        int exitCode = ConfigCommand.Run(Array.Empty<string>());

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_GetKey_ReturnsZero()
    {
        int exitCode = ConfigCommand.Run(new[] { "channel.notes" });

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_GetMissingKey_ReturnsOne()
    {
        int exitCode = ConfigCommand.Run(new[] { "nonexistent.key" });

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_SetKey_ReturnsZero()
    {
        int exitCode = ConfigCommand.Run(new[] { "channel.conversations", "enabled" });

        Assert.Equal(0, exitCode);

        // Verify the value was set
        using var db = SharcDatabase.Open(
            Path.Combine(_tempRoot, ".sharc", "config.arc"),
            new SharcOpenOptions { Writable = false });
        using var cw = new Sharc.Repo.Data.ConfigWriter(db);
        Assert.Equal("enabled", cw.Get("channel.conversations"));
    }

    [Fact]
    public void Run_NotInitialized_ReturnsOne()
    {
        var noInitDir = Path.Combine(Path.GetTempPath(), $"sharc_noinit_{Guid.NewGuid()}");
        Directory.CreateDirectory(noInitDir);
        Directory.CreateDirectory(Path.Combine(noInitDir, ".git"));
        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noInitDir);

        try
        {
            int exitCode = ConfigCommand.Run(Array.Empty<string>());
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noInitDir, recursive: true); } catch { }
        }
    }
}
