// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public class SharcDatabaseQueryTests
{
    // ─── SELECT * ───────────────────────────────────────────────

    [Fact]
    public void Query_SelectAll_ReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(10, count);
    }

    // ─── Column projection ─────────────────────────────────────

    [Fact]
    public void Query_SpecificColumns_ReturnsProjected()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT name, age FROM users");

        Assert.Equal(2, reader.FieldCount);
        Assert.True(reader.Read());
    }

    // ─── WHERE equality ─────────────────────────────────────────

    [Fact]
    public void Query_WhereEqInteger_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE age = 25");

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1)); // name is column 1
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_WhereEqString_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE name = 'User3'");

        Assert.True(reader.Read());
        Assert.Equal(23L, reader.GetInt64(2)); // age is column 2
        Assert.False(reader.Read());
    }

    // ─── Comparison operators ───────────────────────────────────

    [Fact]
    public void Query_WhereGt_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE age > 28");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void Query_WhereLte_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE age <= 22");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    // ─── BETWEEN ────────────────────────────────────────────────

    [Fact]
    public void Query_WhereBetween_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE age BETWEEN 23 AND 25");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(3, count);
    }

    // ─── AND / OR ───────────────────────────────────────────────

    [Fact]
    public void Query_WhereAnd_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            "SELECT * FROM users WHERE age > 25 AND age < 28");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User6", names);
        Assert.Contains("User7", names);
    }

    [Fact]
    public void Query_WhereOr_FiltersCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            "SELECT * FROM users WHERE name = 'User1' OR name = 'User10'");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User10", names);
    }

    // ─── String operations ──────────────────────────────────────

    [Fact]
    public void Query_LikePrefix_StartsWith()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE name LIKE 'User1%'");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        // User1, User10
        Assert.Equal(2, names.Count);
    }

    // ─── Cache hit ──────────────────────────────────────────────

    [Fact]
    public void Query_SameQueryTwice_BothWork()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        const string sql = "SELECT * FROM users WHERE age = 25";

        // First call (cache miss)
        using (var reader = db.Query(sql))
        {
            Assert.True(reader.Read());
            Assert.Equal("User5", reader.GetString(1));
        }

        // Second call (cache hit)
        using (var reader = db.Query(sql))
        {
            Assert.True(reader.Read());
            Assert.Equal("User5", reader.GetString(1));
        }
    }

    // ─── Error handling ─────────────────────────────────────────

    [Fact]
    public void Query_InvalidTable_ThrowsKeyNotFoundException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<KeyNotFoundException>(() => db.Query("SELECT * FROM nonexistent"));
    }

    [Fact]
    public void Query_InvalidColumn_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<ArgumentException>(() => db.Query("SELECT bogus FROM users"));
    }

    // ─── ORDER BY ──────────────────────────────────────────────

    [Fact]
    public void Query_OrderByAsc_ReturnsSorted()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY age");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(10, ages.Count);
        for (int i = 1; i < ages.Count; i++)
            Assert.True(ages[i] >= ages[i - 1], $"Expected ascending: {ages[i - 1]} <= {ages[i]}");
    }

    [Fact]
    public void Query_OrderByDesc_ReturnsSortedDescending()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY age DESC");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(10, ages.Count);
        Assert.Equal(30L, ages[0]);  // User10
        Assert.Equal(21L, ages[9]);  // User1
        for (int i = 1; i < ages.Count; i++)
            Assert.True(ages[i] <= ages[i - 1], $"Expected descending: {ages[i - 1]} >= {ages[i]}");
    }

    [Fact]
    public void Query_OrderByString_SortsAlphabetically()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY name");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(10, names.Count);
        // String sort: User1, User10, User2, User3, ...
        Assert.Equal("User1", names[0]);
        Assert.Equal("User10", names[1]);
        Assert.Equal("User2", names[2]);
    }

    [Fact]
    public void Query_OrderByWithWhere_FiltersThenSorts()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users WHERE age > 25 ORDER BY age DESC");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(5, ages.Count);
        Assert.Equal(30L, ages[0]);
        Assert.Equal(26L, ages[4]);
    }

    // ─── LIMIT / OFFSET ─────────────────────────────────────────

    [Fact]
    public void Query_Limit_ReturnsOnlyNRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 3");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public void Query_Offset_SkipsFirstNRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY age LIMIT 100 OFFSET 8");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(2, ages.Count);
        Assert.Equal(29L, ages[0]); // User9
        Assert.Equal(30L, ages[1]); // User10
    }

    [Fact]
    public void Query_LimitOffset_ReturnsCorrectSlice()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY age LIMIT 3 OFFSET 2");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(3, ages.Count);
        Assert.Equal(23L, ages[0]); // User3
        Assert.Equal(24L, ages[1]); // User4
        Assert.Equal(25L, ages[2]); // User5
    }

    [Fact]
    public void Query_LimitWithOrderBy_SortsThenLimits()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users ORDER BY age DESC LIMIT 3");

        var ages = new List<long>();
        while (reader.Read()) ages.Add(reader.GetInt64(2));

        Assert.Equal(3, ages.Count);
        Assert.Equal(30L, ages[0]); // User10
        Assert.Equal(29L, ages[1]); // User9
        Assert.Equal(28L, ages[2]); // User8
    }

    [Fact]
    public void Query_LimitZero_ReturnsNoRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 0");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_OffsetBeyondRows_ReturnsEmpty()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users LIMIT 100 OFFSET 100");

        Assert.False(reader.Read());
    }

    // ─── DISTINCT ────────────────────────────────────────────────

    [Fact]
    public void Query_Distinct_RemovesDuplicates()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT DISTINCT category FROM items");

        var categories = new List<string>();
        while (reader.Read()) categories.Add(reader.GetString(0));

        Assert.Equal(2, categories.Count);
        Assert.Contains("even", categories);
        Assert.Contains("odd", categories);
    }

    [Fact]
    public void Query_DistinctWithOrderBy_DedupsAndSorts()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT DISTINCT category FROM items ORDER BY category");

        var categories = new List<string>();
        while (reader.Read()) categories.Add(reader.GetString(0));

        Assert.Equal(2, categories.Count);
        Assert.Equal("even", categories[0]);
        Assert.Equal("odd", categories[1]);
    }

    // ─── GetOrdinal ──────────────────────────────────────────────

    [Fact]
    public void GetOrdinal_ExistingColumn_ReturnsIndex()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users");

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetOrdinal("name"));
        Assert.Equal(2, reader.GetOrdinal("age"));
    }

    [Fact]
    public void GetOrdinal_CaseInsensitive_ReturnsIndex()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users");

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetOrdinal("NAME"));
        Assert.Equal(2, reader.GetOrdinal("Age"));
    }

    [Fact]
    public void GetOrdinal_NonExistent_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT * FROM users");

        Assert.True(reader.Read());
        Assert.Throws<ArgumentException>(() => reader.GetOrdinal("nonexistent"));
    }

    // ─── Parameters ─────────────────────────────────────────────

    [Fact]
    public void Query_ParameterInteger_BindsCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            new Dictionary<string, object> { ["targetAge"] = 25L },
            "SELECT * FROM users WHERE age = $targetAge");

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_ParameterString_BindsCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            new Dictionary<string, object> { ["targetName"] = "User3" },
            "SELECT * FROM users WHERE name = $targetName");

        Assert.True(reader.Read());
        Assert.Equal(23L, reader.GetInt64(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_MultipleParameters_AllBound()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            new Dictionary<string, object> { ["minAge"] = 25L, ["maxAge"] = 28L },
            "SELECT * FROM users WHERE age > $minAge AND age < $maxAge");

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User6", names);
        Assert.Contains("User7", names);
    }

    [Fact]
    public void Query_MissingParameter_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<ArgumentException>(() => db.Query(
            new Dictionary<string, object>(),
            "SELECT * FROM users WHERE age = $targetAge"));
    }

    [Fact]
    public void Query_ParameterWithCache_ReusesPlan()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        const string sql = "SELECT * FROM users WHERE age = $age";

        using (var reader = db.Query(new Dictionary<string, object> { ["age"] = 25L }, sql))
        {
            Assert.True(reader.Read());
            Assert.Equal("User5", reader.GetString(1));
        }

        // Same SQL, different parameter value — should reuse cached plan
        using (var reader = db.Query(new Dictionary<string, object> { ["age"] = 28L }, sql))
        {
            Assert.True(reader.Read());
            Assert.Equal("User8", reader.GetString(1));
        }
    }

    // ─── Aggregation ──────────────────────────────────────────────

    [Fact]
    public void Query_CountStar_ReturnsSingleRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT COUNT(*) FROM users");

        Assert.True(reader.Read());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_CountWithWhere_CountsFiltered()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT COUNT(*) FROM users WHERE age > 25");

        Assert.True(reader.Read());
        Assert.Equal(5L, reader.GetInt64(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_SumColumn_ReturnsSum()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT SUM(age) FROM users");

        Assert.True(reader.Read());
        // Users 1-10 have ages 21-30, sum = 255
        Assert.Equal(255L, reader.GetInt64(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_AvgColumn_ReturnsAverage()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT AVG(age) FROM users");

        Assert.True(reader.Read());
        Assert.Equal(25.5, reader.GetDouble(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_MinMax_ReturnsExtremes()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT MIN(age), MAX(age) FROM users");

        Assert.True(reader.Read());
        Assert.Equal(21L, reader.GetInt64(0));
        Assert.Equal(30L, reader.GetInt64(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_GroupBy_GroupsAndAggregates()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT category, COUNT(*) FROM items GROUP BY category");

        var groups = new Dictionary<string, long>();
        while (reader.Read())
            groups[reader.GetString(0)] = reader.GetInt64(1);

        Assert.Equal(2, groups.Count);
        Assert.True(groups.ContainsKey("even"));
        Assert.True(groups.ContainsKey("odd"));
    }

    [Fact]
    public void Query_GroupByWithOrderBy_SortsGroups()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query(
            "SELECT category, COUNT(*) FROM items GROUP BY category ORDER BY category");

        Assert.True(reader.Read());
        Assert.Equal("even", reader.GetString(0));
        Assert.True(reader.Read());
        Assert.Equal("odd", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Query_MultipleAggregates_AllComputed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.Query("SELECT COUNT(*), MIN(age), MAX(age), SUM(age) FROM users");

        Assert.True(reader.Read());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.Equal(21L, reader.GetInt64(1));
        Assert.Equal(30L, reader.GetInt64(2));
        Assert.Equal(255L, reader.GetInt64(3));
        Assert.False(reader.Read());
    }

    // ─── T-SQL via Tsql.Translate + Query ───────────────────────

    [Fact]
    public void Query_TsqlTranslated_Works()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var sharq = Sharc.Query.Sharq.Translate.Tsql.Translate(
            "SELECT * FROM users WHERE age = @minAge");

        // The translated query has $minAge which is a parameter.
        // For now, test that translation produces valid syntax.
        Assert.Contains("$minAge", sharq);
    }
}
