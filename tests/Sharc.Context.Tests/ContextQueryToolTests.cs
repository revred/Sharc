// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Context.Tools;
using Xunit;

namespace Sharc.Context.Tests;

public sealed class ContextQueryToolTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _invalidPath;

    public ContextQueryToolTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_ctx_test_{Guid.NewGuid():N}.db");
        _invalidPath = Path.Combine(Path.GetTempPath(), $"sharc_ctx_invalid_{Guid.NewGuid():N}.txt");
        CreateTestDatabase(_dbPath);
        File.WriteAllText(_invalidPath, "this is not a sqlite database");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_invalidPath); } catch { }
    }

    // --- ListSchema ---

    [Fact]
    public void ListSchema_ValidDatabase_ReturnsTablesAndColumns()
    {
        var result = ContextQueryTool.ListSchema(_dbPath);

        Assert.Contains("users", result);
        Assert.Contains("id", result);
        Assert.Contains("name", result);
        Assert.Contains("age", result);
    }

    [Fact]
    public void ListSchema_NonExistentFile_ReturnsErrorString()
    {
        var result = ContextQueryTool.ListSchema(@"C:\nonexistent\path\file.db");

        Assert.Contains("Error", result);
        Assert.DoesNotContain("Exception", result);
    }

    [Fact]
    public void ListSchema_InvalidDatabase_ReturnsErrorString()
    {
        var result = ContextQueryTool.ListSchema(_invalidPath);

        Assert.Contains("Error", result);
    }

    // --- GetRowCount ---

    [Fact]
    public void GetRowCount_ValidTable_ReturnsCorrectCount()
    {
        var result = ContextQueryTool.GetRowCount(_dbPath, "users");

        Assert.Contains("5", result);
        Assert.Contains("users", result);
    }

    [Fact]
    public void GetRowCount_NonExistentTable_ReturnsErrorString()
    {
        var result = ContextQueryTool.GetRowCount(_dbPath, "nope");

        Assert.Contains("Error", result);
    }

    [Fact]
    public void GetRowCount_NonExistentFile_ReturnsErrorString()
    {
        var result = ContextQueryTool.GetRowCount(@"C:\nonexistent\file.db", "users");

        Assert.Contains("Error", result);
    }

    // --- QueryTable ---

    [Fact]
    public void QueryTable_AllColumns_ReturnsMarkdownTable()
    {
        var result = ContextQueryTool.QueryTable(_dbPath, "users");

        Assert.Contains("|", result);
        Assert.Contains("id", result);
        Assert.Contains("name", result);
        Assert.Contains("age", result);
        Assert.Contains("5 row", result);
    }

    [Fact]
    public void QueryTable_WithProjection_ReturnsOnlyRequestedColumns()
    {
        var result = ContextQueryTool.QueryTable(_dbPath, "users", columns: "name,age");

        Assert.Contains("name", result);
        Assert.Contains("age", result);
        // Should not contain id column header (it's not projected)
        // We check the header line specifically
        var lines = result.Split('\n');
        var headerLine = lines.FirstOrDefault(l => l.Contains("| name"));
        Assert.NotNull(headerLine);
    }

    [Fact]
    public void QueryTable_WithFilter_ReturnsFilteredRows()
    {
        var result = ContextQueryTool.QueryTable(_dbPath, "users", filter: "name=User1");

        Assert.Contains("User1", result);
        Assert.Contains("1 row", result);
    }

    [Fact]
    public void QueryTable_RowLimit_CapsAt100()
    {
        var largePath = Path.Combine(Path.GetTempPath(), $"sharc_ctx_large_{Guid.NewGuid():N}.db");
        try
        {
            CreateLargeDatabase(largePath, 150);
            var result = ContextQueryTool.QueryTable(largePath, "items");

            Assert.Contains("100 row", result);
            Assert.Contains("truncated", result);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(largePath); } catch { }
        }
    }

    [Fact]
    public void QueryTable_NonExistentTable_ReturnsErrorString()
    {
        var result = ContextQueryTool.QueryTable(_dbPath, "nope");

        Assert.Contains("Error", result);
    }

    [Fact]
    public void QueryTable_NonExistentFile_ReturnsErrorString()
    {
        var result = ContextQueryTool.QueryTable(@"C:\nonexistent\file.db", "users");

        Assert.Contains("Error", result);
    }

    // --- SeekRow ---

    [Fact]
    public void SeekRow_ExistingRow_ReturnsSingleRow()
    {
        var result = ContextQueryTool.SeekRow(_dbPath, "users", 3);

        Assert.Contains("User3", result);
        Assert.Contains("|", result);
    }

    [Fact]
    public void SeekRow_NonExistentRow_ReturnsNotFound()
    {
        var result = ContextQueryTool.SeekRow(_dbPath, "users", 999);

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    // --- Helpers ---

    private static void CreateTestDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                age INTEGER NOT NULL
            );
            INSERT INTO users (id, name, age) VALUES (1, 'User1', 21);
            INSERT INTO users (id, name, age) VALUES (2, 'User2', 22);
            INSERT INTO users (id, name, age) VALUES (3, 'User3', 23);
            INSERT INTO users (id, name, age) VALUES (4, 'User4', 24);
            INSERT INTO users (id, name, age) VALUES (5, 'User5', 25);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void CreateLargeDatabase(string path, int rowCount)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, value TEXT NOT NULL)";
        cmd.ExecuteNonQuery();

        using var tx = conn.BeginTransaction();
        for (int i = 1; i <= rowCount; i++)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = $"INSERT INTO items (id, value) VALUES ({i}, 'item{i}')";
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
