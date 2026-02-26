// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Data;

public sealed class KnowledgeWriterTests : IDisposable
{
    private readonly string _arcPath;
    private readonly SharcDatabase _db;
    private readonly long _now;

    public KnowledgeWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_kw_{Guid.NewGuid()}.arc");
        _db = WorkspaceSchemaBuilder.CreateSchema(_arcPath);
        _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    // ── Features ─────────────────────────────────────────────────────

    [Fact]
    public void WriteFeature_ReturnsPositiveRowId()
    {
        using var writer = new KnowledgeWriter(_db);
        long id = writer.WriteFeature(new FeatureRecord(
            "encryption", "AES-256-GCM page-level encryption", "crypto", "complete", _now, null));

        Assert.True(id > 0);
    }

    [Fact]
    public void WriteFeature_Roundtrips()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeature(new FeatureRecord(
            "btree-read", "B-tree traversal and cursor", "btree", "complete", _now, null));

        var reader = new KnowledgeReader(_db);
        var features = reader.ReadFeatures();
        Assert.Single(features);
        Assert.Equal("btree-read", features[0].Name);
        Assert.Equal("B-tree traversal and cursor", features[0].Description);
        Assert.Equal("btree", features[0].Layer);
        Assert.Equal("complete", features[0].Status);
    }

    [Fact]
    public void WriteFeature_DuplicateName_Skipped()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeature(new FeatureRecord("encryption", "First", "crypto", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("encryption", "Second", "crypto", "complete", _now, null));

        var reader = new KnowledgeReader(_db);
        var features = reader.ReadFeatures();
        Assert.Single(features);
        Assert.Equal("First", features[0].Description);
    }

    [Fact]
    public void WriteFeature_MultipleDistinctFeatures_AllPersist()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeature(new FeatureRecord("encryption", "Crypto", "crypto", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("btree-read", "B-tree", "btree", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("graph-engine", "Graph", "graph", "complete", _now, null));

        var reader = new KnowledgeReader(_db);
        Assert.Equal(3, reader.ReadFeatures().Count);
    }

    // ── Feature Edges ────────────────────────────────────────────────

    [Fact]
    public void WriteFeatureEdge_Roundtrips()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeatureEdge(new FeatureEdgeRecord(
            "encryption", "src/Sharc.Crypto/AesGcmCipher.cs", "source",
            "primary", true, _now, null));

        var reader = new KnowledgeReader(_db);
        var edges = reader.ReadFeatureEdges();
        Assert.Single(edges);
        Assert.Equal("encryption", edges[0].FeatureName);
        Assert.Equal("src/Sharc.Crypto/AesGcmCipher.cs", edges[0].TargetPath);
        Assert.Equal("source", edges[0].TargetKind);
        Assert.Equal("primary", edges[0].Role);
        Assert.True(edges[0].AutoDetected);
    }

    [Fact]
    public void WriteFeatureEdge_MultipleEdgesPerFeature_AllPersist()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "src/a.cs", "source", null, true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "src/b.cs", "source", null, true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "tests/c.cs", "test", null, true, _now, null));
        writer.WriteFeatureEdge(new FeatureEdgeRecord("encryption", "PRC/EncryptionSpec.md", "doc", null, true, _now, null));

        var reader = new KnowledgeReader(_db);
        Assert.Equal(4, reader.ReadFeatureEdges(featureName: "encryption").Count);
        Assert.Equal(2, reader.ReadFeatureEdges(featureName: "encryption", targetKind: "source").Count);
        Assert.Single(reader.ReadFeatureEdges(featureName: "encryption", targetKind: "doc"));
    }

    // ── File Purposes ────────────────────────────────────────────────

    [Fact]
    public void WriteFilePurpose_Roundtrips()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFilePurpose(new FilePurposeRecord(
            "src/Sharc.Core/BTree/BTreeReader.cs", "Generic B-tree traversal reader",
            "Sharc.Core", "Sharc.Core.BTree", "btree", true, _now, null));

        var reader = new KnowledgeReader(_db);
        var purposes = reader.ReadFilePurposes();
        Assert.Single(purposes);
        Assert.Equal("src/Sharc.Core/BTree/BTreeReader.cs", purposes[0].Path);
        Assert.Equal("Generic B-tree traversal reader", purposes[0].Purpose);
        Assert.Equal("Sharc.Core", purposes[0].Project);
        Assert.Equal("Sharc.Core.BTree", purposes[0].Namespace);
        Assert.Equal("btree", purposes[0].Layer);
    }

    [Fact]
    public void WriteFilePurpose_DuplicatePath_Skipped()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFilePurpose(new FilePurposeRecord("src/a.cs", "First", "Sharc", null, null, true, _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/a.cs", "Second", "Sharc", null, null, true, _now, null));

        var reader = new KnowledgeReader(_db);
        var purposes = reader.ReadFilePurposes();
        Assert.Single(purposes);
        Assert.Equal("First", purposes[0].Purpose);
    }

    // ── File Deps ────────────────────────────────────────────────────

    [Fact]
    public void WriteFileDep_Roundtrips()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFileDep(new FileDepRecord(
            "src/Sharc/SharcDatabase.cs", "src/Sharc.Core/IO/IPageSource.cs",
            "using", true, _now));

        var reader = new KnowledgeReader(_db);
        var deps = reader.ReadFileDeps();
        Assert.Single(deps);
        Assert.Equal("src/Sharc/SharcDatabase.cs", deps[0].SourcePath);
        Assert.Equal("src/Sharc.Core/IO/IPageSource.cs", deps[0].TargetPath);
        Assert.Equal("using", deps[0].DepKind);
        Assert.True(deps[0].AutoDetected);
    }

    [Fact]
    public void WriteFileDep_MultipleFromSameSource_AllPersist()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFileDep(new FileDepRecord("src/a.cs", "src/b.cs", "using", true, _now));
        writer.WriteFileDep(new FileDepRecord("src/a.cs", "src/c.cs", "using", true, _now));

        var reader = new KnowledgeReader(_db);
        Assert.Equal(2, reader.ReadFileDeps(sourcePath: "src/a.cs").Count);
    }

    // ── P0: ClearAutoDetected correctness ─────────────────────────────

    [Fact]
    public void ClearAutoDetected_DeletesFeaturesRows()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeature(new FeatureRecord("f1", "First", "core", "complete", _now, null));
        writer.WriteFeature(new FeatureRecord("f2", "Second", "core", "complete", _now, null));

        var reader = new KnowledgeReader(_db);
        Assert.Equal(2, reader.ReadFeatures().Count);

        writer.ClearAutoDetected();

        Assert.Empty(reader.ReadFeatures());
    }

    [Fact]
    public void ClearAutoDetected_ThenReinsert_NoDuplicates()
    {
        using var writer = new KnowledgeWriter(_db);
        writer.WriteFeature(new FeatureRecord("encryption", "V1", "crypto", "complete", _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/a.cs", "Purpose V1", "Sharc", null, null, true, _now, null));

        writer.ClearAutoDetected();

        writer.WriteFeature(new FeatureRecord("encryption", "V2", "crypto", "complete", _now, null));
        writer.WriteFilePurpose(new FilePurposeRecord("src/a.cs", "Purpose V2", "Sharc", null, null, true, _now, null));

        var reader = new KnowledgeReader(_db);
        var features = reader.ReadFeatures();
        Assert.Single(features);
        Assert.Equal("V2", features[0].Description);

        var purposes = reader.ReadFilePurposes();
        Assert.Single(purposes);
        Assert.Equal("Purpose V2", purposes[0].Purpose);
    }
}
