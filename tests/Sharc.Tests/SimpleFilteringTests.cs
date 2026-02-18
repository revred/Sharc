// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Core.Query;
using Xunit;

namespace Sharc.Tests;

public class SimpleFilteringTests : IDisposable
{
    private readonly string _dbPath;

    public SimpleFilteringTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_filter_test_{Guid.NewGuid():N}.db");
        CreateTestDatabase();
    }

    private void CreateTestDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                age INTEGER,
                active INTEGER
            );
            INSERT INTO users (id, name, age, active) VALUES (1, 'Alice', 30, 1);
            INSERT INTO users (id, name, age, active) VALUES (2, 'Bob', 25, 1);
            INSERT INTO users (id, name, age, active) VALUES (3, 'Charlie', 35, 0);
            INSERT INTO users (id, name, age, active) VALUES (4, 'David', 25, 0);
        ";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void CanFilter_ByInteger_Equality()
    {
        using var db = SharcDatabase.Open(_dbPath);
        
        // Filter: age = 25
        var filter = new SharcFilter("age", SharcOperator.Equal, 25L);

        using var reader = db.CreateReader("users", filters: new[] { filter });

        var results = new List<string>();
        while (reader.Read())
        {
            results.Add(reader.GetString(1)); // name
        }

        Assert.Equal(2, results.Count);
        Assert.Contains("Bob", results);
        Assert.Contains("David", results);
    }

    [Fact]
    public void CanFilter_ByString_Equality()
    {
        using var db = SharcDatabase.Open(_dbPath);
        
        // Filter: name = 'Alice'
        var filter = new SharcFilter("name", SharcOperator.Equal, "Alice");

        using var reader = db.CreateReader("users", filters: new[] { filter });

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CanFilter_MultipleConditions()
    {
        using var db = SharcDatabase.Open(_dbPath);
        
        // Filter: age = 25 AND active = 1
        var filters = new[]
        {
            new SharcFilter("age", SharcOperator.Equal, 25L),
            new SharcFilter("active", SharcOperator.Equal, 1L)
        };

        using var reader = db.CreateReader("users", filters: filters);

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CanFilter_ByRowIdAlias()
    {
        using var db = SharcDatabase.Open(_dbPath);
        
        // Filter: id = 3 (INTEGER PRIMARY KEY)
        // This tests the special handling of rowid aliases which are NULL in the record
        var filter = new SharcFilter("id", SharcOperator.Equal, 3L);

        using var reader = db.CreateReader("users", filters: new[] { filter });

        Assert.True(reader.Read());
        Assert.Equal("Charlie", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Filter_NonExistentColumn_ThrowsArgumentException()
    {
        using var db = SharcDatabase.Open(_dbPath);
        
        var filter = new SharcFilter("nonexistent", SharcOperator.Equal, 1);

        Assert.Throws<ArgumentException>(() => db.CreateReader("users", filters: new[] { filter }));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
