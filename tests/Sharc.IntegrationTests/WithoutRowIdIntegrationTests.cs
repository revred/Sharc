using Sharc;
using Sharc.Core;
using Sharc.Core.Query;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class WithoutRowIdIntegrationTests
{
    [Fact]
    public void CreateReader_WithoutRowIdTextPk_ReadsAllRows()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE config (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('host', 'localhost')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('port', '8080')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('debug', 'true')");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("config");

        var rows = new Dictionary<string, string>();
        while (reader.Read())
            rows[reader.GetString(0)] = reader.GetString(1);

        Assert.Equal(3, rows.Count);
        Assert.Equal("localhost", rows["host"]);
        Assert.Equal("8080", rows["port"]);
        Assert.Equal("true", rows["debug"]);
    }

    [Fact]
    public void CreateReader_WithoutRowIdIntegerPk_RowIdMatchesPk()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT) WITHOUT ROWID");
            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (10, 'alpha')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (20, 'beta')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (30, 'gamma')");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items");

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.RowId);

        Assert.Equal(3, ids.Count);
        Assert.Contains(10L, ids);
        Assert.Contains(20L, ids);
        Assert.Contains(30L, ids);
    }

    [Fact]
    public void CreateReader_WithoutRowIdCompositePk_ReadsAllRows()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, """
                CREATE TABLE edges (
                    source TEXT,
                    target TEXT,
                    weight REAL,
                    PRIMARY KEY (source, target)
                ) WITHOUT ROWID
            """);
            TestDatabaseFactory.Execute(conn, "INSERT INTO edges VALUES ('A', 'B', 1.0)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO edges VALUES ('A', 'C', 2.5)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO edges VALUES ('B', 'C', 0.5)");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("edges");

        var count = 0;
        while (reader.Read())
        {
            Assert.False(string.IsNullOrEmpty(reader.GetString(0)));
            Assert.False(string.IsNullOrEmpty(reader.GetString(1)));
            count++;
        }

        Assert.Equal(3, count);
    }

    [Fact]
    public void CreateReader_WithoutRowIdProjection_ReturnsSelectedColumns()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE config (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('host', 'localhost')");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("config", "value");

        Assert.True(reader.Read());
        Assert.Equal("localhost", reader.GetString(0));
        Assert.Equal(1, reader.FieldCount);
    }

    [Fact]
    public void CreateReader_WithoutRowIdEmpty_ReadReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE empty_wr (key TEXT PRIMARY KEY) WITHOUT ROWID");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("empty_wr");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Schema_WithoutRowIdTable_IsWithoutRowIdTrue()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE wr (key TEXT PRIMARY KEY, val TEXT) WITHOUT ROWID");
        });

        using var db = SharcDatabase.OpenMemory(data);
        var table = db.Schema.GetTable("wr");

        Assert.True(table.IsWithoutRowId);
    }

    [Fact]
    public void CreateReader_WithoutRowIdWithFilter_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE config (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('host', 'localhost')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('port', '8080')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO config VALUES ('debug', 'true')");
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("config",
            new SharcFilter("key", SharcOperator.Equal, "port"));

        Assert.True(reader.Read());
        Assert.Equal("8080", reader.GetString(1));
        Assert.False(reader.Read());
    }
}
