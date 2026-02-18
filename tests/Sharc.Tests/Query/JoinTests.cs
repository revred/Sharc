// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class JoinTests : IDisposable
{
    private readonly string _dbPath;

    public JoinTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_join_test_{Guid.NewGuid()}.db");
        SetupDatabase();
    }

    private void SetupDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL);

            INSERT INTO users VALUES (1, 'Alice', 30);
            INSERT INTO users VALUES (2, 'Bob', 40);
            INSERT INTO users VALUES (3, 'Charlie', 25);

            INSERT INTO orders VALUES (10, 1, 100.5);
            INSERT INTO orders VALUES (11, 1, 200.0);
            INSERT INTO orders VALUES (12, 2, 300.0);
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
    public void InnerJoin_ReturnsMatchingRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id ORDER BY o.id");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void LeftJoin_ReturnsAllLeftRows_WithNulls()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // Order by u.id to ensure deterministic order for assertion
        using var reader = db.Query("SELECT u.name, o.amount FROM users u LEFT JOIN orders o ON u.id = o.user_id ORDER BY u.id, o.id");

        // Alice (1) -> Order 10
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(100.5, reader.GetDouble(1));

        // Alice (1) -> Order 11
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        // Bob (2) -> Order 12
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        // Charlie (3) -> NULL
        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        Assert.True(reader.IsNull(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void CrossJoin_ReturnsCartesianProduct()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.id FROM users u CROSS JOIN orders o");

        int count = 0;
        while (reader.Read())
        {
            count++;
        }
        // 3 users * 3 orders = 9 rows
        Assert.Equal(9, count);
    }

    [Fact]
    public void Join_WithWhereClause_FiltersCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id WHERE o.amount > 150 ORDER BY o.amount");

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(200.0, reader.GetDouble(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }

    [Fact]
    public void Join_WithTableAliases_ResolvesCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // Explicit aliases are used in the query string
        using var reader = db.Query("SELECT T1.name, T2.amount FROM users T1 JOIN orders T2 ON T1.id = T2.user_id WHERE T1.name = 'Bob'");

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.Equal(300.0, reader.GetDouble(1));

        Assert.False(reader.Read());
    }
}
