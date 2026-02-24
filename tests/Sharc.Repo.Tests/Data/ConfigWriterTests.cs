// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Data;
using Sharc.Repo.Schema;
using Xunit;

namespace Sharc.Repo.Tests.Data;

public class ConfigWriterTests : IDisposable
{
    private readonly string _arcPath;

    public ConfigWriterTests()
    {
        _arcPath = Path.Combine(Path.GetTempPath(), $"sharc_cfgw_{Guid.NewGuid()}.arc");
    }

    public void Dispose()
    {
        try { File.Delete(_arcPath); } catch { }
        try { File.Delete(_arcPath + ".journal"); } catch { }
        GC.SuppressFinalize(this);
    }

    private SharcDatabase CreateDb()
    {
        var db = SharcDatabase.Create(_arcPath);
        using var tx = db.BeginTransaction();
        tx.Execute("""
            CREATE TABLE IF NOT EXISTS config (
                id INTEGER PRIMARY KEY,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            )
            """);
        tx.Commit();
        return db;
    }

    [Fact]
    public void Set_NewKey_InsertsEntry()
    {
        using var db = CreateDb();
        using var cw = new ConfigWriter(db);

        long rowId = cw.Set("test.key", "test.value");

        Assert.True(rowId > 0);
        Assert.Equal("test.value", cw.Get("test.key"));
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValue()
    {
        using var db = CreateDb();
        using var cw = new ConfigWriter(db);

        cw.Set("test.key", "original");
        cw.Set("test.key", "updated");

        Assert.Equal("updated", cw.Get("test.key"));

        // Should still be only 1 entry
        var all = cw.GetAll();
        Assert.Single(all);
    }

    [Fact]
    public void Get_ExistingKey_ReturnsValue()
    {
        using var db = CreateDb();
        using var cw = new ConfigWriter(db);

        cw.Set("hello", "world");

        Assert.Equal("world", cw.Get("hello"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        using var db = CreateDb();
        using var cw = new ConfigWriter(db);

        Assert.Null(cw.Get("nonexistent"));
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        using var db = CreateDb();
        using var cw = new ConfigWriter(db);

        cw.Set("key1", "val1");
        cw.Set("key2", "val2");
        cw.Set("key3", "val3");

        var all = cw.GetAll();
        Assert.Equal(3, all.Count);
    }
}
