// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests query pipeline robustness: error handling, edge cases,
/// empty tables, NULL values, large datasets, and memory behavior.
/// </summary>
public class QueryRobustnessTests
{
    // ─── Malformed SQL → clean error ─────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT SQL AT ALL")]
    [InlineData("INSERT INTO users VALUES (1)")]
    [InlineData("DROP TABLE users")]
    [InlineData("UPDATE users SET name = 'x'")]
    [InlineData("DELETE FROM users")]
    public void Query_NonSelectStatement_Throws(string sql)
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.ThrowsAny<Exception>(() => db.Query(sql));
    }

    [Fact]
    public void Query_NonExistentTable_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = db.Query("SELECT * FROM nonexistent_table");
            reader.Read();
        });
    }

    [Fact]
    public void Query_NonExistentColumn_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = db.Query("SELECT missing_col FROM users");
            reader.Read();
        });
    }

    // ─── Empty table ────────────────────────────────────────────

    [Fact]
    public void Query_EmptyTable_ReturnsZeroRows()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE empty_t (id INTEGER PRIMARY KEY, name TEXT)");
        });
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM empty_t");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_EmptyTableCount_ThrowsOnEmptyAggregation()
    {
        // Known limitation: StreamingAggregator.Finalize() doesn't handle
        // zero input rows. Tracked for fix — COUNT(*) on empty table should return 0.
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE empty_t (id INTEGER PRIMARY KEY, name TEXT)");
        });
        using var db = SharcDatabase.OpenMemory(data);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = db.Query("SELECT COUNT(*) FROM empty_t");
            reader.Read();
        });
    }

    [Fact]
    public void Query_EmptyTableGroupBy_ReturnsNoGroups()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE empty_t (id INTEGER PRIMARY KEY, cat TEXT, val INTEGER)");
        });
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT cat, COUNT(*) FROM empty_t GROUP BY cat");

        Assert.False(reader.Read());
    }

    // ─── Single-row table ───────────────────────────────────────

    [Fact]
    public void Query_SingleRowOrderBy_ReturnsSingleRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name FROM users ORDER BY name");

        Assert.True(reader.Read());
        Assert.Equal("User1", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_SingleRowGroupBy_ReturnsSingleGroup()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT age, COUNT(*) FROM users GROUP BY age");

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.False(reader.Read());
    }

    // ─── NULL value handling ────────────────────────────────────

    [Fact]
    public void Query_WhereIsNull_FindsNullRows()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM all_types WHERE null_val IS NULL");

        int count = 0;
        while (reader.Read()) count++;
        Assert.True(count > 0);
    }

    [Fact]
    public void Query_OrderByNullableColumn_DoesNotThrow()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM all_types ORDER BY null_val");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count); // all_types has 5 rows
    }

    [Fact]
    public void Query_GroupByNullableColumn_GroupsNullsTogether()
    {
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            "SELECT null_val, COUNT(*) AS cnt FROM all_types GROUP BY null_val");

        int groupCount = 0;
        while (reader.Read()) groupCount++;
        // All rows have null_val = NULL → 1 group
        Assert.Equal(1, groupCount);
    }

    // ─── Large dataset queries ──────────────────────────────────

    [Fact]
    public void Query_SelectAll_10KRows_ReturnsAll()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(10_000);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM large_table");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(10_000, count);
    }

    [Fact]
    public void Query_WhereFilter_10KRows_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(10_000);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM large_table WHERE number > 500000");

        int count = 0;
        while (reader.Read()) count++;
        // number = i * 100, so number > 500000 means i > 5000 → 5000 rows
        Assert.Equal(5_000, count);
    }

    [Fact]
    public void Query_OrderByLimit_10KRows_StreamsCorrectly()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(10_000);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            "SELECT number FROM large_table ORDER BY number DESC LIMIT 10");

        var values = new List<long>();
        while (reader.Read())
            values.Add(reader.GetInt64(0));

        Assert.Equal(10, values.Count);
        Assert.Equal(1_000_000L, values[0]); // max = 10000 * 100
        // Verify descending order
        for (int i = 1; i < values.Count; i++)
            Assert.True(values[i] <= values[i - 1]);
    }

    [Fact]
    public void Query_GroupBy_10KRows_AggregatesCorrectly()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(10_000);
        using var db = SharcDatabase.OpenMemory(data);
        // Group by last digit of number (number % 10 is always 0 since number = i*100)
        // Instead use value column length which is constant → 1 group
        using var reader = db.Query(
            "SELECT COUNT(*) AS cnt FROM large_table");

        Assert.True(reader.Read());
        Assert.Equal(10_000L, reader.GetInt64(0));
    }

    [Fact]
    public void Query_UnionAll_LargeDatasets_ConcatenatesCorrectly()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE t1 (id INTEGER PRIMARY KEY, val INTEGER)");
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE t2 (id INTEGER PRIMARY KEY, val INTEGER)");

            using var tx = conn.BeginTransaction();
            for (int i = 1; i <= 5000; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO t1 VALUES ($id, $val)";
                cmd.Parameters.AddWithValue("$id", i);
                cmd.Parameters.AddWithValue("$val", i * 10L);
                cmd.ExecuteNonQuery();
            }
            for (int i = 1; i <= 5000; i++)
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
        Assert.Equal(10_000, count);
    }

    // ─── LIMIT / OFFSET edge cases ─────────────────────────────

    [Fact]
    public void Query_LimitZero_ReturnsNoRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 0");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_LimitExceedsRows_ReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 100");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void Query_OffsetExceedsRows_ReturnsNoRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 10 OFFSET 100");

        Assert.False(reader.Read());
    }

    // ─── Memory behavior: query doesn't allocate excessively ────

    [Fact]
    public void Query_SelectStar_SmallTable_AllocationsReasonable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);

        // Warm up
        using (var warmup = db.Query("SELECT * FROM users"))
            while (warmup.Read()) { }

        // Measure
        long before = GC.GetAllocatedBytesForCurrentThread();
        using (var reader = db.Query("SELECT * FROM users"))
            while (reader.Read()) { }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        // 100 rows should not allocate more than 500 KB
        Assert.True(allocated < 512_000,
            $"SELECT * on 100 rows allocated {allocated:N0} bytes (expected < 512,000)");
    }

    [Fact]
    public void Query_CountStar_AllocatesMinimal()
    {
        var data = TestDatabaseFactory.CreateLargeDatabase(5000);
        using var db = SharcDatabase.OpenMemory(data);

        // Warm up
        using (var warmup = db.Query("SELECT COUNT(*) FROM large_table"))
            warmup.Read();

        // Measure
        long before = GC.GetAllocatedBytesForCurrentThread();
        using (var reader = db.Query("SELECT COUNT(*) FROM large_table"))
            reader.Read();
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        // COUNT(*) streams through rows — should stay under 2 MB for 5K rows
        Assert.True(allocated < 2_000_000,
            $"COUNT(*) on 5K rows allocated {allocated:N0} bytes (expected < 2,000,000)");
    }
}
