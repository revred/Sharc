// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class ViewPromotionTests : IDisposable
{
    private readonly string _dbPath;

    public ViewPromotionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_view_promo_{Guid.NewGuid()}.db");
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
            CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL, status TEXT);

            INSERT INTO users VALUES (1, 'Alice', 30, 'alice@test.com');
            INSERT INTO users VALUES (2, 'Bob', 25, 'bob@test.com');
            INSERT INTO users VALUES (3, 'Charlie', 35, 'charlie@test.com');
            INSERT INTO users VALUES (4, 'Diana', 22, 'diana@test.com');
            INSERT INTO users VALUES (5, 'Eve', 28, 'eve@test.com');

            INSERT INTO orders VALUES (101, 1, 100.50, 'completed');
            INSERT INTO orders VALUES (102, 1, 200.00, 'pending');
            INSERT INTO orders VALUES (103, 2, 300.00, 'completed');
            INSERT INTO orders VALUES (104, 3, 150.75, 'completed');

            -- Simple projection (auto-promotable)
            CREATE VIEW v_user_names AS SELECT name, age FROM users;

            -- SELECT * (auto-promotable)
            CREATE VIEW v_all_users AS SELECT * FROM users;

            -- Filtered (NOT auto-promotable)
            CREATE VIEW v_high_value AS SELECT * FROM orders WHERE amount > 150;

            -- Joined (NOT auto-promotable)
            CREATE VIEW v_user_orders AS
                SELECT u.name, o.amount FROM users u JOIN orders o ON u.id = o.user_id;

            -- Nested view (NOT auto-promotable — references view, not table)
            CREATE VIEW v_nested AS SELECT * FROM v_user_names WHERE age > 28;
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── Auto-promotion: simple projection ───────────────────────

    [Fact]
    public void OpenView_SimpleProjection_ReturnsAllRows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_user_names");

        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void OpenView_SimpleProjection_CorrectFieldCount()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_user_names");

        Assert.Equal(2, cursor.FieldCount); // name, age
    }

    [Fact]
    public void OpenView_SimpleProjection_ReadsCorrectValues()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_user_names");

        Assert.True(cursor.MoveNext());
        // First row should be Alice (rowid 1)
        Assert.Equal("Alice", cursor.GetString(0));
        Assert.Equal(30L, cursor.GetInt64(1));
    }

    [Fact]
    public void OpenView_SimpleProjection_ColumnNamesCorrect()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_user_names");

        Assert.Equal("name", cursor.GetColumnName(0));
        Assert.Equal("age", cursor.GetColumnName(1));
    }

    [Fact]
    public void OpenView_SimpleProjection_RowsReadTracking()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_user_names");

        Assert.Equal(0, cursor.RowsRead);
        cursor.MoveNext();
        Assert.Equal(1, cursor.RowsRead);
        while (cursor.MoveNext()) { }
        Assert.Equal(5, cursor.RowsRead);
    }

    // ─── Auto-promotion: SELECT * ────────────────────────────────

    [Fact]
    public void OpenView_SelectStar_AllColumnsReturned()
    {
        using var db = SharcDatabase.Open(_dbPath);
        using var cursor = db.OpenView("v_all_users");

        Assert.Equal(4, cursor.FieldCount); // id, name, age, email
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.GetInt64(0));
        Assert.Equal("Alice", cursor.GetString(1));
        Assert.Equal(30L, cursor.GetInt64(2));
        Assert.Equal("alice@test.com", cursor.GetString(3));
    }

    // ─── Non-promotable views: OpenView should throw ─────────────

    [Fact]
    public void OpenView_FilteredView_ThrowsKeyNotFound()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var ex = Assert.Throws<KeyNotFoundException>(() => db.OpenView("v_high_value"));
        Assert.Contains("too complex", ex.Message);
    }

    [Fact]
    public void OpenView_JoinedView_ThrowsKeyNotFound()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var ex = Assert.Throws<KeyNotFoundException>(() => db.OpenView("v_user_orders"));
        Assert.Contains("too complex", ex.Message);
    }

    [Fact]
    public void OpenView_NonExistentView_ThrowsKeyNotFound()
    {
        using var db = SharcDatabase.Open(_dbPath);

        Assert.Throws<KeyNotFoundException>(() => db.OpenView("no_such_view"));
    }

    // ─── ViewInfo metadata enrichment ────────────────────────────

    [Fact]
    public void Schema_SimpleView_IsSharcExecutable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = db.Schema.GetView("v_user_names");

        Assert.NotNull(view);
        Assert.True(view!.ParseSucceeded);
        Assert.True(view.IsSharcExecutable);
        Assert.False(view.HasJoin);
        Assert.False(view.HasFilter);
        Assert.False(view.IsSelectAll);
        Assert.Single(view.SourceTables);
        Assert.Equal("users", view.SourceTables[0]);
        Assert.Equal(2, view.Columns.Count);
    }

    [Fact]
    public void Schema_SelectStarView_IsSharcExecutable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = db.Schema.GetView("v_all_users");

        Assert.NotNull(view);
        Assert.True(view!.IsSharcExecutable);
        Assert.True(view.IsSelectAll);
    }

    [Fact]
    public void Schema_FilteredView_NotSharcExecutable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = db.Schema.GetView("v_high_value");

        Assert.NotNull(view);
        Assert.True(view!.ParseSucceeded);
        Assert.False(view.IsSharcExecutable);
        Assert.True(view.HasFilter);
    }

    [Fact]
    public void Schema_JoinedView_NotSharcExecutable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = db.Schema.GetView("v_user_orders");

        Assert.NotNull(view);
        Assert.True(view!.ParseSucceeded);
        Assert.False(view.IsSharcExecutable);
        Assert.True(view.HasJoin);
    }

    // ─── ViewBuilder + Open — end-to-end ─────────────────────────

    [Fact]
    public void ViewBuilder_WithFilter_EndToEnd()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 30) // age >= 30
            .Named("adults")
            .Build();

        using var cursor = view.Open(db);
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    [Fact]
    public void ViewBuilder_NoProjection_ReturnsAllColumns()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("orders")
            .Named("all_orders")
            .Build();

        using var cursor = view.Open(db);
        Assert.Equal(4, cursor.FieldCount); // id, user_id, amount, status
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(4, count);
    }

    // ─── ViewPromoter integration with real schema ───────────────

    [Fact]
    public void ViewPromoter_SimpleView_PromotesSuccessfully()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var schema = db.Schema;
        var viewInfo = schema.GetView("v_user_names")!;

        var promoted = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.NotNull(promoted);
        Assert.Equal("v_user_names", promoted!.Name);
        Assert.Equal("users", promoted.SourceTable);
        Assert.NotNull(promoted.ProjectedColumnNames);
        Assert.Equal(2, promoted.ProjectedColumnNames!.Count);
    }

    [Fact]
    public void ViewPromoter_FilteredView_ReturnsNull()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var schema = db.Schema;
        var viewInfo = schema.GetView("v_high_value")!;

        var promoted = ViewPromoter.TryPromote(viewInfo, schema);

        Assert.Null(promoted);
    }

    // ─── Cursor reuse — same view opened multiple times ──────────

    [Fact]
    public void OpenView_MultipleOpens_IndependentCursors()
    {
        using var db = SharcDatabase.Open(_dbPath);

        using var cursor1 = db.OpenView("v_user_names");
        Assert.True(cursor1.MoveNext());
        Assert.Equal("Alice", cursor1.GetString(0));

        using var cursor2 = db.OpenView("v_user_names");
        Assert.True(cursor2.MoveNext());
        Assert.Equal("Alice", cursor2.GetString(0));

        // cursor1 should still be at row 1 position
        Assert.True(cursor1.MoveNext());
        Assert.Equal("Bob", cursor1.GetString(0));
    }
}
