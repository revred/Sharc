// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for index seek operations — tests boundary conditions, non-existent values,
/// large index B-trees, seek-then-scan continuation, and multi-row index matches.
/// </summary>
public sealed class IndexSeekStressTests
{
    private static byte[] CreateLargeIndexedDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL, category TEXT)");
            TestDatabaseFactory.Execute(conn, "CREATE INDEX idx_products_name ON products (name)");
            TestDatabaseFactory.Execute(conn, "CREATE INDEX idx_products_category ON products (category)");

            for (int i = 1; i <= 500; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO products (name, price, category) VALUES ($name, $price, $cat)";
                cmd.Parameters.AddWithValue("$name", $"Product_{i:D5}");
                cmd.Parameters.AddWithValue("$price", 10.0 + i * 0.5);
                cmd.Parameters.AddWithValue("$cat", i % 5 == 0 ? "premium" : "standard");
                cmd.ExecuteNonQuery();
            }
        });
    }

    [Fact]
    public void SeekIndex_ExactMatch_FindsRow()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00250"));
        Assert.Equal("Product_00250", reader.GetString(1));
    }

    [Fact]
    public void SeekIndex_FirstRow_FindsCorrectly()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00001"));
        Assert.Equal("Product_00001", reader.GetString(1));
    }

    [Fact]
    public void SeekIndex_LastRow_FindsCorrectly()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00500"));
        Assert.Equal("Product_00500", reader.GetString(1));
    }

    [Fact]
    public void SeekIndex_NonExistent_ReturnsFalse()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.False(reader.SeekIndex("idx_products_name", "ZZZ_NonExistent"));
    }

    [Fact]
    public void SeekIndex_EmptyString_ReturnsFalse()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.False(reader.SeekIndex("idx_products_name", ""));
    }

    [Fact]
    public void SeekIndex_CategoryWithMultipleMatches_FindsFirst()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        // "premium" category has 100 rows (every 5th)
        Assert.True(reader.SeekIndex("idx_products_category", "premium"));
        Assert.Equal("premium", reader.GetString(3));
    }

    [Fact]
    public void SeekIndex_ThenScan_ContinuesFromPosition()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00100"));
        Assert.Equal("Product_00100", reader.GetString(1));

        // Read() should advance to next row
        Assert.True(reader.Read());
    }

    [Fact]
    public void SeekIndex_UnknownIndex_Throws()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.Throws<ArgumentException>(() => reader.SeekIndex("idx_nonexistent", "value"));
    }

    [Fact]
    public void SeekIndex_MultipleSeeksOnSameReader_EachFindsCorrectRow()
    {
        var data = CreateLargeIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("products");

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00010"));
        Assert.Equal("Product_00010", reader.GetString(1));

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00400"));
        Assert.Equal("Product_00400", reader.GetString(1));

        Assert.True(reader.SeekIndex("idx_products_name", "Product_00001"));
        Assert.Equal("Product_00001", reader.GetString(1));
    }

    [Fact]
    public void SeekIndex_IntegerIndex_MatchesCorrectRows()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("events");

        // user_id 3 appears for rows where (id % 5) + 1 = 3
        Assert.True(reader.SeekIndex("idx_events_user_id", 3L));
        Assert.Equal(3L, reader.GetInt64(1));
    }

    [Fact]
    public void SeekIndex_IntegerIndex_NonExistentValue_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("events");

        // user_id only 1-5, so 99 doesn't exist
        Assert.False(reader.SeekIndex("idx_events_user_id", 99L));
    }
}
