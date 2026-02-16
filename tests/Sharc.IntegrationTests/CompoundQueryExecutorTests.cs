// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests for CompoundQueryExecutor: streaming paths, chained UNION ALL,
/// index-based set ops, column mismatch errors, and ORDER BY + LIMIT combos.
/// </summary>
public class CompoundQueryExecutorTests
{
    /// <summary>
    /// Creates a database with three tables for compound query testing.
    /// team_a: Alice(25), Bob(30), Carol(28)
    /// team_b: Bob(30), Dave(35), Eve(22)
    /// team_c: Carol(28), Frank(40), Grace(31)
    /// </summary>
    private static byte[] CreateCompoundTestDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE team_a (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE team_b (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE team_c (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO team_a VALUES (1, 'Alice', 25)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_a VALUES (2, 'Bob', 30)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_a VALUES (3, 'Carol', 28)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO team_b VALUES (1, 'Bob', 30)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_b VALUES (2, 'Dave', 35)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_b VALUES (3, 'Eve', 22)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO team_c VALUES (1, 'Carol', 28)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_c VALUES (2, 'Frank', 40)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO team_c VALUES (3, 'Grace', 31)");
        });
    }

    // ─── Three-way UNION ALL (chained streaming path) ────────────

    [Fact]
    public void Query_ThreeWayUnionAll_ConcatenatesAllRows()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name FROM team_a UNION ALL SELECT name FROM team_b UNION ALL SELECT name FROM team_c");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(9, names.Count); // 3 + 3 + 3
        Assert.Contains("Alice", names);
        Assert.Contains("Dave", names);
        Assert.Contains("Frank", names);
        Assert.Contains("Grace", names);
    }

    [Fact]
    public void Query_ThreeWayUnionAll_PreservesAllDuplicates()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a UNION ALL SELECT name, age FROM team_b UNION ALL SELECT name, age FROM team_c");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Bob(30) in team_a and team_b, Carol(28) in team_a and team_c
        Assert.Equal(2, names.Count(n => n == "Bob"));
        Assert.Equal(2, names.Count(n => n == "Carol"));
    }

    // ─── UNION ALL + ORDER BY + LIMIT (streaming TopN path) ─────

    [Fact]
    public void Query_UnionAllOrderByLimit_StreamsTopN()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a UNION ALL SELECT name, age FROM team_b ORDER BY age DESC LIMIT 3");

        var results = new List<(string name, long age)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetInt64(1)));

        Assert.Equal(3, results.Count);
        Assert.Equal(35, results[0].age); // Dave
        Assert.Equal(30, results[1].age); // Bob (one of two)
        Assert.Equal(30, results[2].age); // Bob (one of two)
    }

    [Fact]
    public void Query_UnionAllOrderByLimitOffset_SkipsAndTakes()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a UNION ALL SELECT name, age FROM team_b ORDER BY name LIMIT 2 OFFSET 2");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // All 6 sorted by name: Alice, Bob, Bob, Carol, Dave, Eve → skip 2, take 2 = Bob, Carol
        Assert.Equal(2, names.Count);
        Assert.Equal("Bob", names[0]);
        Assert.Equal("Carol", names[1]);
    }

    // ─── EXCEPT + ORDER BY ──────────────────────────────────────

    [Fact]
    public void Query_ExceptWithOrderBy_SortsResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a EXCEPT SELECT name, age FROM team_b ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a - team_b = Alice(25), Carol(28), sorted
        Assert.Equal(2, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Carol", names[1]);
    }

    // ─── INTERSECT + ORDER BY ───────────────────────────────────

    [Fact]
    public void Query_IntersectWithOrderBy_SortsResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // team_a ∩ team_c (by name, age) = Carol(28)
        using var reader = db.Query(
            "SELECT name, age FROM team_a INTERSECT SELECT name, age FROM team_c ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Single(names);
        Assert.Equal("Carol", names[0]);
    }

    // ─── EXCEPT + LIMIT ─────────────────────────────────────────

    [Fact]
    public void Query_ExceptWithLimit_LimitsResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a EXCEPT SELECT name, age FROM team_b ORDER BY name LIMIT 1");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Single(names);
        Assert.Equal("Alice", names[0]);
    }

    // ─── WHERE filter on both sides of compound ─────────────────

    [Fact]
    public void Query_IntersectWithWhere_FiltersBothSides()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name FROM team_a WHERE age >= 28 INTERSECT SELECT name FROM team_b WHERE age >= 28");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a (age>=28): Bob, Carol; team_b (age>=28): Bob, Dave → intersect = Bob
        Assert.Single(names);
        Assert.Equal("Bob", names[0]);
    }

    [Fact]
    public void Query_ExceptWithWhere_FiltersAndSubtracts()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name FROM team_a WHERE age >= 25 EXCEPT SELECT name FROM team_b WHERE age >= 30");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a (age>=25): Alice, Bob, Carol; team_b (age>=30): Bob, Dave → except = Alice, Carol
        Assert.Equal(2, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Carol", names);
    }

    // ─── Large compound queries ─────────────────────────────────

    [Fact]
    public void Query_UnionAll_LargeTables_ConcatenatesAll()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t1 (id INTEGER PRIMARY KEY, val INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t2 (id INTEGER PRIMARY KEY, val INTEGER)");

            using var tx = conn.BeginTransaction();
            for (int i = 1; i <= 2000; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO t1 VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$val", i * 10L);
                cmd.ExecuteNonQuery();
            }
            for (int i = 1; i <= 3000; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO t2 VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$val", i * 20L);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        });
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT val FROM t1 UNION ALL SELECT val FROM t2");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5000, count);
    }

    [Fact]
    public void Query_Union_LargeTables_DeduplicatesCorrectly()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t1 (id INTEGER PRIMARY KEY, val INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t2 (id INTEGER PRIMARY KEY, val INTEGER)");

            using var tx = conn.BeginTransaction();
            // t1: 1..500, t2: 250..749 → overlap at 250..500 → union = 1..749 = 749 distinct values
            for (int i = 1; i <= 500; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO t1 VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$val", (long)i);
                cmd.ExecuteNonQuery();
            }
            for (int i = 1; i <= 500; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO t2 VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$val", (long)(i + 249));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        });
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT val FROM t1 UNION SELECT val FROM t2");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(749, count);
    }

    // ─── Compound with single-column SELECT ─────────────────────

    [Fact]
    public void Query_UnionAllSingleColumn_Works()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT age FROM team_a UNION ALL SELECT age FROM team_b");

        var ages = new List<long>();
        while (reader.Read())
            ages.Add(reader.GetInt64(0));

        Assert.Equal(6, ages.Count);
        Assert.Contains(25L, ages);
        Assert.Contains(22L, ages);
    }

    // ─── Compound with parameter binding ────────────────────────

    [Fact]
    public void Query_IntersectWithParameters_BindsBothSides()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var parameters = new Dictionary<string, object> { ["minAge"] = 28L };
        using var reader = db.Query(parameters,
            "SELECT name FROM team_a WHERE age >= $minAge INTERSECT SELECT name FROM team_b WHERE age >= $minAge");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a (>=28): Bob, Carol; team_b (>=28): Bob, Dave → intersect = Bob
        Assert.Single(names);
        Assert.Equal("Bob", names[0]);
    }
}
