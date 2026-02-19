// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.Tests.Views;

public sealed class ViewRegistrationTests : IDisposable
{
    private readonly string _dbPath;

    public ViewRegistrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_view_reg_{Guid.NewGuid()}.db");
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

            -- SQLite view for override test
            CREATE VIEW v_user_names AS SELECT name, age FROM users;
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── RegisterView / UnregisterView ──────────────────────────────

    [Fact]
    public void RegisterView_AddsView()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name").Named("my_view").Build();

        db.RegisterView(view);

        // Should be openable via OpenView
        using var cursor = db.OpenView("my_view");
        Assert.True(cursor.MoveNext());
        Assert.Equal("Alice", cursor.GetString(0));
    }

    [Fact]
    public void RegisterView_NullThrows()
    {
        using var db = SharcDatabase.Open(_dbPath);
        Assert.Throws<ArgumentNullException>(() => db.RegisterView(null!));
    }

    [Fact]
    public void UnregisterView_RemovesView()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name").Named("temp_view").Build();

        db.RegisterView(view);
        bool removed = db.UnregisterView("temp_view");

        Assert.True(removed);
        // OpenView should now fail (not in registered or SQLite views as promotable)
        Assert.Throws<KeyNotFoundException>(() => db.OpenView("temp_view"));
    }

    [Fact]
    public void UnregisterView_NonExistent_ReturnsFalse()
    {
        using var db = SharcDatabase.Open(_dbPath);
        bool removed = db.UnregisterView("no_such_view");
        Assert.False(removed);
    }

    [Fact]
    public void OpenView_RegisteredView_ReturnsCursor()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder
            .From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 30)
            .Named("seniors")
            .Build();

        db.RegisterView(view);

        using var cursor = db.OpenView("seniors");
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    [Fact]
    public void OpenView_RegisteredView_OverridesSQLiteView()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Register a programmatic view with the same name as a SQLite view
        var view = ViewBuilder
            .From("users")
            .Select("email")
            .Named("v_user_names") // same name as SQLite view
            .Build();

        db.RegisterView(view);

        using var cursor = db.OpenView("v_user_names");
        // Should return email (from registered view), not name+age (from SQLite view)
        Assert.Equal(1, cursor.FieldCount);
        Assert.True(cursor.MoveNext());
        Assert.Equal("alice@test.com", cursor.GetString(0));
    }

    [Fact]
    public void OpenView_RegisteredSubview_Works()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var parent = ViewBuilder.From("users").Select("name", "age").Named("parent").Build();
        var sub = ViewBuilder.From(parent).Select("name").Named("sub_view").Build();

        db.RegisterView(sub);

        using var cursor = db.OpenView("sub_view");
        Assert.Equal(1, cursor.FieldCount);
        Assert.True(cursor.MoveNext());
        Assert.Equal("Alice", cursor.GetString(0));
    }

    [Fact]
    public void RegisterView_DuplicateName_Overwrites()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view1 = ViewBuilder.From("users").Select("name").Named("dup").Build();
        var view2 = ViewBuilder.From("users").Select("email").Named("dup").Build();

        db.RegisterView(view1);
        db.RegisterView(view2);

        using var cursor = db.OpenView("dup");
        Assert.Equal(1, cursor.FieldCount);
        Assert.True(cursor.MoveNext());
        // Should return email (view2 overwrote view1)
        Assert.Equal("alice@test.com", cursor.GetString(0));
    }

    // ─── Task 3: Gap coverage ─────────────────────────────────────

    [Fact]
    public void RegisterView_EmptyName_Throws()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // Bypass ViewBuilder (which also validates) to test the RegisterView guard directly
        var view = new SharcView("", "users", new[] { "name" }, null);
        Assert.Throws<ArgumentException>(() => db.RegisterView(view));
    }

    [Fact]
    public void RegisterView_WhitespaceName_Throws()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = new SharcView("   ", "users", new[] { "name" }, null);
        Assert.Throws<ArgumentException>(() => db.RegisterView(view));
    }

    [Fact]
    public void ListRegisteredViews_Empty_ReturnsEmpty()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var views = db.ListRegisteredViews();
        Assert.Empty(views);
    }

    [Fact]
    public void ListRegisteredViews_ReturnsRegisteredNames()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var v1 = ViewBuilder.From("users").Select("name").Named("view_a").Build();
        var v2 = ViewBuilder.From("users").Select("email").Named("view_b").Build();
        db.RegisterView(v1);
        db.RegisterView(v2);

        var views = db.ListRegisteredViews();
        Assert.Equal(2, views.Count);
        Assert.Contains("view_a", views);
        Assert.Contains("view_b", views);
    }

    [Fact]
    public void ListRegisteredViews_AfterUnregister_ExcludesRemoved()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var v1 = ViewBuilder.From("users").Select("name").Named("keep").Build();
        var v2 = ViewBuilder.From("users").Select("email").Named("remove").Build();
        db.RegisterView(v1);
        db.RegisterView(v2);

        db.UnregisterView("remove");

        var views = db.ListRegisteredViews();
        Assert.Single(views);
        Assert.Contains("keep", views);
    }

    [Fact]
    public void RegisterView_Twice_OverwritesPrevious()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var v1 = ViewBuilder.From("users").Select("name").Named("x").Build();
        var v2 = ViewBuilder.From("users").Select("email").Named("x").Build();
        db.RegisterView(v1);
        db.RegisterView(v2);

        // Should only have 1 entry, not 2
        Assert.Single(db.ListRegisteredViews());

        using var cursor = db.OpenView("x");
        Assert.True(cursor.MoveNext());
        Assert.Equal("alice@test.com", cursor.GetString(0));
    }

    [Fact]
    public void GenerationCounter_RegisterUnregisterRegister_ResolvesCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Register a view and query it
        var v1 = ViewBuilder.From("users").Select("name").Named("gen_test").Build();
        db.RegisterView(v1);
        using (var reader = db.Query("SELECT * FROM gen_test"))
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(5, count);
        }

        // Unregister
        db.UnregisterView("gen_test");

        // Re-register with different projection
        var v2 = ViewBuilder.From("users").Select("email").Named("gen_test").Build();
        db.RegisterView(v2);

        // Query should use the NEW view (email, not name)
        using (var reader = db.Query("SELECT * FROM gen_test"))
        {
            Assert.True(reader.Read());
            // The column should be email, not name
            Assert.Equal("alice@test.com", reader.GetString(0));
        }
    }

    [Fact]
    public void ResolveViews_ThreeRegisteredViews_AllResolve()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var v1 = ViewBuilder.From("users").Select("name").Named("v1").Build();
        var v2 = ViewBuilder.From("users").Select("age").Named("v2").Build();
        var v3 = ViewBuilder.From("users").Select("email").Named("v3").Build();
        db.RegisterView(v1);
        db.RegisterView(v2);
        db.RegisterView(v3);

        // All three should be queryable
        using var r1 = db.Query("SELECT * FROM v1");
        Assert.True(r1.Read());
        Assert.Equal("Alice", r1.GetString(0));

        using var r2 = db.Query("SELECT * FROM v2");
        Assert.True(r2.Read());
        Assert.Equal(30L, r2.GetInt64(0));

        using var r3 = db.Query("SELECT * FROM v3");
        Assert.True(r3.Read());
        Assert.Equal("alice@test.com", r3.GetString(0));
    }

    // ─── Task 10: Safety hardening ────────────────────────────────

    [Fact]
    public void ViewBuilder_Select_NullColumnName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ViewBuilder.From("users").Select(null!, "age"));
    }

    [Fact]
    public void ViewBuilder_Select_WhitespaceColumnName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ViewBuilder.From("users").Select("name", "  "));
    }

    [Fact]
    public void OpenView_SubviewDepthExceeded_Throws()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Build a chain of 12 views (exceeds the depth limit of 10)
        SharcView current = ViewBuilder.From("users").Select("name").Named("depth_0").Build();
        for (int i = 1; i <= 11; i++)
        {
            current = ViewBuilder.From(current).Named($"depth_{i}").Build();
        }

        db.RegisterView(current);

        var ex = Assert.Throws<InvalidOperationException>(() => db.OpenView($"depth_11"));
        Assert.Contains("depth exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
