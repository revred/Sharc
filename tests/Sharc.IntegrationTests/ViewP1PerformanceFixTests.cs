// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests for P1 performance fixes:
///   P1-4: Lazy Cote resolution for filter-free registered views
///   P1-6: Elimination of double materialization for filtered views
///
/// These tests exercise paths that were previously masked by forced materialization.
/// After the fix, filter-free views use the lazy Cote resolution path while
/// filtered views and JOINs still go through materialization.
/// </summary>
public sealed class ViewP1PerformanceFixTests : IDisposable
{
    private readonly string _dbPath;

    public ViewP1PerformanceFixTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_p1_{Guid.NewGuid()}.db");
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
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT,
                price REAL,
                category TEXT,
                stock INTEGER
            );
            INSERT INTO products VALUES (1, 'Widget',          9.99,  'tools', 50);
            INSERT INTO products VALUES (2, 'Gadget',          19.99, 'tech',  30);
            INSERT INTO products VALUES (3, 'Doohicky',        4.99,  'tools', 100);
            INSERT INTO products VALUES (4, 'Thingamajig',     29.99, 'tech',  10);
            INSERT INTO products VALUES (5, 'Whatchamacallit', 14.99, 'misc',  75);

            CREATE TABLE sales (
                id INTEGER PRIMARY KEY,
                product_id INTEGER,
                quantity INTEGER,
                total REAL
            );
            INSERT INTO sales VALUES (1, 1, 5, 49.95);
            INSERT INTO sales VALUES (2, 2, 2, 39.98);
            INSERT INTO sales VALUES (3, 3, 10, 49.90);
            INSERT INTO sales VALUES (4, 4, 1, 29.99);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── P1-4: Lazy Cote — ORDER BY on non-projected column ────────

    [Fact]
    public void Query_FilterFreeView_OrderByNonProjectedColumn_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // View projects [name, price, stock]. Outer SELECT only wants name.
        // ORDER BY stock — stock is in the view but not in the SELECT.
        var view = ViewBuilder.From("products")
            .Select("name", "price", "stock")
            .Named("v_products")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_products ORDER BY stock");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
        Assert.Equal("Thingamajig", names[0]);      // stock=10
        Assert.Equal("Gadget", names[1]);            // stock=30
        Assert.Equal("Widget", names[2]);            // stock=50
        Assert.Equal("Whatchamacallit", names[3]);   // stock=75
        Assert.Equal("Doohicky", names[4]);          // stock=100
    }

    [Fact]
    public void Query_FilterFreeView_WhereAndOrderByNonProjected_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("name", "price", "stock", "category")
            .Named("v_prods")
            .Build();
        db.RegisterView(view);

        // WHERE on category (in view), ORDER BY stock (in view, not in SELECT)
        using var reader = db.Query(
            "SELECT name FROM v_prods WHERE category = 'tools' ORDER BY stock");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Widget", names[0]);    // stock=50
        Assert.Equal("Doohicky", names[1]);  // stock=100
    }

    [Fact]
    public void Query_FilterFreeView_OrderByDescNonProjected_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("name", "price")
            .Named("v_desc")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_desc ORDER BY price DESC LIMIT 2");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Thingamajig", names[0]);  // 29.99
        Assert.Equal("Gadget", names[1]);        // 19.99
    }

    // ─── P1-4: Lazy Cote — JOINs fall back to materialization ──────

    [Fact]
    public void Query_FilterFreeView_JoinWithTable_StillWorks()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("id", "name", "price")
            .Named("v_prods_j")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query(
            "SELECT v_prods_j.name, sales.quantity " +
            "FROM v_prods_j JOIN sales ON v_prods_j.id = sales.product_id " +
            "ORDER BY sales.quantity");
        var results = new List<(string Name, long Qty)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetInt64(1)));

        Assert.Equal(4, results.Count);
        Assert.Equal("Thingamajig", results[0].Name);  // qty=1
        Assert.Equal("Gadget", results[1].Name);        // qty=2
        Assert.Equal("Widget", results[2].Name);        // qty=5
        Assert.Equal("Doohicky", results[3].Name);      // qty=10
    }

    // ─── P1-4: Lazy Cote — aggregates and DISTINCT ─────────────────

    [Fact]
    public void Query_FilterFreeView_Distinct_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("category")
            .Named("v_cats")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT DISTINCT category FROM v_cats ORDER BY category");
        var cats = new List<string>();
        while (reader.Read())
            cats.Add(reader.GetString(0));

        Assert.Equal(3, cats.Count);
        Assert.Equal("misc", cats[0]);
        Assert.Equal("tech", cats[1]);
        Assert.Equal("tools", cats[2]);
    }

    [Fact]
    public void Query_FilterFreeView_GroupByCount_Correct()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("name", "category")
            .Named("v_grp")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query(
            "SELECT category, COUNT(*) FROM v_grp GROUP BY category ORDER BY category");
        var results = new List<(string Cat, long Count)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetInt64(1)));

        Assert.Equal(3, results.Count);
        Assert.Equal("misc", results[0].Cat);
        Assert.Equal(1L, results[0].Count);
        Assert.Equal("tech", results[1].Cat);
        Assert.Equal(2L, results[1].Count);
        Assert.Equal("tools", results[2].Cat);
        Assert.Equal(2L, results[2].Count);
    }

    // ─── P1-6: Filtered views with double materialization ──────────

    [Fact]
    public void Query_FilteredView_OrderBy_StillCorrect()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // View with programmatic filter — requires materialization
        var view = ViewBuilder.From("products")
            .Select("name", "price", "category")
            .Where(row => row.GetString(2) == "tech")
            .Named("v_tech")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT name FROM v_tech ORDER BY price");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Gadget", names[0]);      // 19.99
        Assert.Equal("Thingamajig", names[1]); // 29.99
    }

    [Fact]
    public void Query_FilteredAndFilterFreeViews_Coexist()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Filter-free view
        var allView = ViewBuilder.From("products")
            .Select("name", "price")
            .Named("v_all_prods")
            .Build();
        db.RegisterView(allView);

        // Filtered view — requires materialization
        var techView = ViewBuilder.From("products")
            .Select("name", "price", "category")
            .Where(row => row.GetString(2) == "tech")
            .Named("v_tech_only")
            .Build();
        db.RegisterView(techView);

        // Query each — both should return correct results
        using var reader1 = db.Query("SELECT name FROM v_all_prods ORDER BY price LIMIT 2");
        var names1 = new List<string>();
        while (reader1.Read())
            names1.Add(reader1.GetString(0));

        Assert.Equal(2, names1.Count);
        Assert.Equal("Doohicky", names1[0]);   // 4.99
        Assert.Equal("Widget", names1[1]);     // 9.99

        using var reader2 = db.Query("SELECT name FROM v_tech_only ORDER BY price");
        var names2 = new List<string>();
        while (reader2.Read())
            names2.Add(reader2.GetString(0));

        Assert.Equal(2, names2.Count);
        Assert.Equal("Gadget", names2[0]);
        Assert.Equal("Thingamajig", names2[1]);
    }

    // ─── Regression: repeated queries with cache ───────────────────

    [Fact]
    public void Query_FilterFreeView_RepeatedQueries_Consistent()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products")
            .Select("name", "price")
            .Named("v_cached")
            .Build();
        db.RegisterView(view);

        // Same query 3 times — cached resolved intent should be consistent
        for (int i = 0; i < 3; i++)
        {
            using var reader = db.Query("SELECT name FROM v_cached ORDER BY price LIMIT 1");
            Assert.True(reader.Read());
            Assert.Equal("Doohicky", reader.GetString(0));
        }
    }

    [Fact]
    public void Query_FilterFreeView_AfterUnregister_Throws()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder.From("products").Select("name").Named("v_temp").Build();
        db.RegisterView(view);

        // Query works
        using var reader = db.Query("SELECT * FROM v_temp");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);

        // Unregister and re-query should fail
        db.UnregisterView("v_temp");
        Assert.Throws<KeyNotFoundException>(() => db.Query("SELECT * FROM v_temp"));
    }
}
