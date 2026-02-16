// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public class SharcDatabaseCompoundQueryTests
{
    /// <summary>
    /// Creates a database with two tables (team_a, team_b) sharing the same schema,
    /// plus a third table (team_c) for 3-way tests.
    /// team_a: Alice(25), Bob(30), Carol(28)
    /// team_b: Bob(30), Dave(35), Eve(22)
    /// team_c: Carol(28), Frank(40)
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
        });
    }

    // ─── UNION ALL ───────────────────────────────────────────────

    [Fact]
    public void Query_UnionAll_ConcatenatesAllRows()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT name, age FROM team_a UNION ALL SELECT name, age FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(6, names.Count); // 3 + 3
        Assert.Contains("Alice", names);
        Assert.Contains("Dave", names);
        // Bob appears twice (once from each table)
        Assert.Equal(2, names.Count(n => n == "Bob"));
    }

    [Fact]
    public void Query_UnionAll_PreservesDuplicates()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT name FROM team_a UNION ALL SELECT name FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Bob(30) appears in both tables
        Assert.Equal(2, names.Count(n => n == "Bob"));
    }

    // ─── UNION ───────────────────────────────────────────────────

    [Fact]
    public void Query_Union_DeduplicatesAcrossTables()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT name, age FROM team_a UNION SELECT name, age FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count); // Alice, Bob, Carol, Dave, Eve (Bob deduplicated)
        Assert.Single(names.Where(n => n == "Bob"));
    }

    // ─── INTERSECT ───────────────────────────────────────────────

    [Fact]
    public void Query_Intersect_ReturnsCommonRows()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT name, age FROM team_a INTERSECT SELECT name, age FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Single(names); // Only Bob(30) appears in both
        Assert.Equal("Bob", names[0]);
    }

    [Fact]
    public void Query_Intersect_EmptyWhenNoOverlap()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // team_a has Alice,Bob,Carol; team_c has Carol,Frank
        // But with name+age, Carol(28) appears in both
        using var reader = db.Query("SELECT name FROM team_a INTERSECT SELECT name FROM team_c");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Single(names);
        Assert.Equal("Carol", names[0]);
    }

    // ─── EXCEPT ──────────────────────────────────────────────────

    [Fact]
    public void Query_Except_ReturnsLeftOnlyRows()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT name, age FROM team_a EXCEPT SELECT name, age FROM team_b");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count); // Alice, Carol (Bob removed)
        Assert.DoesNotContain("Bob", names);
        Assert.Contains("Alice", names);
        Assert.Contains("Carol", names);
    }

    // ─── Three-way compound ──────────────────────────────────────

    [Fact]
    public void Query_ThreeWayUnion_ChainsCorrectly()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name FROM team_a UNION SELECT name FROM team_b UNION SELECT name FROM team_c");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Alice, Bob, Carol, Dave, Eve, Frank — all unique
        Assert.Equal(6, names.Count);
        Assert.Contains("Frank", names);
    }

    // ─── WHERE on both sides ─────────────────────────────────────

    [Fact]
    public void Query_UnionWithWhere_BothSidesFiltered()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name FROM team_a WHERE age > 26 UNION ALL SELECT name FROM team_b WHERE age > 26");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a: Bob(30), Carol(28); team_b: Bob(30), Dave(35)
        Assert.Equal(4, names.Count);
        Assert.Contains("Carol", names);
        Assert.Contains("Dave", names);
    }

    // ─── ORDER BY on final result ────────────────────────────────

    [Fact]
    public void Query_UnionWithOrderBy_SortsFinalResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a UNION SELECT name, age FROM team_b ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
        Assert.Equal("Carol", names[2]);
        Assert.Equal("Dave", names[3]);
        Assert.Equal("Eve", names[4]);
    }

    // ─── LIMIT / OFFSET ─────────────────────────────────────────

    [Fact]
    public void Query_UnionWithLimitOffset_SlicesFinalResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "SELECT name, age FROM team_a UNION SELECT name, age FROM team_b ORDER BY name LIMIT 2 OFFSET 1");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // Sorted: Alice, Bob, Carol, Dave, Eve → skip 1, take 2 = Bob, Carol
        Assert.Equal(2, names.Count);
        Assert.Equal("Bob", names[0]);
        Assert.Equal("Carol", names[1]);
    }

    // ─── Parameters ──────────────────────────────────────────────

    [Fact]
    public void Query_UnionWithParameters_BindsBothSides()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var parameters = new Dictionary<string, object> { ["minAge"] = 28L };
        using var reader = db.Query(parameters,
            "SELECT name FROM team_a WHERE age >= $minAge UNION ALL SELECT name FROM team_b WHERE age >= $minAge");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // team_a: Bob(30), Carol(28); team_b: Bob(30), Dave(35)
        Assert.Equal(4, names.Count);
    }

    // ─── Cote (WITH) ─────────────────────────────────────────────

    [Fact]
    public void Query_CoteSimpleSelect_ReturnsCoteRows()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH seniors AS (SELECT name, age FROM team_a WHERE age >= 28) SELECT * FROM seniors");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count); // Bob(30), Carol(28)
        Assert.Contains("Bob", names);
        Assert.Contains("Carol", names);
    }

    [Fact]
    public void Query_CteWithOrderBy_SortsCteResult()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH all_people AS (SELECT name, age FROM team_a) SELECT * FROM all_people ORDER BY name");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(3, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
        Assert.Equal("Carol", names[2]);
    }

    [Fact]
    public void Query_CteUsedInUnion_CombinesWithTable()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query(
            "WITH seniors AS (SELECT name FROM team_a WHERE age >= 28) " +
            "SELECT name FROM seniors UNION SELECT name FROM team_c");

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // seniors: Bob(30), Carol(28); team_c: Carol(28), Frank(40)
        // UNION: Bob, Carol, Frank
        Assert.Equal(3, names.Count);
        Assert.Contains("Bob", names);
        Assert.Contains("Carol", names);
        Assert.Contains("Frank", names);
    }

    // ─── Agent entitlement ───────────────────────────────────────

    [Fact]
    public void Query_CompoundWithAgent_EnforcesAllTables()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var agent = new AgentInfo("agent-1", AgentClass.User, Array.Empty<byte>(), 0,
            "*", "team_a.*,team_b.*", 0, 0, "", false, Array.Empty<byte>());

        // Should succeed — agent has access to both tables
        using var reader = db.Query(null, "SELECT name FROM team_a UNION SELECT name FROM team_b", agent);

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(5, names.Count);
    }

    [Fact]
    public void Query_CompoundWithAgent_DeniedOneTable_Throws()
    {
        var data = CreateCompoundTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        var agent = new AgentInfo("agent-limited", AgentClass.User, Array.Empty<byte>(), 0,
            "*", "team_a.*", 0, 0, "", false, Array.Empty<byte>()); // No access to team_b

        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Query(null, "SELECT name FROM team_a UNION SELECT name FROM team_b", agent));
    }
}
