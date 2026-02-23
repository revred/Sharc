// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for FilterStar / FilterNode evaluation — tests NULL semantics,
/// type coercion edge cases, compound filter trees, boundary values, and
/// empty string handling under real byte-level evaluation.
/// </summary>
public sealed class FilterStarStressTests
{
    private static byte[] CreateNullableDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE data (id INTEGER PRIMARY KEY, val INTEGER, text_val TEXT)");

            // Row 1: NULL val, NULL text
            TestDatabaseFactory.Execute(conn, "INSERT INTO data (id) VALUES (1)");
            // Row 2: val=0, empty string
            TestDatabaseFactory.Execute(conn, "INSERT INTO data VALUES (2, 0, '')");
            // Row 3: val=42, normal text
            TestDatabaseFactory.Execute(conn, "INSERT INTO data VALUES (3, 42, 'hello')");
            // Row 4: val=-1, text with unicode
            TestDatabaseFactory.Execute(conn, "INSERT INTO data VALUES (4, -1, '世界')");
            // Row 5: val=MAX, long text
            TestDatabaseFactory.Execute(conn, "INSERT INTO data VALUES (5, 9223372036854775807, 'very long string padding here')");
        });
    }

    private static byte[] CreateLargeFilterDatabase()
    {
        return TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn, "CREATE TABLE items (id INTEGER PRIMARY KEY, category TEXT, price REAL, stock INTEGER)");
            for (int i = 1; i <= 200; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO items (category, price, stock) VALUES ($cat, $price, $stock)";
                cmd.Parameters.AddWithValue("$cat", i % 4 == 0 ? "electronics" : i % 4 == 1 ? "books" : i % 4 == 2 ? "clothing" : "food");
                cmd.Parameters.AddWithValue("$price", 10.0 + (i * 0.5));
                cmd.Parameters.AddWithValue("$stock", i % 10);
                cmd.ExecuteNonQuery();
            }
        });
    }

    // ── NULL Semantics ──

    [Fact]
    public void Filter_IsNull_MatchesNullRows()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").IsNull());

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(1, count); // only row 1
    }

    [Fact]
    public void Filter_IsNotNull_ExcludesNullRows()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").IsNotNull());

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(4, count); // rows 2-5
    }

    [Fact]
    public void Filter_EqNull_DoesNotMatchNullRow()
    {
        // SQL semantics: NULL = 42 → NULL (not true)
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Eq(42L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Single(ids);
        Assert.Equal(3, ids[0]); // only row 3 with val=42
    }

    // ── Zero vs NULL distinction ──

    [Fact]
    public void Filter_EqZero_MatchesZeroNotNull()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Eq(0L));

        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.Equal(2, reader.GetInt64(0)); // row 2 has val=0
        }
        Assert.Equal(1, count);
    }

    // ── Empty string vs NULL ──

    [Fact]
    public void Filter_TextIsNull_DoesNotMatchEmptyString()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("text_val").IsNull());

        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.Equal(1, reader.GetInt64(0)); // only row 1 has NULL text
        }
        Assert.Equal(1, count);
    }

    // ── Compound Filters (AND / OR) ──

    [Fact]
    public void Filter_And_BothConditionsMustMatch()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items",
            new[] { "category", "stock" },
            FilterStar.And(
                FilterStar.Column("category").Eq("electronics"),
                FilterStar.Column("stock").Gt(5L)));

        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.Equal("electronics", reader.GetString(0));
            Assert.True(reader.GetInt64(1) > 5);
        }
        Assert.True(count > 0);
    }

    [Fact]
    public void Filter_Or_EitherConditionSuffices()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items",
            new[] { "category" },
            FilterStar.Or(
                FilterStar.Column("category").Eq("electronics"),
                FilterStar.Column("category").Eq("books")));

        var categories = new HashSet<string>();
        int count = 0;
        while (reader.Read())
        {
            categories.Add(reader.GetString(0));
            count++;
        }

        Assert.Equal(100, count); // 50 electronics + 50 books
        Assert.Contains("electronics", categories);
        Assert.Contains("books", categories);
        Assert.DoesNotContain("clothing", categories);
    }

    [Fact]
    public void Filter_Not_InvertsResult()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items",
            new[] { "category" },
            FilterStar.Not(FilterStar.Column("category").Eq("food")));

        int count = 0;
        while (reader.Read())
        {
            Assert.NotEqual("food", reader.GetString(0));
            count++;
        }
        Assert.Equal(150, count); // 200 - 50 food
    }

    // ── Boundary Values ──

    [Fact]
    public void Filter_MaxInt64_MatchesExact()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Eq(long.MaxValue));

        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.Equal(5, reader.GetInt64(0));
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Filter_NegativeValue_MatchesCorrectly()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Lt(0L));

        int count = 0;
        while (reader.Read())
        {
            count++;
            Assert.Equal(4, reader.GetInt64(0)); // val = -1
        }
        Assert.Equal(1, count);
    }

    // ── String operations ──

    [Fact]
    public void Filter_StartsWith_MatchesPrefix()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items",
            FilterStar.Column("category").StartsWith("elec"));

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(50, count);
    }

    [Fact]
    public void Filter_Contains_MatchesSubstring()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items",
            new[] { "category" },
            FilterStar.Column("category").Contains("oo"));

        // "books" and "food" both contain "oo"
        var categories = new HashSet<string>();
        while (reader.Read())
            categories.Add(reader.GetString(0));

        Assert.Contains("books", categories);
        Assert.Contains("food", categories);
        Assert.DoesNotContain("electronics", categories);
    }

    // ── Between with boundary values ──

    [Fact]
    public void Filter_Between_IncludesBothBounds()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Between(-1L, 42L));

        var vals = new List<long>();
        while (reader.Read())
            vals.Add(reader.GetInt64(1));

        Assert.Contains(0L, vals);   // row 2
        Assert.Contains(42L, vals);  // row 3
        Assert.Contains(-1L, vals);  // row 4
        Assert.Equal(3, vals.Count);
    }

    // ── In operator ──

    [Fact]
    public void Filter_In_MatchesSetMembers()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").In(0L, 42L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Contains(2L, ids); // val=0
        Assert.Contains(3L, ids); // val=42
    }

    // ── Deep compound filter tree ──

    [Fact]
    public void Filter_DeepAndOrTree_CorrectResults()
    {
        var data = CreateLargeFilterDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // (category = 'electronics' AND stock > 5) OR (category = 'food' AND stock = 0)
        var filter = FilterStar.Or(
            FilterStar.And(
                FilterStar.Column("category").Eq("electronics"),
                FilterStar.Column("stock").Gt(5L)),
            FilterStar.And(
                FilterStar.Column("category").Eq("food"),
                FilterStar.Column("stock").Eq(0L)));

        using var reader = db.CreateReader("items", filter);
        int count = 0;
        while (reader.Read()) count++;
        Assert.True(count > 0);
    }

    // ── No-match filter returns zero rows ──

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        var data = CreateNullableDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("data",
            FilterStar.Column("val").Eq(999999L));

        Assert.False(reader.Read());
    }
}
