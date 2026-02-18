// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Schema;
using Sharc;
using Sharc.Core.Query;
using Xunit;

namespace Sharc.IntegrationTests;

public class DDLTests : IDisposable
{
    private string _dbPath;
    private SharcDatabase _db;

    public DDLTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_ddl_{Guid.NewGuid()}.db");
        _db = SharcDatabase.Create(_dbPath);
    }

    public void Dispose()
    {
        _db?.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + ".journal"))
            File.Delete(_dbPath + ".journal");
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateTable_Simple_Succeeds()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT, Age INTEGER)");
            tx.Commit();
        }

        Assert.NotNull(_db.Schema.GetTable("Users"));
        var table = _db.Schema.GetTable("Users");
        Assert.Equal(3, table.Columns.Count);
        Assert.Equal("Id", table.Columns[0].Name);
        Assert.Equal("Name", table.Columns[1].Name);
        Assert.Equal("Age", table.Columns[2].Name);

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users",
                ColumnValue.FromInt64(1, 1),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
                ColumnValue.FromInt64(2, 30));
        }

        table = _db.Schema.GetTable("Users");
        Assert.NotNull(table);
        Assert.Equal(3, table.Columns.Count);

        using (var reader = _db.CreateReader("Users"))
        {
            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("Alice", reader.GetString(1));
            Assert.Equal(30L, reader.GetInt64(2));
        }
    }

    [Fact]
    public void AlterTable_AddColumn_Succeeds()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT)");
            tx.Commit();
        }

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users", ColumnValue.FromInt64(1, 1), ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")));
        }

        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("ALTER TABLE Users ADD COLUMN Age INTEGER");
            tx.Commit();
        }

        var table = _db.Schema.GetTable("Users");
        Assert.Equal(3, table.Columns.Count);
        Assert.Equal("Age", table.Columns[2].Name);

        using (var reader = _db.CreateReader("Users", filters: new[] { new SharcFilter("Id", SharcOperator.Equal, 1L) }))
        {
            Assert.True(reader.Read());
            Assert.True(reader.IsNull(2));
        }

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("Users",
                ColumnValue.FromInt64(1, 2),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Bob")),
                ColumnValue.FromInt64(2, 25));
        }

        using (var reader = _db.CreateReader("Users", filters: new[] { new SharcFilter("Id", SharcOperator.Equal, 2L) }))
        {
            Assert.True(reader.Read());
            Assert.Equal(25L, reader.GetInt64(2));
        }
    }

    [Fact]
    public void AlterTable_Rename_Succeeds()
    {
        using (var tx = _db.BeginTransaction())
        {
            tx.Execute("CREATE TABLE OldName (Id INTEGER PRIMARY KEY)");
            tx.Commit();
        }

        using (var writer = SharcWriter.From(_db))
        {
            writer.Insert("OldName", ColumnValue.FromInt64(1, 1));
        }

        using (var tx = _db.BeginTransaction())
        {
             tx.Execute("ALTER TABLE OldName RENAME TO NewName");
             tx.Commit();
        }

        Assert.Throws<KeyNotFoundException>(() => _db.Schema.GetTable("OldName"));
        Assert.NotNull(_db.Schema.GetTable("NewName"));

        using (var reader = _db.CreateReader("NewName"))
        {
            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
        }
    }
}
