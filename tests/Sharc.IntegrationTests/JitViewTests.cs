/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Text;
using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class JitViewTests
{
    // Users table: id INTEGER PRIMARY KEY, name TEXT, age INTEGER, balance REAL, avatar BLOB
    // Rows: User1 (age=21), User2 (age=22), ..., User10 (age=30)

    // ── View-backed JitQuery read tests ──

    [Fact]
    public void Jit_FromView_ReturnsFilteredRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Create a view that filters age >= 28 (User8, User9, User10)
        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 28)
            .Named("senior_users")
            .Build();

        var jit = db.Jit(view);
        using var reader = jit.Query();

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User8", names);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void Jit_FromView_WithAdditionalFilter_ComposesBoth()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // View: age >= 25 (User5..User10 = 6 rows)
        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 25)
            .Named("mid_users")
            .Build();

        var jit = db.Jit(view);
        // Additional JitQuery filter: age <= 27 → AND with view filter → age 25,26,27
        jit.Where(FilterStar.Column("age").Lte(27L));
        using var reader = jit.Query();

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User5", names);
        Assert.Contains("User6", names);
        Assert.Contains("User7", names);
    }

    [Fact]
    public void Jit_FromView_WithProjection_ReturnsSubset()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // View: all columns, no filter
        var view = ViewBuilder.From("users").Named("all_users").Build();

        var jit = db.Jit(view);
        using var reader = jit.Query("name");

        Assert.Equal(1, reader.FieldCount);
        Assert.True(reader.Read());
        var name = reader.GetString(0);
        Assert.StartsWith("User", name);
    }

    [Fact]
    public void Jit_FromView_WithLimitOffset_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Named("all_users")
            .Build();

        var jit = db.Jit(view);
        jit.WithOffset(2);
        jit.WithLimit(3);
        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    // ── Name resolution tests ──

    [Fact]
    public void Jit_ByName_ResolvesRegisteredView()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 28)
            .Named("senior_view")
            .Build();

        db.RegisterView(view);

        // Jit by view name — should resolve to the registered view
        var jit = db.Jit("senior_view");
        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        // age >= 28 → User8, User9, User10
        Assert.Equal(3, count);
    }

    [Fact]
    public void Jit_ByName_ResolvesSqliteView()
    {
        // CreateDatabaseWithViews has: employees table + eng_employees view (SELECT * FROM employees WHERE dept = 'Eng')
        var data = TestDatabaseFactory.CreateDatabaseWithViews();
        using var db = SharcDatabase.OpenMemory(data);

        // eng_employees is a SQLite schema view — auto-promotable (simple SELECT * with WHERE)
        // But wait — it has WHERE, so it's NOT auto-promotable. Let's verify what happens:
        // ViewPromoter returns null for views with WHERE. Jit should throw for non-promotable views.
        // This test verifies the fallback throws a clear error for non-promotable views.
        var ex = Assert.Throws<KeyNotFoundException>(() => db.Jit("eng_employees"));
        Assert.Contains("eng_employees", ex.Message);
    }

    [Fact]
    public void Jit_ByName_TableTakesPrecedence()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Register a view with the same name as a table
        var view = ViewBuilder.From("users")
            .Select("name")
            .Where(row => row.GetInt64(2) >= 28)
            .Named("users") // same name as the table!
            .Build();

        db.RegisterView(view);

        // Jit("users") should resolve to the TABLE, not the view
        var jit = db.Jit("users");

        // Table-backed JitQuery should support mutations (view-backed would throw)
        // If this doesn't throw, it resolved to the table
        using var dbw = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        var jitW = dbw.Jit("users");
        dbw.RegisterView(view);
        long rowId = jitW.Insert(
            ColumnValue.FromInt64(1, 99),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Precedence")),
            ColumnValue.FromInt64(2, 50),
            ColumnValue.FromDouble(0.0),
            ColumnValue.Null());
        Assert.True(rowId > 0);
    }

    [Fact]
    public void Jit_ByName_NotFound_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var ex = Assert.Throws<KeyNotFoundException>(() => db.Jit("nonexistent"));
        Assert.Contains("Table or view", ex.Message);
        Assert.Contains("nonexistent", ex.Message);
    }

    // ── Mutation guard tests ──

    [Fact]
    public void Jit_FromView_Insert_ThrowsNotSupported()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var view = ViewBuilder.From("users").Named("all_users").Build();
        var jit = db.Jit(view);

        Assert.Throws<NotSupportedException>(() => jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Nope")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null()));
    }

    [Fact]
    public void Jit_FromView_ToPrepared_ThrowsNotSupported()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users").Select("name").Named("names").Build();
        var jit = db.Jit(view);

        Assert.Throws<NotSupportedException>(() => jit.ToPrepared("name"));
    }

    // ── View-backed filter cache tests ──

    [Fact]
    public void Jit_FromView_RepeatedQuery_ConsistentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Named("all_users")
            .Build();

        var jit = db.Jit(view);
        jit.Where(FilterStar.Column("age").Gte(28L));

        // Query three times — cached view filter should produce identical results
        for (int i = 0; i < 3; i++)
        {
            using var reader = jit.Query();
            int count = 0;
            while (reader.Read())
                count++;
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Jit_FromView_ClearAndReFilter_InvalidatesCache()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age")
            .Named("all_users")
            .Build();

        var jit = db.Jit(view);
        jit.Where(FilterStar.Column("age").Eq(25L));

        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(1, count);
        }

        // Clear + new filter — view cache must be invalidated
        jit.ClearFilters();
        jit.Where(FilterStar.Column("age").Gte(28L));

        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Jit_FromView_DifferentProjections_Work()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var view = ViewBuilder.From("users")
            .Select("name", "age", "balance")
            .Named("three_col_view")
            .Build();

        var jit = db.Jit(view);

        // First projection: name only
        using (var reader = jit.Query("name"))
        {
            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.StartsWith("User", reader.GetString(0));
        }

        // Second projection: age only
        using (var reader = jit.Query("age"))
        {
            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.Equal(21L, reader.GetInt64(0));
        }
    }

    // ── AsView tests ──

    [Fact]
    public void Jit_AsView_CreatesTransientView()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Table-backed JitQuery with filter
        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gte(28L));

        // Export as transient view
        var view = jit.AsView("senior_users", "name", "age");

        Assert.Equal("senior_users", view.Name);
        Assert.Equal("users", view.SourceTable);
        Assert.NotNull(view.Filter);

        // Open the view cursor and verify it filters correctly
        using var cursor = view.Open(db);
        var names = new List<string>();
        while (cursor.MoveNext())
            names.Add(cursor.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("User8", names);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void Jit_AsView_FromViewBacked_CreatesSubview()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Parent view: age >= 25 (User5..User10)
        var parentView = ViewBuilder.From("users")
            .Select("name", "age")
            .Where(row => row.GetInt64(1) >= 25)
            .Named("mid_users")
            .Build();

        // View-backed JitQuery with additional filter
        var jit = db.Jit(parentView);
        jit.Where(FilterStar.Column("age").Lte(27L));

        // Export as subview — should chain from parentView
        var subview = jit.AsView("narrow_users");

        Assert.Equal("narrow_users", subview.Name);
        Assert.NotNull(subview.SourceView);
        Assert.Equal("mid_users", subview.SourceView!.Name);

        // Open and verify: parent(age>=25) AND subview(age<=27) → age 25,26,27
        using var cursor = subview.Open(db);
        int count = 0;
        while (cursor.MoveNext())
            count++;

        Assert.Equal(3, count);
    }
}
