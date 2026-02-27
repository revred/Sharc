// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Data;

public sealed class KnowledgeReaderTests : IDisposable
{
    private readonly string _arcPath;
    private readonly SharcDatabase _db;
    private readonly long _now;

    public KnowledgeReaderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_kr_{Guid.NewGuid()}.arc");
        _db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SeedData();
    }

    private void SeedData()
    {
        using var writer = new KnowledgeWriter(_db);

        // Features
        writer.WriteFeature(new FeatureRecord("encryption", "AES-256-GCM crypto", "crypto", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("btree-read", "B-tree traversal", "btree", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("vector-search", "SIMD vector search", "vector", "in-progress", _now, null));

        // Feature edges
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "src/Sharc.Crypto/Cipher.cs", "source", "primary", true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "tests/Sharc.Tests/Crypto/CipherTests.cs", "test", null, true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "PRC/EncryptionSpec.md", "doc", null, true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("btree-read", "src/Sharc.Core/BTree/BTreeReader.cs", "source", "primary", true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("btree-read", "tests/Sharc.Tests/BTree/BTreeReaderTests.cs", "test", null, true, _now, null));
        // vector-search has source but no doc — gap
        writer.WriteFeatureEdge(new FeatureEdgeRecord("vector-search", "src/Sharc.Vector/VectorQuery.cs", "source", "primary", true, _now, null));

        // File purposes
        writer.WriteFilePurpose(new FilePurposeRecord("src/Sharc.Crypto/Cipher.cs", "AES-GCM cipher", "Sharc.Crypto", "Sharc.Crypto", "crypto", true, _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/Sharc.Core/BTree/BTreeReader.cs", "B-tree reader", "Sharc.Core", "Sharc.Core.BTree", "btree", true, _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/Sharc.Vector/VectorQuery.cs", "Vector similarity", "Sharc.Vector", "Sharc.Vector", "vector", true, _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/Sharc/Orphan.cs", "Orphan file", "Sharc", "Sharc", "api", true, _now, null));

        // File deps
        writer.WriteFileDep(new FileDepRecord("src/Sharc/SharcDatabase.cs", "src/Sharc.Core/IO/IPageSource.cs", "using", true, _now));
        writer.WriteFileDep(new FileDepRecord("src/Sharc/SharcDatabase.cs", "src/Sharc.Crypto/Cipher.cs", "using", true, _now));
        writer.WriteFileDep(new FileDepRecord("src/Sharc.Crypto/Cipher.cs", "src/Sharc.Core/IO/IPageSource.cs", "using", true, _now));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    // ── ReadFeatures ─────────────────────────────────────────────────

    [Fact]
    public void ReadFeatures_NoFilter_ReturnsAll()
    {
        var reader = new KnowledgeReader(_db);
        Assert.Equal(3, reader.ReadFeatures().Count);
    }

    [Fact]
    public void ReadFeatures_FilterByLayer_OnlyMatchingReturned()
    {
        var reader = new KnowledgeReader(_db);
        var crypto = reader.ReadFeatures(layer: "crypto");
        Assert.Single(crypto);
        Assert.Equal("encryption", crypto[0].Name);
    }

    [Fact]
    public void ReadFeatures_FilterByStatus_OnlyMatchingReturned()
    {
        var reader = new KnowledgeReader(_db);
        var inProgress = reader.ReadFeatures(status: "in-progress");
        Assert.Single(inProgress);
        Assert.Equal("vector-search", inProgress[0].Name);
    }

    [Fact]
    public void GetFeature_Exists_ReturnsRecord()
    {
        var reader = new KnowledgeReader(_db);
        var feature = reader.GetFeature("encryption");
        Assert.NotNull(feature);
        Assert.Equal("crypto", feature.Layer);
    }

    [Fact]
    public void GetFeature_NotFound_ReturnsNull()
    {
        var reader = new KnowledgeReader(_db);
        Assert.Null(reader.GetFeature("nonexistent"));
    }

    // ── ReadFeatureEdges ─────────────────────────────────────────────

    [Fact]
    public void ReadFeatureEdges_FilterByFeatureName_ReturnsEdges()
    {
        var reader = new KnowledgeReader(_db);
        var edges = reader.ReadFeatureEdges(featureName: "encryption");
        Assert.Equal(3, edges.Count); // source + test + doc
    }

    [Fact]
    public void ReadFeatureEdges_FilterByTargetKind_SourceOnly()
    {
        var reader = new KnowledgeReader(_db);
        var sources = reader.ReadFeatureEdges(targetKind: "source");
        Assert.Equal(3, sources.Count); // one per feature
    }

    [Fact]
    public void ReadFeatureEdges_FilterByTargetPath_ReturnsMatching()
    {
        var reader = new KnowledgeReader(_db);
        var edges = reader.ReadFeatureEdges(targetPath: "PRC/EncryptionSpec.md");
        Assert.Single(edges);
        Assert.Equal("encryption", edges[0].FeatureName);
    }

    [Fact]
    public void ReadFeatureEdges_CombinedFilters_ReturnsIntersection()
    {
        var reader = new KnowledgeReader(_db);
        var edges = reader.ReadFeatureEdges(featureName: "encryption", targetKind: "test");
        Assert.Single(edges);
        Assert.Contains("CipherTests.cs", edges[0].TargetPath);
    }

    // ── ReadFilePurposes ─────────────────────────────────────────────

    [Fact]
    public void ReadFilePurposes_FilterByProject_ReturnsMatching()
    {
        var reader = new KnowledgeReader(_db);
        var core = reader.ReadFilePurposes(project: "Sharc.Core");
        Assert.Single(core);
        Assert.Equal("src/Sharc.Core/BTree/BTreeReader.cs", core[0].Path);
    }

    [Fact]
    public void GetFilePurpose_Exists_ReturnsRecord()
    {
        var reader = new KnowledgeReader(_db);
        var fp = reader.GetFilePurpose("src/Sharc.Crypto/Cipher.cs");
        Assert.NotNull(fp);
        Assert.Equal("AES-GCM cipher", fp.Purpose);
    }

    [Fact]
    public void GetFilePurpose_NotFound_ReturnsNull()
    {
        var reader = new KnowledgeReader(_db);
        Assert.Null(reader.GetFilePurpose("nonexistent.cs"));
    }

    // ── ReadFileDeps ─────────────────────────────────────────────────

    [Fact]
    public void ReadFileDeps_FilterBySourcePath_ReturnsOutgoing()
    {
        var reader = new KnowledgeReader(_db);
        var deps = reader.ReadFileDeps(sourcePath: "src/Sharc/SharcDatabase.cs");
        Assert.Equal(2, deps.Count);
    }

    [Fact]
    public void ReadFileDeps_FilterByTargetPath_ReturnsIncoming()
    {
        var reader = new KnowledgeReader(_db);
        var deps = reader.ReadFileDeps(targetPath: "src/Sharc.Core/IO/IPageSource.cs");
        Assert.Equal(2, deps.Count); // SharcDatabase + Cipher both depend on it
    }

    // ── Gap Analysis ─────────────────────────────────────────────────

    [Fact]
    public void FindFeaturesWithoutDocs_VectorSearchHasNoDoc_ReturnsIt()
    {
        var reader = new KnowledgeReader(_db);
        var gaps = reader.FindFeaturesWithoutDocs();
        Assert.Contains("vector-search", gaps);
        Assert.DoesNotContain("encryption", gaps);
    }

    [Fact]
    public void FindOrphanFiles_FileNotInAnyFeature_ReturnsIt()
    {
        var reader = new KnowledgeReader(_db);
        var orphans = reader.FindOrphanFiles();
        Assert.Contains("src/Sharc/Orphan.cs", orphans);
        Assert.DoesNotContain("src/Sharc.Crypto/Cipher.cs", orphans);
    }
}
