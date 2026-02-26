// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Cli;
using Sharc.Repo.Data;
using Xunit;

namespace Sharc.Repo.Tests.Cli;

[Collection("CLI")]
public sealed class FeatureCommandTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _savedCwd;

    public FeatureCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"sharc_feature_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, ".git"));
        _savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempRoot);
        InitCommand.Run(Array.Empty<string>());

        // Seed source files and run scan to populate knowledge graph
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
        int exitCode = FeatureCommand.Run(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_NoSubcommand_ReturnsZero()
    {
        int exitCode = FeatureCommand.Run(Array.Empty<string>());
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_List_ReturnsZero()
    {
        int exitCode = FeatureCommand.Run(new[] { "list" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_List_FilterByLayer_ReturnsZero()
    {
        int exitCode = FeatureCommand.Run(new[] { "list", "--layer", "crypto" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_Show_KnownFeature_ReturnsZero()
    {
        int exitCode = FeatureCommand.Run(new[] { "show", "encryption" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_Show_UnknownFeature_ReturnsOne()
    {
        int exitCode = FeatureCommand.Run(new[] { "show", "nonexistent-feature" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_Show_NoName_ReturnsOne()
    {
        int exitCode = FeatureCommand.Run(new[] { "show" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_Add_CreatesFeature()
    {
        int exitCode = FeatureCommand.Run(new[] { "add", "custom-feature", "A custom feature", "--layer", "api", "--status", "active" });
        Assert.Equal(0, exitCode);

        // Verify it exists
        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        var feature = reader.GetFeature("custom-feature");
        Assert.NotNull(feature);
        Assert.Equal("A custom feature", feature.Description);
        Assert.Equal("api", feature.Layer);
    }

    [Fact]
    public void Run_Add_Duplicate_ReturnsOne()
    {
        FeatureCommand.Run(new[] { "add", "dup-feature", "First", "--layer", "api", "--status", "active" });
        int exitCode = FeatureCommand.Run(new[] { "add", "dup-feature", "Second", "--layer", "api", "--status", "active" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_Link_CreatesEdge()
    {
        int exitCode = FeatureCommand.Run(new[] { "link", "encryption", "src/Sharc.Crypto/AesGcmCipher.cs", "--kind", "source" });
        Assert.Equal(0, exitCode);

        var wsPath = Path.Combine(_tempRoot, ".sharc", RepoLocator.WorkspaceFileName);
        using var db = SharcDatabase.Open(wsPath, new SharcOpenOptions { Writable = false });
        var reader = new KnowledgeReader(db);
        var edges = reader.ReadFeatureEdges(featureName: "encryption", targetPath: "src/Sharc.Crypto/AesGcmCipher.cs");
        Assert.True(edges.Count > 0);
    }

    [Fact]
    public void Run_UnknownSubcommand_ReturnsOne()
    {
        int exitCode = FeatureCommand.Run(new[] { "frobnicate" });
        Assert.Equal(1, exitCode);
    }
}
