// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using Sharc.Views;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests for the three P0 fixes identified in the view feature review:
///   1. NULL handling in PreMaterializeFilteredViews
///   2. Quoted identifiers in BuildViewCoteSql
///   3. EvalLeaf fallback defensiveness
/// </summary>
public sealed class ViewP0FixTests : IDisposable
{
    private readonly string _dbPath;

    public ViewP0FixTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_p0_{Guid.NewGuid()}.db");
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
            CREATE TABLE employees (
                id INTEGER PRIMARY KEY,
                name TEXT,
                age INTEGER,
                salary REAL,
                dept TEXT
            );
            INSERT INTO employees VALUES (1, 'Alice',   30, 75000.50, 'eng');
            INSERT INTO employees VALUES (2, 'Bob',     25, 60000.00, 'sales');
            INSERT INTO employees VALUES (3, NULL,      NULL, NULL,    'eng');
            INSERT INTO employees VALUES (4, 'Diana',   22, 55000.00, NULL);
            INSERT INTO employees VALUES (5, 'Eve',     28, NULL,     'eng');

            CREATE TABLE [has spaces] (
                id INTEGER PRIMARY KEY,
                [first name] TEXT,
                [last name] TEXT,
                [hire date] TEXT
            );
            INSERT INTO [has spaces] VALUES (1, 'Alice', 'Smith', '2020-01-15');
            INSERT INTO [has spaces] VALUES (2, 'Bob',   'Jones', '2021-06-01');
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    // ─── P0-1: NULL handling in PreMaterializeFilteredViews ──────────

    [Fact]
    public void Query_FilteredView_NullValues_PreservedAsNull()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Filtered view: only 'eng' dept — includes row 3 (NULLs) and row 5 (NULL salary).
        // Columns are [name, salary, dept] so ordinals match what the test reads.
        var view = ViewBuilder
            .From("employees")
            .Select("name", "salary", "dept")
            .Where(row => row.GetString(2) == "eng") // dept is column 2
            .Named("v_eng")
            .Build();
        db.RegisterView(view);

        // Read all rows. Cote returns all 3 columns: [name, salary, dept]
        // Don't assume sort order — just check null patterns across all rows.
        using var reader = db.Query("SELECT * FROM v_eng");
        var rows = new List<(bool NameIsNull, bool SalaryIsNull)>();
        while (reader.Read())
        {
            rows.Add((reader.IsNull(0), reader.IsNull(1)));
        }

        // 3 eng employees: Alice, row 3 (NULLs), Eve
        Assert.Equal(3, rows.Count);

        // Check NULL patterns without depending on sort order
        int bothNull = 0, neitherNull = 0, salaryOnlyNull = 0;
        foreach (var (nameIsNull, salaryIsNull) in rows)
        {
            if (nameIsNull && salaryIsNull) bothNull++;        // row 3: name=NULL, salary=NULL
            else if (!nameIsNull && !salaryIsNull) neitherNull++; // Alice: name="Alice", salary=75000.50
            else if (!nameIsNull && salaryIsNull) salaryOnlyNull++; // Eve: name="Eve", salary=NULL
        }

        Assert.Equal(1, bothNull);        // row 3
        Assert.Equal(1, neitherNull);     // Alice
        Assert.Equal(1, salaryOnlyNull);  // Eve
    }

    [Fact]
    public void Query_FilteredView_NullIntColumn_NotCoercedToZero()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Filtered view exposing rows with potentially NULL age
        var view = ViewBuilder
            .From("employees")
            .Select("id", "name", "age")
            .Where(row => !row.IsNull(0)) // all rows (filter on id NOT NULL)
            .Named("v_all")
            .Build();
        db.RegisterView(view);

        // Query for rows where age IS NULL — should find row 3
        using var reader = db.Query("SELECT id FROM v_all WHERE age IS NULL");
        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Single(ids);
        Assert.Equal(3L, ids[0]);
    }

    [Fact]
    public void Query_FilteredView_NullRealColumn_NotCoercedToZero()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("employees")
            .Select("id", "name", "salary")
            .Where(row => !row.IsNull(0)) // all rows
            .Named("v_salaries")
            .Build();
        db.RegisterView(view);

        // Rows with NULL salary: id=3 and id=5
        using var reader = db.Query("SELECT id FROM v_salaries WHERE salary IS NULL ORDER BY id");
        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Equal(3L, ids[0]);
        Assert.Equal(5L, ids[1]);
    }

    // ─── P0-2: Quoted identifiers in BuildViewCoteSql ────────────────

    [Fact]
    public void Query_RegisteredView_ColumnNameWithSpaces()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("has spaces")
            .Select("first name", "last name")
            .Named("v_names")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT * FROM v_names ORDER BY [first name]");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
    }

    [Fact]
    public void Query_RegisteredView_TableNameWithSpaces()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // View from table with spaces in its name, no projection (SELECT *)
        var view = ViewBuilder
            .From("has spaces")
            .Named("v_spaced")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT * FROM v_spaced");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(2, count);
    }

    // ─── P0-3: EvalLeaf defensive fallback ───────────────────────────

    [Fact]
    public void Query_RegisteredView_NeqOnDoubleColumn()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Columns: [name, salary] — name at ordinal 0 for correct projection
        var view = ViewBuilder
            .From("employees")
            .Select("name", "salary")
            .Named("v_sal")
            .Build();
        db.RegisterView(view);

        // NEQ on REAL column — exercises the (Neq, Real, Double) EvalLeaf case
        using var reader = db.Query("SELECT name FROM v_sal WHERE salary != 60000.0");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Alice (75000.50) and Diana (55000.00) pass; Bob (60000.00) filtered out
        // Row 3 and Eve have NULL salary — NULL != X is false in SQL
        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Diana", names);
    }

    [Fact]
    public void Query_RegisteredView_NeqCrossType_IntColumnDoubleFilter()
    {
        using var db = SharcDatabase.Open(_dbPath);

        // Columns: [name, age] — name at ordinal 0 for correct projection
        var view = ViewBuilder
            .From("employees")
            .Select("name", "age")
            .Named("v_ages")
            .Build();
        db.RegisterView(view);

        // NEQ with a double literal against an integer column
        using var reader = db.Query("SELECT name FROM v_ages WHERE age != 25.0 ORDER BY name");
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Alice(30), Diana(22), Eve(28) pass; Bob(25) filtered out; row 3 NULL
        Assert.Equal(3, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Diana", names);
        Assert.Contains("Eve", names);
    }

    // ─── Task 3: NULL materialization gap coverage ─────────────────

    [Fact]
    public void Query_RegisteredView_NullInIntegerColumn_ReturnsNull()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("employees")
            .Select("id", "age")
            .Where(row => row.GetInt64(0) == 3) // row with NULL age
            .Named("v_null_int")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT * FROM v_null_int");
        Assert.True(reader.Read());
        Assert.True(reader.IsNull(1)); // age should be NULL, not 0
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_RegisteredView_NullInTextColumn_ReturnsNull()
    {
        using var db = SharcDatabase.Open(_dbPath);

        var view = ViewBuilder
            .From("employees")
            .Select("id", "name")
            .Where(row => row.GetInt64(0) == 3) // row with NULL name
            .Named("v_null_text")
            .Build();
        db.RegisterView(view);

        using var reader = db.Query("SELECT * FROM v_null_text");
        Assert.True(reader.Read());
        Assert.True(reader.IsNull(1)); // name should be NULL, not ""
        Assert.False(reader.Read());
    }
}
