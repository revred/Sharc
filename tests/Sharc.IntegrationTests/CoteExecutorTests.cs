// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests for CoteExecutor: materialization, predicate evaluation on Cote results,
/// aggregation, DISTINCT, ORDER BY + LIMIT, parameters, and edge cases.
/// </summary>
public class CoteExecutorTests
{
    /// <summary>
    /// Creates a database with a people table for Cote testing.
    /// people: Alice(25,Engineering), Bob(30,Sales), Carol(28,Engineering),
    ///         Dave(35,Sales), Eve(22,Marketing), Frank(40,Engineering)
    /// </summary>
    private static byte[] CreateCoteTestDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT, age INTEGER, dept TEXT)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (1, 'Alice', 25, 'Engineering')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (2, 'Bob', 30, 'Sales')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (3, 'Carol', 28, 'Engineering')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (4, 'Dave', 35, 'Sales')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (5, 'Eve', 22, 'Marketing')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO people VALUES (6, 'Frank', 40, 'Engineering')");
        });
    }

    // ─── Basic Cote materialization ─────────────────────────────────

    [Fact]
    public void Query_CoteSelectAll_ReturnsAllCoteRows()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH eng AS (SELECT name, age FROM people WHERE dept = 'Engineering') SELECT * FROM eng");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Carol", names);
        Assert.Contains("Frank", names);
    }

    [Fact]
    public void Query_CoteWithOuterWhere_FiltersCoteResult()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH eng AS (SELECT name, age FROM people WHERE dept = 'Engineering') " +
            "SELECT name FROM eng WHERE age > 26");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Engineering: Alice(25), Carol(28), Frank(40) → age > 26: Carol, Frank
        Assert.Equal(2, names.Count);
        Assert.Contains("Carol", names);
        Assert.Contains("Frank", names);
    }

    // ─── ORDER BY + LIMIT on Cote ──────────────────────────────────

    [Fact]
    public void Query_CoteWithOrderBy_SortsMaterializedResult()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT name, age FROM people) " +
            "SELECT * FROM all_people ORDER BY age DESC");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(6, names.Count);
        Assert.Equal("Frank", names[0]);  // age 40
        Assert.Equal("Dave", names[1]);   // age 35
    }

    [Fact]
    public void Query_CoteWithOrderByAndLimit_ReturnsTopN()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT name, age FROM people) " +
            "SELECT * FROM all_people ORDER BY age DESC LIMIT 3");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Top 3 by age DESC: Frank(40), Dave(35), Bob(30)
        Assert.Equal(3, names.Count);
        Assert.Equal("Frank", names[0]);
        Assert.Equal("Dave", names[1]);
        Assert.Equal("Bob", names[2]);
    }

    [Fact]
    public void Query_CoteWithLimitAndOffset_SlicesCoteResult()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT name, age FROM people) " +
            "SELECT * FROM all_people ORDER BY name LIMIT 2 OFFSET 2");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Sorted by name: Alice, Bob, Carol, Dave, Eve, Frank → skip 2, take 2 = Carol, Dave
        Assert.Equal(2, names.Count);
        Assert.Equal("Carol", names[0]);
        Assert.Equal("Dave", names[1]);
    }

    // ─── Aggregation on Cote results ───────────────────────────────

    [Fact]
    public void Query_CoteWithGroupBy_AggregatesCoteRows()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT name, age, dept FROM people) " +
            "SELECT dept, COUNT(*) AS cnt FROM all_people GROUP BY dept ORDER BY dept");

        var results = new List<(string dept, long count)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetInt64(1)));

        Assert.Equal(3, results.Count);
        Assert.Equal(("Engineering", 3), results[0]);
        Assert.Equal(("Marketing", 1), results[1]);
        Assert.Equal(("Sales", 2), results[2]);
    }

    // ─── DISTINCT on Cote results ──────────────────────────────────

    [Fact]
    public void Query_CoteWithDistinct_DeduplicatesResults()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT dept FROM people) " +
            "SELECT DISTINCT dept FROM all_people ORDER BY dept");

        var depts = new List<string>();
        while (reader.Read())
            depts.Add(reader.GetString(0));

        Assert.Equal(3, depts.Count);
        Assert.Equal("Engineering", depts[0]);
        Assert.Equal("Marketing", depts[1]);
        Assert.Equal("Sales", depts[2]);
    }

    // ─── Parameters in Cote queries ────────────────────────────────

    [Fact]
    public void Query_CoteWithParameters_BindsInCoteAndOuter()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var parameters = new Dictionary<string, object> { ["minAge"] = 28L };
        using var reader = db.Query(parameters,
            "WITH seniors AS (SELECT name, age FROM people WHERE age >= $minAge) " +
            "SELECT * FROM seniors ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // age >= 28: Bob(30), Carol(28), Dave(35), Frank(40)
        Assert.Equal(4, names.Count);
        Assert.Equal("Bob", names[0]);
        Assert.Equal("Carol", names[1]);
        Assert.Equal("Dave", names[2]);
        Assert.Equal("Frank", names[3]);
    }

    // ─── Cote used in compound query ───────────────────────────────

    [Fact]
    public void Query_CoteInUnionAll_CombinesCoteWithTable()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE team_a (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE team_b (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO team_a VALUES (1, 'Alice', 25)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_a VALUES (2, 'Bob', 30)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_b VALUES (1, 'Carol', 28)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_b VALUES (2, 'Dave', 35)");
        });
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH young AS (SELECT name FROM team_a WHERE age < 30) " +
            "SELECT name FROM young UNION ALL SELECT name FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // young: Alice(25); team_b: Carol, Dave → UNION ALL = 3
        Assert.Equal(3, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Carol", names);
        Assert.Contains("Dave", names);
    }

    // ─── Empty Cote result ─────────────────────────────────────────

    [Fact]
    public void Query_CoteReturnsEmpty_OuterQueryReturnsEmpty()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH nobody AS (SELECT name FROM people WHERE age > 100) " +
            "SELECT * FROM nobody");

        int count = 0;
        while (reader.Read()) count++;

        Assert.Equal(0, count);
    }

    // ─── Column projection through Cote ────────────────────────────

    [Fact]
    public void Query_CoteWithColumnProjection_SelectsSpecificColumns()
    {
        var data = CreateCoteTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH eng AS (SELECT id, name, age FROM people WHERE dept = 'Engineering') " +
            "SELECT name FROM eng ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Carol", names[1]);
        Assert.Equal("Frank", names[2]);
    }

    // ─── Cote with NULL values ─────────────────────────────────────

    [Fact]
    public void Query_CoteWithNulls_HandlesNullValues()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, value INTEGER)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (1, 'A', 10)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (2, 'B', NULL)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO items VALUES (3, 'C', 30)");
        });
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_items AS (SELECT name, value FROM items) " +
            "SELECT * FROM all_items ORDER BY name");

        int count = 0;
        bool foundNull = false;
        while (reader.Read())
        {
            count++;
            if (reader.GetString(0) == "B" && reader.IsNull(1))
                foundNull = true;
        }

        Assert.Equal(3, count);
        Assert.True(foundNull);
    }
}
