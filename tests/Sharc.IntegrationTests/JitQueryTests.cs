// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Text;
using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class JitQueryTests
{
    // Users table: id INTEGER PRIMARY KEY, name TEXT, age INTEGER, balance REAL, avatar BLOB
    // Rows: User1 (age=21), User2 (age=22), ..., User10 (age=30)

    // ── Read Tests ──

    [Fact]
    public void Jit_Query_ReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Jit_Query_WithProjection_ReturnsColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        using var reader = jit.Query("name", "age");

        Assert.Equal(2, reader.FieldCount);
        Assert.True(reader.Read());
        // Column 0 should be name, column 1 should be age
        var name = reader.GetString(0);
        Assert.StartsWith("User", name);
    }

    [Fact]
    public void Jit_Query_WithFilter_ReturnsSubset()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Eq(25L));
        using var reader = jit.Query();

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1)); // name is ordinal 1
        Assert.False(reader.Read());
    }

    [Fact]
    public void Jit_Query_MultipleWhere_ChainsWithAnd()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gte(25L));
        jit.Where(FilterStar.Column("age").Lte(27L));
        using var reader = jit.Query();

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        // age 25, 26, 27 → User5, User6, User7
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void Jit_Query_WithLimit_CapsResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.WithLimit(3);
        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    [Fact]
    public void Jit_Query_WithOffset_SkipsRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.WithOffset(8);
        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        // 10 rows, skip 8 → 2 remain
        Assert.Equal(2, count);
    }

    [Fact]
    public void Jit_ClearFilters_ResetsToFullScan()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");

        // Add filter, then clear
        jit.Where(FilterStar.Column("age").Eq(25L));
        jit.ClearFilters();

        using var reader = jit.Query();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    // ── Mutation Tests ──

    [Fact]
    public void Jit_Insert_ReturnsRowId()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var jit = db.Jit("users");
        long rowId = jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        Assert.True(rowId > 0);

        // Verify the row exists
        using var reader = jit.Query();
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Jit_Delete_RemovesRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var jit = db.Jit("users");
        long rowId = jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        bool found = jit.Delete(rowId);
        Assert.True(found);

        // Verify the row is gone
        using var reader = jit.Query();
        Assert.False(reader.Read());
    }

    [Fact]
    public void Jit_Update_ModifiesRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var jit = db.Jit("users");
        long rowId = jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        bool updated = jit.Update(rowId,
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(23, Encoding.UTF8.GetBytes("Bob")),
            ColumnValue.FromInt64(2, 40),
            ColumnValue.FromDouble(200.0),
            ColumnValue.Null());

        Assert.True(updated);

        // Verify updated values
        using var reader = jit.Query();
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(1));
        Assert.Equal(40L, reader.GetInt64(2));
        Assert.False(reader.Read());
    }

    // ── Transaction Tests ──

    [Fact]
    public void Jit_WithTransaction_BatchInsert_Commits()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var jit = db.Jit("users");

        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        jit.WithTransaction(tx);

        jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        jit.Insert(
            ColumnValue.FromInt64(1, 2),
            ColumnValue.Text(23, Encoding.UTF8.GetBytes("Bob")),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(50.0),
            ColumnValue.Null());

        tx.Commit();
        jit.DetachTransaction();

        // Verify both rows exist after commit
        using var reader = jit.Query();
        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(2, count);
    }

    [Fact]
    public void Jit_WithTransaction_RollbackDiscardsChanges()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });

        var jit = db.Jit("users");

        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();
        jit.WithTransaction(tx);

        jit.Insert(
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        tx.Rollback();
        jit.DetachTransaction();

        // Verify no rows after rollback
        using var reader = jit.Query();
        Assert.False(reader.Read());
    }

    // ── Lifecycle Tests ──

    [Fact]
    public void Jit_ToPrepared_ProducesSameResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gte(25L));

        // Collect JitQuery results
        var jitNames = new List<string>();
        using (var reader = jit.Query("name"))
        {
            while (reader.Read())
                jitNames.Add(reader.GetString(0));
        }

        // Freeze into PreparedQuery
        using var prepared = jit.ToPrepared("name");
        var preparedNames = new List<string>();
        using (var reader = prepared.Execute())
        {
            while (reader.Read())
                preparedNames.Add(reader.GetString(0));
        }

        Assert.Equal(jitNames, preparedNames);
        Assert.True(jitNames.Count > 0);
    }

    [Fact]
    public void Jit_ReuseAfterClear_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");

        // First pass: filter to age == 25
        jit.Where(FilterStar.Column("age").Eq(25L));
        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(1, count);
        }

        // Clear and new filter: age >= 28
        jit.ClearFilters();
        jit.Where(FilterStar.Column("age").Gte(28L));
        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            // age 28, 29, 30 → 3 rows
            Assert.Equal(3, count);
        }
    }

    // ── Filter Cache Tests ──

    [Fact]
    public void Jit_RepeatedQuery_SameFilter_ConsistentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gte(28L));

        // Query three times — cached filter should produce identical results each time
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
    public void Jit_AdditiveWhere_AfterQuery_InvalidatesCache()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gte(25L));

        // First query — 6 rows (age 25..30)
        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(6, count);
        }

        // Add another filter — should invalidate cache
        jit.Where(FilterStar.Column("age").Lte(27L));

        // Second query — 3 rows (age 25,26,27)
        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Jit_DifferentProjections_OnSameHandle_Work()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");

        // First projection: name only
        using (var reader = jit.Query("name"))
        {
            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.StartsWith("User", reader.GetString(0));
        }

        // Second projection: age and balance
        using (var reader = jit.Query("age", "balance"))
        {
            Assert.Equal(2, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.Equal(21L, reader.GetInt64(0));
        }

        // Third: back to name — should still work (cache replaced, not stale)
        using (var reader = jit.Query("name"))
        {
            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.StartsWith("User", reader.GetString(0));
        }
    }

    [Fact]
    public void Jit_ClearFilters_ThenReFilter_InvalidatesCache()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Eq(25L));

        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(1, count);
        }

        // Clear + new filter — cache must be invalidated
        jit.ClearFilters();
        jit.Where(FilterStar.Column("age").Eq(30L));

        using (var reader = jit.Query())
        {
            Assert.True(reader.Read());
            Assert.Equal("User10", reader.GetString(1));
            Assert.False(reader.Read());
        }
    }

    [Fact]
    public void Jit_ClearFilters_ThenNoFilter_ReturnsAll()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Eq(25L));

        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(1, count);
        }

        // Clear with no replacement — should return all 10 rows
        jit.ClearFilters();

        using (var reader = jit.Query())
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(10, count);
        }
    }

    // ── Lifecycle Tests ──

    [Fact]
    public void Jit_OnDisposedDb_ThrowsObjectDisposed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        var db = SharcDatabase.OpenMemory(data);
        var jit = db.Jit("users");
        jit.Dispose();

        Assert.Throws<ObjectDisposedException>(() => jit.Query());
    }

    [Fact]
    public void Jit_QueryMultipleTimes_ConsistentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var jit = db.Jit("users");
        jit.Where(FilterStar.Column("age").Gt(25L));

        for (int i = 0; i < 3; i++)
        {
            var names = new List<string>();
            using var reader = jit.Query();
            while (reader.Read())
                names.Add(reader.GetString(1));

            // age > 25 → User6..User10 → 5 rows
            Assert.Equal(5, names.Count);
        }
    }
}