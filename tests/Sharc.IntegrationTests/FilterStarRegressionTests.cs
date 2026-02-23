// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Regression tests covering dangerous FilterStar gaps:
/// - Lt/Gte operators (never integration-tested on any type)
/// - Rowid alias with untested operators (Gt, Lt, Gte, Lte, Neq)
/// - All-NULL column comparisons (SQL NULL semantics)
/// - Filter on non-projected column
/// - Deep nesting (5+ levels)
/// </summary>
public sealed class FilterStarRegressionTests
{
    // ═══════════════════════════════════════════════════════════════════
    // 1. Lt and Gte operators — never integration-tested on ANY type
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateReader_FilterStarLtInt_ReturnsRowsBelow()
    {
        // ages 21..30, Lt(23) → User1 (21), User2 (22)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Lt(23L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    [Fact]
    public void CreateReader_FilterStarGteInt_ReturnsRowsAtOrAbove()
    {
        // ages 21..30, Gte(29) → User9 (29), User10 (30)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("age").Gte(29L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterStarLtReal_ReturnsRowsBelow()
    {
        // balance = 100.50 + i, Lt(103.50) → User1 (101.50), User2 (102.50)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("balance").Lt(103.50));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    [Fact]
    public void CreateReader_FilterStarGteReal_ReturnsRowsAtOrAbove()
    {
        // balance = 100.50 + i, Gte(109.50) → User9 (109.50), User10 (110.50)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("balance").Gte(109.50));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterStarLtText_ReturnsLexicographicallyLower()
    {
        // UTF-8 ordinal: "User1" < "User10" < "User2" < "User3"
        // Lt("User3") → User1, User10, User2 (three names sort before "User3")
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").Lt("User3"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(3, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User10", names);
        Assert.Contains("User2", names);
    }

    [Fact]
    public void CreateReader_FilterStarGteText_ReturnsLexicographicallyHigherOrEqual()
    {
        // UTF-8 ordinal: "User9" is the highest single-digit name
        // Gte("User9") → User9 only (no name >= "User9" except "User9" itself)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("name").Gte("User9"));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Single(names);
        Assert.Equal("User9", names[0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. Rowid alias — 5 untested operators
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateReader_FilterStarRowidAlias_Gt_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Gt(8L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Contains(9L, ids);
        Assert.Contains(10L, ids);
    }

    [Fact]
    public void CreateReader_FilterStarRowidAlias_Lt_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Lt(3L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Contains(1L, ids);
        Assert.Contains(2L, ids);
    }

    [Fact]
    public void CreateReader_FilterStarRowidAlias_Gte_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Gte(9L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Contains(9L, ids);
        Assert.Contains(10L, ids);
    }

    [Fact]
    public void CreateReader_FilterStarRowidAlias_Lte_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Lte(2L));

        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        Assert.Equal(2, ids.Count);
        Assert.Contains(1L, ids);
        Assert.Contains(2L, ids);
    }

    [Fact]
    public void CreateReader_FilterStarRowidAlias_Neq_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.Column("id").Neq(5L));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(9, count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. All-NULL column comparisons (SQL NULL semantics)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateReader_FilterStarAllNullColumn_EqNonNull_ReturnsZeroRows()
    {
        // null_val is NULL for all 5 rows in all_types — Eq(42) must return 0
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("all_types",
            FilterStar.Column("null_val").Eq("anything"));

        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterStarAllNullColumn_IsNull_ReturnsAllRows()
    {
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
    public void CreateReader_FilterStarNullableColumn_GtSkipsNulls()
    {
        // blob_val is NULL for row 3 in all_types. Gt comparison on a column
        // with some NULLs should skip NULL rows (SQL: NULL > x is unknown/false).
        // Use int_val: row1=42, row2=0, row3=-999, row4=MaxValue, row5=1
        // Gt(0) should return rows with int_val > 0: row1 (42), row4 (MaxValue), row5 (1)
        var data = TestDatabaseFactory.CreateAllTypesDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("all_types",
            FilterStar.Column("int_val").Gt(0L));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. Filter on non-projected column — most common real-world pattern
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateReader_FilterOnNonProjectedColumn_OnlyProjectedReturned()
    {
        // Project only "name", but filter on "age" (not in projection)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            ["name"],
            FilterStar.Column("age").Gt(28L));

        Assert.Equal(1, reader.FieldCount);

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. Deep nesting (5 levels) — realistic filters go much deeper than 2
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateReader_FilterStarDeepNesting5Levels_CorrectResults()
    {
        // Build: AND(
        //   OR(                              ← level 2
        //     AND(                           ← level 3
        //       Gt("age", 22),               ← level 4
        //       Lt("age", 26)                ← level 4
        //     ),
        //     OR(                            ← level 3
        //       Eq("name", "User8"),         ← level 4
        //       Eq("name", "User9")          ← level 4
        //     )
        //   ),
        //   NOT(                             ← level 2
        //     Eq("name", "User4")            ← level 3 (deepest via NOT path = 5 counting AND root)
        //   )
        // )
        //
        // Expected: User3 (age 23), User5 (age 25), User8, User9
        //   (User4 excluded by NOT, despite age 24 matching the AND)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            FilterStar.And(
                FilterStar.Or(
                    FilterStar.And(
                        FilterStar.Column("age").Gt(22L),
                        FilterStar.Column("age").Lt(26L)
                    ),
                    FilterStar.Or(
                        FilterStar.Column("name").Eq("User8"),
                        FilterStar.Column("name").Eq("User9")
                    )
                ),
                FilterStar.Not(
                    FilterStar.Column("name").Eq("User4")
                )
            ));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(4, names.Count);
        Assert.Contains("User3", names);
        Assert.Contains("User5", names);
        Assert.Contains("User8", names);
        Assert.Contains("User9", names);
        Assert.DoesNotContain("User4", names);
    }
}
