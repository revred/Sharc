// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class CaseExpressionTests : IDisposable
{
    private readonly string _dbPath;

    public CaseExpressionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_case_test_{Guid.NewGuid()}.db");
        SetupDatabase();
    }

    private void SetupDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER, score REAL);

            INSERT INTO users VALUES (1, 'Alice', 30, 85.5);
            INSERT INTO users VALUES (2, 'Bob', 40, 92.0);
            INSERT INTO users VALUES (3, 'Charlie', 25, 60.0);
            INSERT INTO users VALUES (4, 'Diana', 35, NULL);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Case_SimpleWhenThenElse_ReturnsComputedColumn()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age > 30 THEN 'senior' ELSE 'junior' END AS category FROM users ORDER BY id");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("junior", reader.GetString(1)); // age=30, not > 30

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal("senior", reader.GetString(1)); // age=40

        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        Assert.Equal("junior", reader.GetString(1)); // age=25

        Assert.True(reader.Read());
        Assert.Equal("Diana", reader.GetString(0));
        Assert.Equal("senior", reader.GetString(1)); // age=35

        Assert.False(reader.Read());
    }

    [Fact]
    public void Case_MultipleWhens_FirstMatchWins()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age >= 40 THEN 'A' WHEN age >= 30 THEN 'B' ELSE 'C' END AS tier FROM users ORDER BY id");

        Assert.True(reader.Read());
        Assert.Equal("B", reader.GetString(1)); // Alice age=30

        Assert.True(reader.Read());
        Assert.Equal("A", reader.GetString(1)); // Bob age=40

        Assert.True(reader.Read());
        Assert.Equal("C", reader.GetString(1)); // Charlie age=25

        Assert.True(reader.Read());
        Assert.Equal("B", reader.GetString(1)); // Diana age=35

        Assert.False(reader.Read());
    }

    [Fact]
    public void Case_NoElse_ReturnsNull()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age > 50 THEN 'old' END AS label FROM users ORDER BY id");

        // No one has age > 50 — all should be NULL
        while (reader.Read())
        {
            Assert.True(reader.IsNull(1));
        }
    }

    [Fact]
    public void Case_WithIntegerResult_ReturnsInteger()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age >= 30 THEN 1 ELSE 0 END AS flag FROM users ORDER BY id");

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(1)); // Alice age=30

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(1)); // Bob age=40

        Assert.True(reader.Read());
        Assert.Equal(0L, reader.GetInt64(1)); // Charlie age=25

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(1)); // Diana age=35

        Assert.False(reader.Read());
    }

    [Fact]
    public void Case_WithNullColumn_NullComparisonFalse()
    {
        // Diana has score=NULL, comparison with NULL should be false
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN score > 80 THEN 'high' ELSE 'low' END AS rating FROM users WHERE name = 'Diana'");

        Assert.True(reader.Read());
        Assert.Equal("Diana", reader.GetString(0));
        Assert.Equal("low", reader.GetString(1)); // NULL > 80 is false → ELSE
        Assert.False(reader.Read());
    }

    [Fact]
    public void Case_WithOrderBy_SortsCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age > 30 THEN 'senior' ELSE 'junior' END AS category FROM users ORDER BY name");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));

        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));

        Assert.True(reader.Read());
        Assert.Equal("Diana", reader.GetString(0));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Case_WithLimit_ReturnsLimitedRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT name, CASE WHEN age > 30 THEN 'senior' ELSE 'junior' END AS category FROM users ORDER BY id LIMIT 2");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Case_MixedWithRegularColumns_ProjectsCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query(
            "SELECT id, name, CASE WHEN age >= 30 THEN 'mature' ELSE 'young' END AS group_label, age FROM users WHERE id = 1");

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal("Alice", reader.GetString(1));
        Assert.Equal("mature", reader.GetString(2));
        Assert.Equal(30L, reader.GetInt64(3));
        Assert.False(reader.Read());
    }
}
