// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Xunit;

namespace Sharc.Index.Tests;

public sealed class GcdSchemaBuilderTests : IDisposable
{
    private readonly string _dbPath;

    public GcdSchemaBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_gcd_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void CreateSchema_NewFile_CreatesDatabase()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void CreateSchema_CommitsTableExists_HasExpectedColumns()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        var columns = GetColumnNames("commits");
        Assert.Contains("sha", columns);
        Assert.Contains("author_name", columns);
        Assert.Contains("author_email", columns);
        Assert.Contains("authored_date", columns);
        Assert.Contains("message", columns);
    }

    [Fact]
    public void CreateSchema_FilesTableExists_HasExpectedColumns()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        var columns = GetColumnNames("files");
        Assert.Contains("path", columns);
        Assert.Contains("first_seen_sha", columns);
        Assert.Contains("last_modified_sha", columns);
    }

    [Fact]
    public void CreateSchema_FileChangesTableExists_HasExpectedColumns()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        var columns = GetColumnNames("file_changes");
        Assert.Contains("commit_sha", columns);
        Assert.Contains("path", columns);
        Assert.Contains("lines_added", columns);
        Assert.Contains("lines_deleted", columns);
    }

    [Fact]
    public void CreateSchema_CommitsTable_ShaPrimaryKey()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        var pkColumns = GetPrimaryKeyColumns("commits");
        Assert.Single(pkColumns);
        Assert.Equal("sha", pkColumns[0]);
    }

    [Fact]
    public void CreateSchema_FileChangesTable_CompositePrimaryKey()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);

        var pkColumns = GetPrimaryKeyColumns("file_changes");
        Assert.Equal(2, pkColumns.Count);
        Assert.Contains("commit_sha", pkColumns);
        Assert.Contains("path", pkColumns);
    }

    [Fact]
    public void CreateSchema_CalledTwice_Idempotent()
    {
        GcdSchemaBuilder.CreateSchema(_dbPath);
        GcdSchemaBuilder.CreateSchema(_dbPath); // should not throw

        Assert.True(File.Exists(_dbPath));
    }

    private List<string> GetColumnNames(string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1)); // column 1 = name
        return columns;
    }

    private List<string> GetPrimaryKeyColumns(string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        var pkColumns = new List<string>();
        while (reader.Read())
        {
            if (reader.GetInt32(5) > 0) // column 5 = pk (0 = not PK, 1+ = PK ordinal)
                pkColumns.Add(reader.GetString(1));
        }
        return pkColumns;
    }
}
