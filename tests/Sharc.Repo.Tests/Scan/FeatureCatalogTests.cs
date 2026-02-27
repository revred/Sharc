// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Scan;
using Xunit;

namespace Sharc.Repo.Tests.Scan;

public sealed class FeatureCatalogTests
{
    [Fact]
    public void AllFeatures_HaveUniqueName()
    {
        var names = FeatureCatalog.All.Select(f => f.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AllFeatures_HaveNonEmptyLayer()
    {
        foreach (var f in FeatureCatalog.All)
            Assert.False(string.IsNullOrWhiteSpace(f.Layer), $"Feature '{f.Name}' has empty layer");
    }

    [Fact]
    public void AllFeatures_HaveAtLeastOneSourcePattern()
    {
        foreach (var f in FeatureCatalog.All)
            Assert.True(f.SourcePatterns.Length > 0, $"Feature '{f.Name}' has no source patterns");
    }

    [Fact]
    public void AllFeatures_HaveNonEmptyStatus()
    {
        foreach (var f in FeatureCatalog.All)
            Assert.False(string.IsNullOrWhiteSpace(f.Status), $"Feature '{f.Name}' has empty status");
    }

    [Fact]
    public void MatchFile_EncryptionSource_ReturnsEncryption()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Crypto/AesGcmCipher.cs");
        Assert.Contains("encryption", matches);
    }

    [Fact]
    public void MatchFile_BTreeReaderSource_ReturnsBtreeRead()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Core/BTree/BTreeReader.cs");
        Assert.Contains("btree-read", matches);
    }

    [Fact]
    public void MatchFile_GraphStore_ReturnsGraphEngine()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Graph/Store/ConceptStore.cs");
        Assert.Contains("graph-engine", matches);
    }

    [Fact]
    public void MatchFile_VectorSource_ReturnsVectorSearch()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Vector/HnswIndex.cs");
        Assert.Contains("vector-search", matches);
    }

    [Fact]
    public void MatchFile_QuerySource_ReturnsSqlPipeline()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Query/Sharq/SqlParser.cs");
        Assert.Contains("sql-pipeline", matches);
    }

    [Fact]
    public void MatchFile_WriteEngine_ReturnsWriteEngine()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc/Write/SharcWriter.cs");
        Assert.Contains("write-engine", matches);
    }

    [Fact]
    public void MatchFile_TrustSource_ReturnsTrustLayer()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc/Trust/AgentRegistry.cs");
        Assert.Contains("trust-layer", matches);
    }

    [Fact]
    public void MatchFile_UnrelatedPath_ReturnsEmpty()
    {
        var matches = FeatureCatalog.MatchFile("docs/README.md");
        Assert.Empty(matches);
    }

    [Fact]
    public void MatchFile_TestFile_MatchesByConvention()
    {
        // Test files under tests/ should match the feature they test
        var matches = FeatureCatalog.MatchFile("tests/Sharc.Tests/Crypto/AesGcmCipherTests.cs");
        Assert.Contains("encryption", matches);
    }

    [Fact]
    public void GetFeature_Exists_ReturnsDefinition()
    {
        var feature = FeatureCatalog.GetFeature("encryption");
        Assert.NotNull(feature);
        Assert.Equal("crypto", feature.Value.Layer);
    }

    [Fact]
    public void GetFeature_NotFound_ReturnsNull()
    {
        Assert.Null(FeatureCatalog.GetFeature("nonexistent"));
    }

    [Fact]
    public void FeatureCount_AtLeast20()
    {
        Assert.True(FeatureCatalog.All.Count >= 20,
            $"Expected at least 20 features, got {FeatureCatalog.All.Count}");
    }

    [Fact]
    public void MatchFile_ArcSource_ReturnsCrossArcSync()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Arc/Diff/ArcDiffer.cs");
        Assert.Contains("cross-arc-sync", matches);
    }

    [Fact]
    public void MatchFile_PageIOSource_ReturnsPageIO()
    {
        var matches = FeatureCatalog.MatchFile("src/Sharc.Core/IO/FilePageSource.cs");
        Assert.Contains("page-io", matches);
    }
}
