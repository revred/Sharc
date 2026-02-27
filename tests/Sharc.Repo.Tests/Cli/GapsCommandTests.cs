// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class GapsCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public GapsCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_gaps_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
        InitCommand.Run(Array.Empty<string>());

        // Seed source files and scan
        SeedAndScan();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void SeedAndScan()
    {
        var srcDir = Path.Combine(_tempRoot, "src", "Sharc.Crypto");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "AesGcmCipher.cs"),
            "namespace Sharc.Crypto;\npublic sealed class AesGcmCipher { }");

        ScanCommand.Run(Array.Empty<string>());
    }

    [Fact]
    public void Run_Help_ReturnsZero()
    {
        int exitCode = GapsCommand.Run(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_Default_ReturnsZero()
    {
        int exitCode = GapsCommand.Run(Array.Empty<string>());
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_DocsOnly_ReturnsZero()
    {
        int exitCode = GapsCommand.Run(new[] { "--docs" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_TestsOnly_ReturnsZero()
    {
        int exitCode = GapsCommand.Run(new[] { "--tests" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_OrphansOnly_ReturnsZero()
    {
        int exitCode = GapsCommand.Run(new[] { "--orphans" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_NotInitialized_ReturnsOne()
    {
        var noInitDir = Path.Combine(Path.GetTempPath(), $"sharc_nogaps_{Guid.NewGuid():N}");
        Directory.CreateDirectory(noInitDir);
        Directory.CreateDirectory(Path.Combine(noInitDir, ".git"));
        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noInitDir);

        try
        {
            int exitCode = GapsCommand.Run(Array.Empty<string>());
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noInitDir, recursive: true); } catch { }
        }
    }
}
