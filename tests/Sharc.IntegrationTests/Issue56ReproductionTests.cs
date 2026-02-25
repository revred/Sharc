// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc;
using Sharc.Core.Query;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class Issue56ReproductionTests
{
    [Fact]
    public void Issue56_Reproduction_InMemory()
    {
        // 1. Setup: Create data
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // 2. Create filter for id = 5 (User5)
        var filter = new[] { new SharcFilter("id", SharcOperator.Equal, 5L) };

        // 3. Create reader
        using var reader = db.CreateReader("users", filter);

        // 4. Assert
        Assert.True(reader.Read(), "Expected at least one row for id=5");
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Issue56_Reproduction_FileBacked()
    {
        // 1. Setup: Create data and save to file
        var path = Path.Combine(Path.GetTempPath(), $"issue56_repro_{Guid.NewGuid():N}.db");
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        File.WriteAllBytes(path, data);

        try
        {
            // 2. Open database as file
            using var db = SharcDatabase.Open(path, new SharcOpenOptions { Writable = false });

            // 3. Create filter for id = 5 (User5)
            var filter = new[] { new SharcFilter("id", SharcOperator.Equal, 5L) };

            // 4. Create reader
            using var reader = db.CreateReader("users", filter);

            // 5. Assert
            Assert.True(reader.Read(), "Expected at least one row for id=5");
            Assert.Equal("User5", reader.GetString(1));
            Assert.False(reader.Read());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MergedColumn_Reproduction()
    {
        // 1. Create database with merged GUID columns
        // id(0), guid__hi(1), guid__lo(2), category_id(3)
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE products (id INTEGER PRIMARY KEY, guid__hi INTEGER, guid__lo INTEGER, category_id INTEGER)";
            cmd.ExecuteNonQuery();

            for (int i = 1; i <= 5; i++)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO products (id, guid__hi, guid__lo, category_id) VALUES ($id, 100, 200, $cat)";
                ins.Parameters.AddWithValue("$id", i);
                ins.Parameters.AddWithValue("$cat", i);
                ins.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);

        // 2. Create filter for category_id = 3
        // Logical ordinal for category_id should be 2 (id=0, guid=1, category_id=2)
        // But physical ordinal is 3.
        var filter = new[] { new SharcFilter("category_id", SharcOperator.Equal, 3L) };

        // 3. Create reader
        using var reader = db.CreateReader("products", filter);

        // 4. Assert
        // If the bug exists, reader.Read() will return false because it compares 3L against guid__lo (200)
        Assert.True(reader.Read(), "Expected at least one row for category_id=3");
        Assert.Equal(3L, reader.GetInt64(2)); // category_id is logical index 2
        Assert.False(reader.Read());

        // 5. Test db.Query (IFilterStar / JIT path)
        var filterStar = FilterStar.Column("category_id").Eq(3L);
        using var queryReader = db.Jit("products").Where(filterStar).Query();
        Assert.True(queryReader.Read(), "Expected db.Jit query to find row for category_id=3");
        Assert.Equal(3L, queryReader.GetInt64(queryReader.GetOrdinal("category_id")));
        Assert.False(queryReader.Read());
    }

    [Fact]
    public void MultipleMergedColumns_Reproduction()
    {
        // 1. Setup database with multiple merged GUID columns
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE items (
                    id INTEGER PRIMARY KEY,
                    guid1__hi INTEGER,
                    guid1__lo INTEGER,
                    guid2__hi INTEGER,
                    guid2__lo INTEGER,
                    category_id INTEGER,
                    name TEXT
                );
                INSERT INTO items (id, guid1__hi, guid1__lo, guid2__hi, guid2__lo, category_id, name)
                VALUES (1, 100, 200, 300, 400, 5, 'Item 1');
                INSERT INTO items (id, guid1__hi, guid1__lo, guid2__hi, guid2__lo, category_id, name)
                VALUES (2, 500, 600, 700, 800, 10, 'Item 2');
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test filtering on category_id (logical 3, physical 5)
        // Schema: [id(0), guid1(1), guid2(2), category_id(3), name(4)]
        // Physical: [id(0), guid1_hi(1), guid1_lo(2), guid2_hi(3), guid2_lo(4), category_id(5), name(6)]
        
        var filters = new[] { new SharcFilter("category_id", SharcOperator.Equal, 10L) };
        using var reader = db.CreateReader("items", filters: filters);

        Assert.True(reader.Read(), "Expected row with category_id=10");
        Assert.Equal(10L, reader.GetInt64(3)); // logical index 3
        Assert.Equal("Item 2", reader.GetString(4)); // logical index 4
        Assert.False(reader.Read());

        // 3. Verify projection works correctly for columns after multiple merges
        using var readerAll = db.CreateReader("items");
        Assert.True(readerAll.Read());
        Assert.Equal(5L, readerAll.GetInt64(3)); // category_id
        Assert.Equal("Item 1", readerAll.GetString(4)); // name
    }

    [Fact]
    public void WithoutRowId_MergedColumns_Reproduction()
    {
        // 1. Setup WITHOUT ROWID table with merged GUID columns
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE secure_items (
                    pk_guid__hi INTEGER,
                    pk_guid__lo INTEGER,
                    category_id INTEGER,
                    name TEXT,
                    PRIMARY KEY (pk_guid__hi, pk_guid__lo)
                ) WITHOUT ROWID;
                INSERT INTO secure_items (pk_guid__hi, pk_guid__lo, category_id, name)
                VALUES (1, 1, 3, 'Secure 1');
                INSERT INTO secure_items (pk_guid__hi, pk_guid__lo, category_id, name)
                VALUES (2, 2, 7, 'Secure 2');
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test filtering on category_id
        // Schema: [pk_guid(0), category_id(1), name(2)]
        // Physical: [pk_guid_hi(0), pk_guid_lo(1), category_id(2), name(3)]
        
        var filter = FilterStar.Column("category_id").Eq(7L);
        using var jitReader = db.Jit("secure_items").Where(filter).Query();

        Assert.True(jitReader.Read(), "Expected row with category_id=7 in WITHOUT ROWID table");
        Assert.Equal(7L, jitReader.GetInt64(1)); // logical 1
        Assert.Equal("Secure 2", jitReader.GetString(2)); // logical 2
        Assert.False(jitReader.Read());
    }

    [Fact]
    public void IndexSeeker_MergedColumns_Reproduction()
    {
        // 1. Setup database with merged GUID columns and an index on a later column
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE indexed_items (
                    id INTEGER PRIMARY KEY,
                    guid__hi INTEGER,
                    guid__lo INTEGER,
                    status_code INTEGER,
                    name TEXT
                );
                CREATE INDEX idx_status ON indexed_items (status_code);
                INSERT INTO indexed_items (id, guid__hi, guid__lo, status_code, name)
                VALUES (1, 1, 1, 200, 'OK');
                INSERT INTO indexed_items (id, guid__hi, guid__lo, status_code, name)
                VALUES (2, 2, 2, 404, 'Not Found');
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test sargable filter on status_code
        // Physical ordinals: [id:0, guid_hi:1, guid_lo:2, status_code:3, name:4]
        // Logical ordinals: [id:0, guid:1, status_code:2, name:3]
        
        using var reader = db.Query("SELECT * FROM indexed_items WHERE status_code = 404");

        Assert.True(reader.Read(), "Expected index seeker to find row with status_code=404");
        Assert.Equal(404L, reader.GetInt64(2)); // logical 2
        Assert.Equal("Not Found", reader.GetString(3)); // logical 3
        Assert.False(reader.Read());
    }

    [Fact]
    public void ParameterizedQuery_MergedColumns_Reproduction()
    {
        // 1. Setup database with merged GUID columns
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE catalog (
                    guid__hi INTEGER,
                    guid__lo INTEGER,
                    price REAL,
                    title TEXT
                );
                INSERT INTO catalog (guid__hi, guid__lo, price, title)
                VALUES (1, 1, 19.99, 'Book A');
                INSERT INTO catalog (guid__hi, guid__lo, price, title)
                VALUES (2, 2, 29.99, 'Book B');
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test parameterized query on price (logical 1, physical 2)
        var parameters = new Dictionary<string, object> { ["targetPrice"] = 29.99 };
        using var reader = db.Query(parameters, "SELECT * FROM catalog WHERE price = $targetPrice");

        Assert.True(reader.Read(), "Expected parameterized query to find row with price=29.99");
        Assert.Equal(29.99, reader.GetDouble(1)); // logical 1 (price)
        Assert.Equal("Book B", reader.GetString(2)); // logical 2 (title)
        Assert.False(reader.Read());
    }

    [Fact]
    public void GuidAtEnd_Reproduction()
    {
        // 1. Setup database with merged GUID at the end
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE ending_guid (
                    id INTEGER PRIMARY KEY,
                    category_id INTEGER,
                    guid__hi INTEGER,
                    guid__lo INTEGER
                );
                INSERT INTO ending_guid (id, category_id, guid__hi, guid__lo)
                VALUES (1, 10, 100, 200);
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test filtering on category_id (logical 1, physical 1)
        // Schema: [id(0), category_id(1), guid(2)]
        // Physical: [id(0), category_id(1), guid_hi(2), guid_lo(3)]
        
        var filter = FilterStar.Column("category_id").Eq(10L);
        using var reader = db.Jit("ending_guid").Where(filter).Query();

        Assert.True(reader.Read(), "Expected row with category_id=10 when GUID is at end");
        Assert.Equal(10L, reader.GetInt64(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void FilterOnGuid_Reproduction()
    {
        // 1. Setup database with merged GUID in the middle
        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE guid_lookup (
                    id INTEGER PRIMARY KEY,
                    target_guid__hi INTEGER,
                    target_guid__lo INTEGER,
                    val TEXT
                );
                INSERT INTO guid_lookup (id, target_guid__hi, target_guid__lo, val)
                VALUES (1, 123, 456, 'Found It');
                INSERT INTO guid_lookup (id, target_guid__hi, target_guid__lo, val)
                VALUES (2, 789, 012, 'Wrong One');
            ";
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test filtering ON the GUID column
        // We expect this to work by splitting the GUID filter into two LONG filters.
        // For now, we simulate this by filtering hi and lo separately to see if the mapping works.
        
        var filter = FilterStar.And(
            FilterStar.Column("target_guid__hi").Eq(123L),
            FilterStar.Column("target_guid__lo").Eq(456L)
        );
        
        // Wait, if the columns are merged, "target_guid__hi" might not be accessible logically!
        // Let's see if we can filter on the logical "target_guid" name if we support it in the future.
        // For now, let's test if we can filter on the logical 'target_guid' using its first physical part.

        using var reader = db.Jit("guid_lookup").Where(FilterStar.Column("target_guid").Eq(123L)).Query();
        Assert.True(reader.Read(), "Filtering on hi part of merged GUID should work at least");
        Assert.Equal("Found It", reader.GetString(2)); // logical 2 (val)
    }

    [Fact]
    public void NativeEqGuid_Reproduction()
    {
        // 1. Setup database with merged GUID
        var targetGuid = Guid.NewGuid();
        var (hi, lo) = Sharc.Core.Primitives.GuidCodec.ToInt64Pair(targetGuid);

        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE guid_native (
                    id INTEGER PRIMARY KEY,
                    target_guid__hi INTEGER,
                    target_guid__lo INTEGER,
                    val TEXT
                );
                INSERT INTO guid_native (id, target_guid__hi, target_guid__lo, val)
                VALUES (1, $hi, $lo, 'Found It');
            ";
            cmd.Parameters.AddWithValue("$hi", hi);
            cmd.Parameters.AddWithValue("$lo", lo);
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test native Eq(Guid)
        var filter = FilterStar.Column("target_guid").Eq(targetGuid);
        using var reader = db.Jit("guid_native").Where(filter).Query();

        Assert.True(reader.Read(), "Native Eq(Guid) should find the row via expansion to hi/lo");
        Assert.Equal("Found It", reader.GetString(2));
    }

    [Fact]
    public void InterleavedMergedColumns_StressTest()
    {
        // 1. Setup database with interleaved merged and regular columns
        // Physical: [id:0, g1_hi:1, g1_lo:2, c1:3, g2_hi:4, g2_lo:5, c2:6]
        // Logical: [id:0, g1:1, c1:2, g2:3, c2:4]
        
        var g1 = Guid.NewGuid();
        var (g1hi, g1lo) = Sharc.Core.Primitives.GuidCodec.ToInt64Pair(g1);
        var g2 = Guid.NewGuid();
        var (g2hi, g2lo) = Sharc.Core.Primitives.GuidCodec.ToInt64Pair(g2);

        var dbBytes = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE interleaved (
                    id INTEGER PRIMARY KEY,
                    g1__hi INTEGER,
                    g1__lo INTEGER,
                    c1 TEXT,
                    g2__hi INTEGER,
                    g2__lo INTEGER,
                    c2 TEXT
                );
                INSERT INTO interleaved (id, g1__hi, g1__lo, c1, g2__hi, g2__lo, c2)
                VALUES (1, $g1hi, $g1lo, 'C1_Val', $g2hi, $g2lo, 'C2_Val');
            ";
            cmd.Parameters.AddWithValue("$g1hi", g1hi);
            cmd.Parameters.AddWithValue("$g1lo", g1lo);
            cmd.Parameters.AddWithValue("$g2hi", g2hi);
            cmd.Parameters.AddWithValue("$g2lo", g2lo);
            cmd.ExecuteNonQuery();
        });

        var dbPath = Path.GetTempFileName();
        File.WriteAllBytes(dbPath, dbBytes);
        using var db = SharcDatabase.Open(dbPath);

        // 2. Test filtering on column AFTER second GUID (logical 4, physical 6)
        using var reader = db.Jit("interleaved").Where(FilterStar.Column("c2").Eq("C2_Val")).Query();
        Assert.True(reader.Read(), "Should find row when filtering after interleaved GUIDs");
        Assert.Equal("C2_Val", reader.GetString(4)); // logical 4
        Assert.Equal(g1, reader.GetGuid(1)); // logical 1
        Assert.Equal(g2, reader.GetGuid(3)); // logical 3

        // 3. Test filtering ON the second GUID (logical 3)
        using var reader2 = db.Jit("interleaved").Where(FilterStar.Column("g2").Eq(g2)).Query();
        Assert.True(reader2.Read(), "Should find row when filtering ON interleaved GUID");
        Assert.Equal(1L, reader2.GetInt64(0));
    }
}
