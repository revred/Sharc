// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Views;

public sealed class SubviewTests : IDisposable
{
    private readonly string _dbPath;

    public SubviewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_subview_{Guid.NewGuid()}.db");
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
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER, email TEXT, dept TEXT);
            INSERT INTO users VALUES (1, 'Alice',   30, 'alice@test.com',   'eng');
            INSERT INTO users VALUES (2, 'Bob',     25, 'bob@test.com',     'sales');
            INSERT INTO users VALUES (3, 'Charlie', 35, 'charlie@test.com', 'eng');
            INSERT INTO users VALUES (4, 'Diana',   22, 'diana@test.com',   'hr');
            INSERT INTO users VALUES (5, 'Eve',     28, 'eve@test.com',     'eng');
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── Subview: all columns (no projection) ──────────────────────

    [Fact]
    public void FromView_AllColumns_ReturnsAllRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Named("sub_all").Build();

        using var cursor = sub.Open(db);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void FromView_AllColumns_FieldCountMatchesParent()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Named("sub_all").Build();

        using var cursor = sub.Open(db);
        Assert.Equal(5, cursor.FieldCount); // id, name, age, email, dept
    }

    // ─── Subview: projected columns ─────────────────────────────────

    [Fact]
    public void FromView_ProjectedColumns_FieldCountCorrect()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name", "age").Named("sub_proj").Build();

        using var cursor = sub.Open(db);
        Assert.Equal(2, cursor.FieldCount);
    }

    [Fact]
    public void FromView_ProjectedColumns_ReadsCorrectValues()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name", "age").Named("sub_proj").Build();

        using var cursor = sub.Open(db);
        Assert.True(cursor.MoveNext());
        Assert.Equal("Alice", cursor.GetString(0));
        Assert.Equal(30L, cursor.GetInt64(1));
    }

    [Fact]
    public void FromView_ProjectedColumns_ColumnNamesMatch()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name", "email").Named("sub_cols").Build();

        using var cursor = sub.Open(db);
        Assert.Equal("name", cursor.GetColumnName(0));
        Assert.Equal("email", cursor.GetColumnName(1));
    }

    // ─── Subview: with filter ───────────────────────────────────────

    [Fact]
    public void FromView_WithFilter_FiltersRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder
            .From(parent)
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 30) // age >= 30
            .Named("sub_filtered")
            .Build();

        using var cursor = sub.Open(db);
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    // ─── Subview: parent has filter, both compose ───────────────────

    [Fact]
    public void FromView_ParentHasFilter_BothFiltersApply()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Parent: only engineers
        var parent = ViewBuilder
            .From("users")
            .Select("name", "age", "dept")
            .Where(row => row.GetString(2) == "eng") // dept == 'eng'
            .Named("engineers")
            .Build();

        // Sub: engineers over 28
        var sub = ViewBuilder
            .From(parent)
            .Select("name", "age")
            .Where(row => row.GetInt64(1) > 28) // age > 28
            .Named("senior_engineers")
            .Build();

        using var cursor = sub.Open(db);
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        // Alice (30, eng), Charlie (35, eng) pass both filters
        // Eve (28, eng) fails age > 28
        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    // ─── Subview: nested projection (parent projects, sub projects subset) ──

    [Fact]
    public void FromView_ParentHasProjection_SubviewProjectsSubset()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Parent projects: name, age, email (3 of 5 columns)
        var parent = ViewBuilder
            .From("users")
            .Select("name", "age", "email")
            .Named("basic_info")
            .Build();

        // Sub projects: name, email (2 of parent's 3 columns)
        var sub = ViewBuilder
            .From(parent)
            .Select("name", "email")
            .Named("contact_info")
            .Build();

        using var cursor = sub.Open(db);
        Assert.Equal(2, cursor.FieldCount);
        Assert.True(cursor.MoveNext());
        Assert.Equal("Alice", cursor.GetString(0));
        Assert.Equal("alice@test.com", cursor.GetString(1));
    }

    // ─── Subview: RowsRead tracking ─────────────────────────────────

    [Fact]
    public void FromView_RowsRead_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name").Named("sub_count").Build();

        using var cursor = sub.Open(db);
        Assert.Equal(0, cursor.RowsRead);
        cursor.MoveNext();
        Assert.Equal(1, cursor.RowsRead);
        while (cursor.MoveNext()) { }
        Assert.Equal(5, cursor.RowsRead);
    }

    // ─── Subview: three-deep chain ──────────────────────────────────

    [Fact]
    public void FromView_ThreeDeep_Works()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var level1 = ViewBuilder
            .From("users")
            .Select("name", "age", "dept")
            .Named("level1")
            .Build();

        var level2 = ViewBuilder
            .From(level1)
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 25) // age >= 25
            .Named("level2")
            .Build();

        var level3 = ViewBuilder
            .From(level2)
            .Select("name")
            .Named("level3")
            .Build();

        using var cursor = level3.Open(db);
        Assert.Equal(1, cursor.FieldCount);
        Assert.Equal("name", cursor.GetColumnName(0));

        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        // Alice(30), Bob(25), Charlie(35), Eve(28) pass age>=25; Diana(22) excluded
        Assert.Equal(4, names.Count);
        Assert.DoesNotContain("Diana", names);
    }

    // ─── Subview: reusable (multiple opens) ─────────────────────────

    [Fact]
    public void FromView_Reusable_MultipleOpens()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name").Named("sub_reuse").Build();

        // First open
        using (var c1 = sub.Open(db))
        {
            int count = 0;
            while (c1.MoveNext()) count++;
            Assert.Equal(5, count);
        }

        // Second open — fresh cursor
        using (var c2 = sub.Open(db))
        {
            int count = 0;
            while (c2.MoveNext()) count++;
            Assert.Equal(5, count);
        }
    }

    // ─── Subview: Named sets name ───────────────────────────────────

    [Fact]
    public void FromView_Named_SetsName()
    {
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Named("my_subview").Build();

        Assert.Equal("my_subview", sub.Name);
    }

    // ─── Subview: SourceView populated ──────────────────────────────

    [Fact]
    public void FromView_Build_SourceViewSet()
    {
        var parent = ViewBuilder.From("users").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Named("sub").Build();

        Assert.NotNull(sub.SourceView);
        Assert.Same(parent, sub.SourceView);
        Assert.Null(sub.SourceTable);
    }

    // ─── Subview: table-based view has no SourceView ────────────────

    [Fact]
    public void FromTable_Build_SourceViewIsNull()
    {
        var view = ViewBuilder.From("users").Named("table_view").Build();

        Assert.Null(view.SourceView);
        Assert.Equal("users", view.SourceTable);
    }

    // ─── Subview: null parent throws ────────────────────────────────

    [Fact]
    public void FromView_NullParent_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => ViewBuilder.From((SharcView)null!));
    }
}
