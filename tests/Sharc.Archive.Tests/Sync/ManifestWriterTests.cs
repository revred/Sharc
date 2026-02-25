// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive;
using Sharc.Archive.Sync;
using Xunit;

namespace Sharc.Archive.Tests.Sync;

public class ManifestWriterTests : IDisposable
{
    private readonly string _arcPath;
    private readonly SharcDatabase _db;

    public ManifestWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_manifest_{Guid.NewGuid()}.arc");
        _db = ArchiveSchemaBuilder.CreateSchema(_arcPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Insert_SingleManifest_ReturnsRowId()
    {
        using var mw = new ManifestWriter(_db);
        var record = new ManifestRecord("frag-1", 1, "arc://remote/data.arc", 1000, 5, 10, null, null);

        long rowId = mw.Insert(record);

        Assert.True(rowId > 0);
    }

    [Fact]
    public void ReadAll_MultipleEntries_ReturnsAll()
    {
        using var mw = new ManifestWriter(_db);
        mw.Insert(new ManifestRecord("frag-1", 1, null, 1000, 5, 10, null, null));
        mw.Insert(new ManifestRecord("frag-2", 2, "arc://remote.arc", 2000, 3, 20, null, null));

        var all = mw.ReadAll();

        Assert.Equal(2, all.Count);
        Assert.Equal("frag-1", all[0].FragmentId);
        Assert.Equal("frag-2", all[1].FragmentId);
    }

    [Fact]
    public void FindByFragmentId_Exists_ReturnsRecord()
    {
        using var mw = new ManifestWriter(_db);
        mw.Insert(new ManifestRecord("frag-1", 1, null, 1000, 5, 10, null, null));
        mw.Insert(new ManifestRecord("frag-2", 2, "arc://remote.arc", 2000, 3, 20, null, null));

        var found = mw.FindByFragmentId("frag-2");

        Assert.NotNull(found);
        Assert.Equal("frag-2", found!.FragmentId);
        Assert.Equal(2, found.Version);
        Assert.Equal("arc://remote.arc", found.SourceUri);
    }

    [Fact]
    public void FindByFragmentId_NotExists_ReturnsNull()
    {
        using var mw = new ManifestWriter(_db);
        mw.Insert(new ManifestRecord("frag-1", 1, null, 1000, 5, 10, null, null));

        var found = mw.FindByFragmentId("nonexistent");

        Assert.Null(found);
    }

    [Fact]
    public void Insert_WithChecksum_RoundTrips()
    {
        using var mw = new ManifestWriter(_db);
        var checksum = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        mw.Insert(new ManifestRecord("frag-cs", 1, null, 3000, 1, 5, checksum, null));

        var found = mw.FindByFragmentId("frag-cs");

        Assert.NotNull(found);
        Assert.NotNull(found!.Checksum);
        Assert.Equal(checksum, found.Checksum);
    }

    [Fact]
    public void Insert_WithMetadata_RoundTrips()
    {
        using var mw = new ManifestWriter(_db);
        var metadata = new Dictionary<string, object?> { ["source"] = "ci", ["build"] = 42L };
        mw.Insert(new ManifestRecord("frag-md", 1, null, 4000, 2, 8, null, metadata));

        var found = mw.FindByFragmentId("frag-md");

        Assert.NotNull(found);
        Assert.NotNull(found!.Metadata);
        Assert.Equal("ci", found.Metadata!["source"]);
    }
}
