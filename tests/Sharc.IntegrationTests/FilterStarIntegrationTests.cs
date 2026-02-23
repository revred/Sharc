// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for the FilterStar byte-level filter engine.
/// Verifies end-to-end behavior: expression → compile → evaluate raw bytes → return matching rows.
/// </summary>
public sealed class FilterStarIntegrationTests
{
    // ── Integer comparisons ──

    [Fact]
    public void CreateReader_FilterStarEqInt_ReturnsMatchingRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Eq(25L));

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarGtInt_ReturnsMatchingRows()
    {
        // ages 21..30, filter > 28 → User9 (29), User10 (30)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Gt(28L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterStarLteInt_ReturnsMatchingRows()
    {
        // ages 21..30, filter <= 22 → User1 (21), User2 (22)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Lte(22L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    [Fact]
    public void CreateReader_FilterStarBetweenInt_ReturnsMatchingRows()
    {
        // ages 21..30, between 23 and 25 → User3 (23), User4 (24), User5 (25)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Between(23L, 25L));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    [Fact]
    public void CreateReader_FilterStarNeqInt_ExcludesMatchingRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Neq(23L));

        var count = 0;
        while (reader.Read())
            count++;

        // 5 rows minus User3 (age=23) = 4
        Assert.Equal(4, count);
    }

    // ── Text comparisons ──

    [Fact]
    public void CreateReader_FilterStarEqText_ReturnsMatchingRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").Eq("User3"));

        Assert.True(reader.Read());
        Assert.Equal(23L, reader.GetInt64(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarStartsWith_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").StartsWith("User1"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        // "User1" and "User10"
        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterStarEndsWith_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").EndsWith("5"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Single(names);
        Assert.Equal("User5", names[0]);
    }

    [Fact]
    public void CreateReader_FilterStarContains_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").Contains("er1"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        // "User1", "User10" — both contain "er1"
        Assert.Equal(2, names.Count);
    }

    // ── Real comparisons ──

    [Fact]
    public void CreateReader_FilterStarEqReal_ReturnsMatchingRow()
    {
        // balance = 100.50 + i, User7 has balance=107.50
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("balance").Eq(107.50));

        Assert.True(reader.Read());
        Assert.Equal("User7", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarBetweenReal_ReturnsMatchingRows()
    {
        // balance 101.50..110.50, between 103.50 and 105.50 → User3, User4, User5
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("balance").Between(103.50, 105.50));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    // ── NULL operators ──

    [Fact]
    public void CreateReader_FilterStarIsNull_MatchesNullValues()
    {
        // all_types: null_val is NULL for all 5 rows
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("all_types",
            FilterStar.Column("null_val").IsNull());

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(5, count);
    }

    [Fact]
    public void CreateReader_FilterStarIsNotNull_MatchesNonNullValues()
    {
        // all_types: blob_val is NULL for row 3, non-null for rows 1,2,4,5
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("all_types",
            FilterStar.Column("blob_val").IsNotNull());

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(4, count);
    }

    // ── Logical composition ──

    [Fact]
    public void CreateReader_FilterStarAnd_BothConditionsMustMatch()
    {
        // age > 22 AND age < 26 → User3 (23), User4 (24), User5 (25)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.And(
                FilterStar.Column("age").Gt(22L),
                FilterStar.Column("age").Lt(26L)
            ));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    [Fact]
    public void CreateReader_FilterStarOr_AnyConditionMatches()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Or(
                FilterStar.Column("name").Eq("User1"),
                FilterStar.Column("name").Eq("User10")
            ));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterStarNot_NegatesCondition()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Not(FilterStar.Column("name").Eq("User3")));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(4, count);
    }

    [Fact]
    public void CreateReader_FilterStarComplexNested_Works()
    {
        // (age BETWEEN 22 AND 24) OR (name = "User8")
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Or(
                FilterStar.Column("age").Between(22L, 24L),
                FilterStar.Column("name").Eq("User8")
            ));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        // User2 (22), User3 (23), User4 (24), User8
        Assert.Equal(4, names.Count);
        Assert.Contains("User2", names);
        Assert.Contains("User3", names);
        Assert.Contains("User4", names);
        Assert.Contains("User8", names);
    }

    // ── Set membership ──

    [Fact]
    public void CreateReader_FilterStarInInt_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").In(21L, 25L, 30L));

        var ages = new List<long>();
        while (reader.Read())
            ages.Add(reader.GetInt64(2));

        Assert.Equal(3, ages.Count);
        Assert.Contains(21L, ages);
        Assert.Contains(25L, ages);
        Assert.Contains(30L, ages);
    }

    [Fact]
    public void CreateReader_FilterStarInText_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").In("User2", "User7"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User2", names);
        Assert.Contains("User7", names);
    }

    [Fact]
    public void CreateReader_FilterStarNotIn_ExcludesMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").NotIn(21L, 23L, 25L));

        var ages = new List<long>();
        while (reader.Read())
            ages.Add(reader.GetInt64(2));

        // 22, 24
        Assert.Equal(2, ages.Count);
        Assert.Contains(22L, ages);
        Assert.Contains(24L, ages);
    }

    // ── Column by ordinal ──

    [Fact]
    public void CreateReader_FilterStarByOrdinal_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        // Column 2 = age (0=id, 1=name, 2=age, 3=balance, 4=avatar)
        using var reader = db.CreateReader("users",
            FilterStar.Column(2).Gt(23L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        // User4 (age 24), User5 (age 25)
        Assert.Equal(2, names.Count);
        Assert.Contains("User4", names);
        Assert.Contains("User5", names);
    }

    // ── Projection + FilterStar ──

    [Fact]
    public void CreateReader_FilterStarWithProjection_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            ["name", "age"],
            FilterStar.Column("age").Lte(22L));

        Assert.Equal(2, reader.FieldCount);

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0)); // projected column 0 = name

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    // ── Edge cases ──

    [Fact]
    public void CreateReader_FilterStarNoMatches_ReadReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Eq(999L));

        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarInvalidColumn_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<ArgumentException>(() =>
            db.CreateReader("users",
                FilterStar.Column("nonexistent").Eq(1L)));
    }

    [Fact]
    public void CreateReader_FilterStarCaseInsensitiveColumn_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("AGE").Eq(23L));

        Assert.True(reader.Read());
        Assert.Equal("User3", reader.GetString(1));
        Assert.False(reader.Read());
    }

    // ── INTEGER PRIMARY KEY (rowid alias) ──

    [Fact]
    public void CreateReader_FilterStarOnRowidAlias_ReturnsMatchingRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        // "id" is INTEGER PRIMARY KEY → stored as rowid, record has NULL
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Eq(5L));

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarRowidAlias_BetweenWorks()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Between(3L, 5L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(3, ids.Count);
        Assert.Contains(3L, ids);
        Assert.Contains(4L, ids);
        Assert.Contains(5L, ids);
    }

    // ── Large table (multi-page B-tree) ──

    [Fact]
    public void CreateReader_FilterStarLargeTable_CorrectResults()
    {
        // 1000 rows, number = i * 100
        var data = TestDatabaseFactory.CreateLargeDatabase(1000);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("large_table",
            FilterStar.Column("number").Between(50_000L, 50_200L));

        var numbers = new List<long>();
        while (reader.Read())
            numbers.Add(reader.GetInt64(2));

        // 50000, 50100, 50200
        Assert.Equal(3, numbers.Count);
        Assert.Contains(50_000L, numbers);
        Assert.Contains(50_100L, numbers);
        Assert.Contains(50_200L, numbers);
    }
}
