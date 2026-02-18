// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Tests.Query;

public sealed class ViewTests : IDisposable
{
    private readonly string _dbPath;

    public ViewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_view_test_{Guid.NewGuid()}.db");
        SetupDatabase();
    }

    private void SetupDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = DELETE;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL);
            
            INSERT INTO users VALUES (1, 'Alice', 30);
            INSERT INTO users VALUES (2, 'Bob', 25);
            INSERT INTO users VALUES (3, 'Charlie', 35);
            INSERT INTO orders VALUES (101, 1, 100.50);
            INSERT INTO orders VALUES (102, 1, 200.00);
            INSERT INTO orders VALUES (103, 2, 300.00);

            CREATE VIEW v_users_simple AS SELECT name, age FROM users;
            CREATE VIEW v_high_value_orders AS SELECT * FROM orders WHERE amount > 150;
            CREATE VIEW v_user_orders AS 
                SELECT u.name, o.amount 
                FROM users u 
                JOIN orders o ON u.id = o.user_id;

            CREATE VIEW v_nested_view AS SELECT * FROM v_users_simple WHERE age > 28;
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void SelectFromSimpleView_ReturnsRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT * FROM v_users_simple ORDER BY name");
        
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal(30, reader.GetInt64(1));
        
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        
        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        
        Assert.False(reader.Read());
    }

    [Fact]
    public void SelectFromFilteredView_ReturnsFilteredRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT * FROM v_high_value_orders ORDER BY amount");
        
        Assert.True(reader.Read());
        Assert.Equal(200.0, reader.GetDouble(2)); // amount is 3rd column in * select
        
        Assert.True(reader.Read());
        Assert.Equal(300.0, reader.GetDouble(2));
        
        Assert.False(reader.Read());
    }

    [Fact]
    public void SelectFromViewWithJoin_ReturnsJoinedRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT * FROM v_user_orders");
        
        var results = new List<(string Name, double Amount)>();
        while (reader.Read())
        {
            results.Add((reader.GetString(0), reader.GetDouble(1)));
        }

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Name == "Alice" && r.Amount == 100.5);
        Assert.Contains(results, r => r.Name == "Alice" && r.Amount == 200.0);
        Assert.Contains(results, r => r.Name == "Bob" && r.Amount == 300.0);
    }

    [Fact]
    public void SelectFromNestedView_ResolvesRecursively()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT * FROM v_nested_view ORDER BY name");
        
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        
        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(0));
        
        Assert.False(reader.Read());
    }

    [Fact]
    public void SelectFromView_WithAdditionalFilter()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var reader = db.Query("SELECT * FROM v_users_simple WHERE age < 30");
        
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        
        Assert.False(reader.Read());
    }
}
