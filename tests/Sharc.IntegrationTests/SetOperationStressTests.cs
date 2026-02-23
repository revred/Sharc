// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for set operations (UNION, UNION ALL, INTERSECT, EXCEPT) — verifies
/// dedup correctness with large result sets, NULL handling, empty branches, and
/// mixed materialized + cursor-backed fingerprinting.
/// </summary>
public sealed class SetOperationStressTests
{
    private static byte[] CreateSetTestDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE alpha (id INTEGER PRIMARY KEY, name TEXT, score INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE beta (id INTEGER PRIMARY KEY, name TEXT, score INTEGER)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE gamma (id INTEGER PRIMARY KEY, name TEXT, score INTEGER)");

            // alpha: 100 rows (names: A_001 to A_100)
            for (int i = 1; i <= 100; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO alpha VALUES ($id, $name, $score)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$name", $"A_{i:D3}");
                cmd.Parameters.AddWithValue("$score", i * 10);
                cmd.ExecuteNonQuery();
            }

            // beta: 100 rows, overlapping names A_051..A_100 + B_001..B_050
            for (int i = 1; i <= 100; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO beta VALUES ($id, $name, $score)";
                cmd.Parameters.AddWithValue("$id", i);
                string name = i <= 50 ? $"A_{i + 50:D3}" : $"B_{i - 50:D3}";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$score", i * 10);
                cmd.ExecuteNonQuery();
            }

            // gamma: empty table
            // (no inserts)
        });
    }

    private static byte[] CreateNullSetDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t1 (id INTEGER PRIMARY KEY, val TEXT)");
            TestDatabaseFactory.Execute(conn, "CREATE TABLE t2 (id INTEGER PRIMARY KEY, val TEXT)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO t1 VALUES (1, NULL)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO t1 VALUES (2, 'hello')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO t1 VALUES (3, NULL)");

            TestDatabaseFactory.Execute(conn, "INSERT INTO t2 VALUES (1, NULL)");
            TestDatabaseFactory.Execute(conn, "INSERT INTO t2 VALUES (2, 'hello')");
            TestDatabaseFactory.Execute(conn, "INSERT INTO t2 VALUES (3, 'world')");
        });
    }

    // ── UNION ALL ──

    [Fact]
    public void UnionAll_ConcatenatesAll_IncludingDuplicates()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name, score FROM alpha UNION ALL SELECT name, score FROM beta");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(200, count); // 100 + 100
    }

    [Fact]
    public void UnionAll_WithEmptyBranch_ReturnsOtherBranch()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name, score FROM alpha UNION ALL SELECT name, score FROM gamma");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(100, count); // 100 + 0
    }

    // ── UNION (deduplicated) ──

    [Fact]
    public void Union_DeduplicatesOverlappingRows()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM alpha UNION SELECT name FROM beta");

        var names = new HashSet<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        // alpha: A_001..A_100, beta: A_051..A_100 + B_001..B_050
        // Union: A_001..A_100 + B_001..B_050 = 150 unique names
        Assert.Equal(150, names.Count);
    }

    [Fact]
    public void Union_WithEmptyBranch_ReturnsNonEmptyBranch()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM gamma UNION SELECT name FROM alpha");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(100, count);
    }

    [Fact]
    public void Union_BothBranchesEmpty_ReturnsNothing()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM gamma UNION SELECT name FROM gamma");

        Assert.False(reader.Read());
    }

    // ── INTERSECT ──

    [Fact]
    public void Intersect_ReturnsOnlyCommonRows()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // alpha names A_051..A_100 overlap with beta's A_051..A_100
        // But scores differ (alpha: 510..1000, beta: 10..500)
        // So name-only intersect on common names
        using var reader = db.Query("SELECT name FROM alpha INTERSECT SELECT name FROM beta");

        var names = new HashSet<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(50, names.Count); // A_051..A_100
        Assert.Contains("A_051", names);
        Assert.Contains("A_100", names);
        Assert.DoesNotContain("A_001", names);
        Assert.DoesNotContain("B_001", names);
    }

    [Fact]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM alpha INTERSECT SELECT name FROM gamma");

        Assert.False(reader.Read());
    }

    // ── EXCEPT ──

    [Fact]
    public void Except_ReturnsLeftMinusRight()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // alpha names: A_001..A_100
        // beta names: A_051..A_100 + B_001..B_050
        // EXCEPT: A_001..A_050
        using var reader = db.Query("SELECT name FROM alpha EXCEPT SELECT name FROM beta");

        var names = new HashSet<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(50, names.Count);
        Assert.Contains("A_001", names);
        Assert.Contains("A_050", names);
        Assert.DoesNotContain("A_051", names);
        Assert.DoesNotContain("B_001", names);
    }

    [Fact]
    public void Except_NoOverlap_ReturnsAllLeft()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM alpha EXCEPT SELECT name FROM gamma");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(100, count);
    }

    [Fact]
    public void Except_CompleteOverlap_ReturnsEmpty()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM alpha EXCEPT SELECT name FROM alpha");

        Assert.False(reader.Read());
    }

    // ── NULL handling in set operations ──

    [Fact]
    public void Union_WithNulls_DeduplicatesNullRows()
    {
        var data = CreateNullSetDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // t1 has 2 NULLs, t2 has 1 NULL — UNION should dedup NULLs as equal
        using var reader = db.Query("SELECT val FROM t1 UNION SELECT val FROM t2");

        int nullCount = 0;
        int totalCount = 0;
        while (reader.Read())
        {
            totalCount++;
            if (reader.IsNull(0)) nullCount++;
        }

        // Unique values: NULL, 'hello', 'world' = 3
        Assert.Equal(3, totalCount);
        Assert.Equal(1, nullCount);
    }

    [Fact]
    public void Intersect_WithNulls_NullEqualsNull()
    {
        var data = CreateNullSetDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT val FROM t1 INTERSECT SELECT val FROM t2");

        // Common values: NULL and 'hello'
        int count = 0;
        bool hasNull = false;
        bool hasHello = false;
        while (reader.Read())
        {
            count++;
            if (reader.IsNull(0)) hasNull = true;
            else if (reader.GetString(0) == "hello") hasHello = true;
        }
        Assert.Equal(2, count);
        Assert.True(hasNull);
        Assert.True(hasHello);
    }

    [Fact]
    public void Except_WithNulls_RemovesMatchingNulls()
    {
        var data = CreateNullSetDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // t1 vals: NULL, 'hello', NULL
        // t2 vals: NULL, 'hello', 'world'
        // EXCEPT: nothing remains (NULL and 'hello' are in both)
        using var reader = db.Query("SELECT val FROM t1 EXCEPT SELECT val FROM t2");

        Assert.False(reader.Read());
    }

    // ── Large result sets ──

    [Fact]
    public void UnionAll_LargeResultSet_AllRowsReturned()
    {
        var data = CreateSetTestDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        // 3-way UNION ALL
        using var reader = db.Query(
            "SELECT name, score FROM alpha UNION ALL SELECT name, score FROM beta UNION ALL SELECT name, score FROM alpha");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(300, count); // 100 + 100 + 100
    }
}
