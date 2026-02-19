// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class ViewQueryIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public ViewQueryIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_view_query_{Guid.NewGuid()}.db");
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

            CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER, amount REAL, status TEXT);
            INSERT INTO orders VALUES (101, 1, 100.50, 'completed');
            INSERT INTO orders VALUES (102, 1, 200.00, 'pending');
            INSERT INTO orders VALUES (103, 2, 300.00, 'completed');
            INSERT INTO orders VALUES (104, 3, 150.75, 'completed');

            -- SQLite view for compatibility testing
            CREATE VIEW v_user_emails AS SELECT name, email FROM users;
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── SQL queries on registered views ────────────────────────────

    [Fact]
    public void Query_RegisteredView_SelectStar()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_names").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT * FROM v_names");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void Query_RegisteredView_ColumnProjection()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age", "email").Named("v_info").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_info");
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
    }

    [Fact]
    public void Query_RegisteredView_WithWhere()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_ages").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_ages WHERE age >= 30");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    [Fact]
    public void Query_RegisteredView_WithOrderBy()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_sorted").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_sorted ORDER BY age");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
        Assert.Equal("Diana", names[0]);  // 22
        Assert.Equal("Bob", names[1]);    // 25
        Assert.Equal("Eve", names[2]);    // 28
        Assert.Equal("Alice", names[3]);  // 30
        Assert.Equal("Charlie", names[4]); // 35
    }

    [Fact]
    public void Query_RegisteredView_JoinWithTable()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("id", "name").Named("v_users").Build();
        db.RegisterView(view);

        using var reader = db.Query(
            "SELECT v_users.name, orders.amount FROM v_users JOIN orders ON v_users.id = orders.user_id ORDER BY orders.amount");
        var results = new List<(string Name, double Amount)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetDouble(1)));

        Assert.Equal(4, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(100.50, results[0].Amount, 2);
    }

    [Fact]
    public void Query_RegisteredSubview_Works()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var parent = ViewBuilder.From("users").Select("name", "age", "dept").Named("v_parent").Build();
        var sub = ViewBuilder.From(parent).Select("name", "age").Named("v_sub").Build();
        db.RegisterView(sub);

        using var reader = db.Query("SELECT name FROM v_sub WHERE age > 25");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count); // Alice(30), Charlie(35), Eve(28)
    }

    [Fact]
    public void Query_RegisteredView_WithProgrammaticFilter()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Programmatic filter: only 'eng' dept
        var view = ViewBuilder
            .From("users")
            .Select("name", "age", "dept")
            .Where(row => row.GetString(2) == "eng")
            .Named("v_eng")
            .Build();
        db.RegisterView(view);

        // SQL filter on top: age > 28
        using var reader = db.Query("SELECT name FROM v_eng WHERE age > 28");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Alice(30, eng) and Charlie(35, eng) pass both filters
        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
    }

    [Fact]
    public void Query_RegisteredView_Count()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_count").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT COUNT(*) FROM v_count");
        Assert.True(reader.Read());
        Assert.Equal(5L, reader.GetInt64(0));
    }

    [Fact]
    public void Query_RegisteredView_WithLimit()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_limit").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_limit ORDER BY age LIMIT 2");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Diana", names[0]);
        Assert.Equal("Bob", names[1]);
    }

    [Fact]
    public void Query_SQLiteView_StillWorks()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Query the existing SQLite view (no registration needed)
        using var reader = db.Query("SELECT name FROM v_user_emails ORDER BY name");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
        Assert.Equal("Alice", names[0]);
    }

    [Fact]
    public void Query_MixedRegisteredAndSQLite()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Register a programmatic view
        var view = ViewBuilder.From("orders").Select("user_id", "amount").Named("v_orders").Build();
        db.RegisterView(view);

        // Query uses both a registered view (v_orders) and works alongside SQLite views
        using var reader = db.Query("SELECT * FROM v_orders WHERE amount > 150");
        int count = 0;
        while (reader.Read()) count++;

        Assert.Equal(3, count); // 150.75, 200.00, and 300.00
    }

    // ─── Task 3: Gap coverage ─────────────────────────────────────

    [Fact]
    public void Query_RegisteredView_WithGroupBy()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age", "dept").Named("v_all").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT dept, COUNT(*) FROM v_all GROUP BY dept");
        var groups = new Dictionary<string, long>();
        while (reader.Read())
            groups[reader.GetString(0)] = reader.GetInt64(1);

        Assert.Equal(3, groups["eng"]);
        Assert.Equal(1, groups["sales"]);
        Assert.Equal(1, groups["hr"]);
    }

    [Fact]
    public void Query_RegisteredView_WithDistinct()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("dept").Named("v_dept").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT DISTINCT dept FROM v_dept ORDER BY dept");
        var depts = new List<string>();
        while (reader.Read())
            depts.Add(reader.GetString(0));

        Assert.Equal(3, depts.Count);
        Assert.Equal("eng", depts[0]);
        Assert.Equal("hr", depts[1]);
        Assert.Equal("sales", depts[2]);
    }

    [Fact]
    public void Query_TwoRegisteredViews_InJoin()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var userView = ViewBuilder.From("users").Select("id", "name").Named("v_users").Build();
        var orderView = ViewBuilder.From("orders").Select("user_id", "amount").Named("v_orders2").Build();
        db.RegisterView(userView);
        db.RegisterView(orderView);

        using var reader = db.Query(
            "SELECT v_users.name, v_orders2.amount FROM v_users JOIN v_orders2 ON v_users.id = v_orders2.user_id ORDER BY v_orders2.amount");
        var results = new List<(string Name, double Amount)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetDouble(1)));

        Assert.Equal(4, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(100.50, results[0].Amount);
    }

    [Fact]
    public void Query_RegisteredView_WithLimit_MaterializesAll()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_lim").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_lim ORDER BY age LIMIT 2");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Diana", names[0]); // age 22
        Assert.Equal("Bob", names[1]);   // age 25
    }

    // ─── Task 4: Cross-type filters & edge cases ────────────────────

    [Fact]
    public void Query_SubviewChain_ThreeDeep_WithSQL()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Level 1: all user columns
        var v1 = ViewBuilder.From("users").Select("id", "name", "age", "dept").Named("v_level1").Build();
        // Level 2: narrow to name + age
        var v2 = ViewBuilder.From(v1).Select("name", "age").Named("v_level2").Build();
        // Level 3: narrow to name only
        var v3 = ViewBuilder.From(v2).Select("name").Named("v_level3").Build();
        db.RegisterView(v3);

        using var reader = db.Query("SELECT * FROM v_level3 ORDER BY name");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
        Assert.Equal("Charlie", names[2]);
        Assert.Equal("Diana", names[3]);
        Assert.Equal("Eve", names[4]);
    }

    [Fact]
    public void OpenView_SubviewWithFilter_CursorFiltersCorrectly()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var parent = ViewBuilder.From("users").Select("name", "age").Named("v_parent").Build();
        var sub = ViewBuilder.From(parent)
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 28)
            .Named("v_filtered_sub")
            .Build();
        db.RegisterView(sub);

        using var cursor = db.OpenView("v_filtered_sub");
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        // Alice(30), Charlie(35), Eve(28) pass the filter
        Assert.Equal(3, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
        Assert.Contains("Eve", names);
    }

    [Fact]
    public void Query_RegisteredViewLeft_PhysicalTableRight_Join()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("users").Select("id", "name").Named("v_u").Build();
        db.RegisterView(view);

        using var reader = db.Query(
            "SELECT v_u.name, orders.amount FROM v_u JOIN orders ON v_u.id = orders.user_id ORDER BY orders.amount");
        var results = new List<(string Name, double Amount)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetDouble(1)));

        Assert.Equal(4, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(100.50, results[0].Amount);
        Assert.Equal("Charlie", results[1].Name);
        Assert.Equal(150.75, results[1].Amount);
    }

    [Fact]
    public void Query_BothRegisteredViews_Join()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var userView = ViewBuilder.From("users").Select("id", "name").Named("v_u2").Build();
        var orderView = ViewBuilder.From("orders").Select("user_id", "amount").Named("v_o2").Build();
        db.RegisterView(userView);
        db.RegisterView(orderView);

        using var reader = db.Query(
            "SELECT v_u2.name, v_o2.amount " +
            "FROM v_u2 JOIN v_o2 ON v_u2.id = v_o2.user_id " +
            "ORDER BY v_o2.amount");
        var results = new List<(string Name, double Amount)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetDouble(1)));

        // 4 orders matched to 3 users: Alice(100.50, 200.00), Bob(300.00), Charlie(150.75)
        Assert.Equal(4, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(100.50, results[0].Amount);
    }

    [Fact]
    public void CompoundQuery_UnionOfViewAndTable()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("users")
            .Select("name")
            .Where(row => row.GetString(0) == "Alice")
            .Named("v_alice")
            .Build();
        db.RegisterView(view);

        // UNION between the view (Alice) and a direct table query (Bob)
        using var reader = db.Query(
            "SELECT name FROM v_alice UNION ALL SELECT name FROM users WHERE name = 'Bob'");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Bob", names);
    }

    [Fact]
    public void Query_RegisteredView_OrderByDescending()
    {
        using var db = SharcDatabase.Open(_dbPath);
        var view = ViewBuilder.From("users").Select("name", "age").Named("v_desc").Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_desc ORDER BY age DESC LIMIT 3");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Equal("Charlie", names[0]); // 35
        Assert.Equal("Alice", names[1]);   // 30
        Assert.Equal("Eve", names[2]);     // 28
    }

    [Fact]
    public void Query_RegisteredView_OrderByNonProjectedColumn_Throws()
    {
        using var db = SharcDatabase.Open(_dbPath);
        // View only projects name — "email" is NOT in the view
        var view = ViewBuilder.From("users").Select("name").Named("v_name_only").Build();
        db.RegisterView(view);

        // ORDER BY a column that doesn't exist in the view should throw
        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = db.Query("SELECT name FROM v_name_only ORDER BY email");
            while (reader.Read()) { }
        });
    }
}
