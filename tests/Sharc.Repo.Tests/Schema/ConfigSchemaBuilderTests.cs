// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Schema;

public class ConfigSchemaBuilderTests : IDisposable
{
    private readonly string _arcPath;

    public ConfigSchemaBuilderTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_cfg_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateSchema_NewDatabase_CreatesConfigTable()
    {
        using var db = ConfigSchemaBuilder.CreateSchema(_arcPath);

        var tableNames = db.Schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("config", tableNames);
    }

    [Fact]
    public void CreateSchema_SeedsDefaultEntries()
    {
        using var db = ConfigSchemaBuilder.CreateSchema(_arcPath);
        using var cw = new ConfigWriter(db);

        var entries = cw.GetAll();
        Assert.Equal(ConfigSchemaBuilder.Defaults.Count, entries.Count);

        // Verify a few specific defaults
        Assert.Equal("enabled", cw.Get("channel.notes"));
        Assert.Equal("disabled", cw.Get("channel.conversations"));
        Assert.Equal("false", cw.Get("encryption.enabled"));
    }

    [Fact]
    public void CreateSchema_Idempotent_DoesNotDuplicateDefaults()
    {
        using var db1 = ConfigSchemaBuilder.CreateSchema(_arcPath);
        db1.Dispose();

        using var db2 = ConfigSchemaBuilder.CreateSchema(_arcPath);
        using var cw = new ConfigWriter(db2);

        var entries = cw.GetAll();
        Assert.Equal(ConfigSchemaBuilder.Defaults.Count, entries.Count);
    }
}
