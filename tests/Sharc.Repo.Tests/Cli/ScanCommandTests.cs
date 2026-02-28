// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class ScanCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public ScanCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
        InitCommand.Run(Array.Empty<string>());
        SeedSourceFiles();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedCwd);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void SeedSourceFiles()
    {
        CreateFile("src/Sharc.Crypto/AesGcmCipher.cs", """
            using Sharc.Core;
            namespace Sharc.Crypto;
            public sealed class AesGcmCipher { }
            """);

        CreateFile("src/Sharc.Core/BTree/BTreeReader.cs", """
            namespace Sharc.Core.BTree;
            public sealed class BTreeReader { }
            """);

        CreateFile("tests/Sharc.Tests/Crypto/AesGcmCipherTests.cs", """
            using Sharc.Crypto;
            namespace Sharc.Tests.Crypto;
            public sealed class AesGcmCipherTests { }
            """);
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Run_DryRun_ReturnsZero_NoWrite()
    {
        int exitCode = ScanCommand.Run(new[] { "--dry-run" });

        Assert.Equal(0, exitCode);

        // Verify nothing was written
        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        Assert.Empty(reader.ReadFeatures());
    }

    [Fact]
    public void Run_Default_WritesFeatures()
    {
        int exitCode = ScanCommand.Run(Array.Empty<string>());

        Assert.Equal(0, exitCode);

        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        Assert.True(reader.ReadFeatures().Count >= 20);
    }

    [Fact]
    public void Run_Default_WritesFilePurposes()
    {
        ScanCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        Assert.True(reader.ReadFilePurposes().Count >= 3);
    }

    [Fact]
    public void Run_Default_WritesFeatureEdges()
    {
        ScanCommand.Run(Array.Empty<string>());

        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        Assert.True(reader.ReadFeatureEdges().Count > 0);
    }

    [Fact]
    public void Run_Full_ClearsAndRescans()
    {
        // First scan
        ScanCommand.Run(Array.Empty<string>());

        // Second scan with --full
        int exitCode = ScanCommand.Run(new[] { "--full" });
        Assert.Equal(0, exitCode);

        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        Assert.True(reader.ReadFeatures().Count >= 20);
    }

    [Fact]
    public void Run_NotInitialized_ReturnsOne()
    {
        var noInitDir = Path.Combine(Path.GetTempPath(), $"sharc_noscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(noInitDir);
        Directory.CreateDirectory(Path.Combine(noInitDir, ".git"));
        var saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(noInitDir);

        try
        {
            int exitCode = ScanCommand.Run(Array.Empty<string>());
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
            try { Directory.Delete(noInitDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_Help_ReturnsZero()
    {
        int exitCode = ScanCommand.Run(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }
}
