// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Views;

public sealed class SharcViewTests : IDisposable
{
    private readonly string _dbPath;

    public SharcViewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_view_cursor_{Guid.NewGuid()}.db");
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
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER, email TEXT);
            INSERT INTO users VALUES (1, 'Alice', 30, 'alice@test.com');
            INSERT INTO users VALUES (2, 'Bob', 25, 'bob@test.com');
            INSERT INTO users VALUES (3, 'Charlie', 35, 'charlie@test.com');
            INSERT INTO users VALUES (4, 'Diana', 22, 'diana@test.com');
            INSERT INTO users VALUES (5, 'Eve', 28, 'eve@test.com');
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── SharcView.Open — SimpleViewCursor (all columns) ─────────

    [Fact]
    public void Open_AllColumns_ReturnsAllRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Named("all_users").Build();

        using var cursor = view.Open(db);
        int count = 0;
        while (cursor.MoveNext()) count++;

        Assert.Equal(5, count);
    }

    [Fact]
    public void Open_AllColumns_FieldCountMatchesTable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Named("all_users").Build();

        using var cursor = view.Open(db);
        Assert.Equal(4, cursor.FieldCount); // id, name, age, email
    }

    [Fact]
    public void Open_AllColumns_ReadsTypedValues()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Named("all_users").Build();

        using var cursor = view.Open(db);
        Assert.True(cursor.MoveNext());

        Assert.Equal(1L, cursor.GetInt64(0));  // id
        Assert.Equal("Alice", cursor.GetString(1)); // name
        Assert.Equal(30L, cursor.GetInt64(2)); // age
        Assert.Equal("alice@test.com", cursor.GetString(3)); // email
    }

    [Fact]
    public void Open_AllColumns_RowsReadIncrementsCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Named("all_users").Build();

        using var cursor = view.Open(db);
        Assert.Equal(0, cursor.RowsRead);

        cursor.MoveNext();
        Assert.Equal(1, cursor.RowsRead);

        cursor.MoveNext();
        Assert.Equal(2, cursor.RowsRead);
    }

    // ─── SharcView.Open — Projected columns ──────────────────────

    [Fact]
    public void Open_Projected_FieldCountMatchesProjection()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Named("name_age")
            .Build();

        using var cursor = view.Open(db);
        Assert.Equal(2, cursor.FieldCount);
    }

    [Fact]
    public void Open_Projected_ReadsCorrectColumns()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Named("name_age")
            .Build();

        using var cursor = view.Open(db);
        Assert.True(cursor.MoveNext());
        Assert.Equal("Alice", cursor.GetString(0)); // name
        Assert.Equal(30L, cursor.GetInt64(1));       // age
    }

    [Fact]
    public void Open_Projected_ColumnNamesMatch()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "email")
            .Named("name_email")
            .Build();

        using var cursor = view.Open(db);
        Assert.Equal("name", cursor.GetColumnName(0));
        Assert.Equal("email", cursor.GetColumnName(1));
    }

    // ─── SharcView.Open — FilteredViewCursor ─────────────────────

    [Fact]
    public void Open_WithFilter_ReturnsOnlyMatchingRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 30) // age >= 30
            .Named("seniors")
            .Build();

        using var cursor = view.Open(db);
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);   // age 30
        Assert.Contains("Charlie", names); // age 35
    }

    [Fact]
    public void Open_WithFilter_RowsReadCountsOnlyPassingRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) > 30) // only Charlie (35)
            .Named("over30")
            .Build();

        using var cursor = view.Open(db);
        while (cursor.MoveNext()) { }

        Assert.Equal(1, cursor.RowsRead);
    }

    [Fact]
    public void Open_WithFilterNoMatch_ReturnsNoRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Where(row => row.GetInt64(0) > 999) // no id > 999
            .Named("empty")
            .Build();

        using var cursor = view.Open(db);
        Assert.False(cursor.MoveNext());
        Assert.Equal(0, cursor.RowsRead);
    }

    // ─── SharcView immutability ──────────────────────────────────

    [Fact]
    public void SharcView_IsReusable_OpenMultipleTimes()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name")
            .Named("names")
            .Build();

        // First open
        using (var cursor1 = view.Open(db))
        {
            int count1 = 0;
            while (cursor1.MoveNext()) count1++;
            Assert.Equal(5, count1);
        }

        // Second open — same view, fresh cursor
        using (var cursor2 = view.Open(db))
        {
            int count2 = 0;
            while (cursor2.MoveNext()) count2++;
            Assert.Equal(5, count2);
        }
    }

    [Fact]
    public void SharcView_Properties_AreCorrect()
    {
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Named("my_view")
            .Build();

        Assert.Equal("my_view", view.Name);
        Assert.Equal("users", view.SourceTable);
        Assert.NotNull(view.ProjectedColumnNames);
        Assert.Equal(2, view.ProjectedColumnNames!.Count);
        Assert.Null(view.Filter);
    }

    [Fact]
    public void SharcView_WithFilter_HasFilterSet()
    {
        Func<IRowAccessor, bool> filter = row => row.GetInt64(0) > 0;
        var view = ViewBuilder
            .From("users")
            .Where(filter)
            .Named("filtered")
            .Build();

        Assert.NotNull(view.Filter);
        Assert.Equal(filter, view.Filter);
    }
}
