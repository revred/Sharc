// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Query;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public sealed class FilterIntegrationTests
{
    [Fact]
    public void CreateReader_FilterEqualInt_ReturnsMatchingRows()
    {
        // Users: age = 20+i, so User5 has age=25
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("age", SharcOperator.Equal, 25L));

        Assert.True(reader.Read());
        Assert.Equal("User5", reader.GetString(1));
        Assert.False(reader.Read()); // only one match
    }

    [Fact]
    public void CreateReader_FilterGreaterThanInt_ReturnsMatchingRows()
    {
        // Users: age = 21..30, filter age > 28 → User9 (29), User10 (30)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("age", SharcOperator.GreaterThan, 28L));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Equal(2, names.Count);
        Assert.Contains("User9", names);
        Assert.Contains("User10", names);
    }

    [Fact]
    public void CreateReader_FilterEqualText_ReturnsMatchingRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("name", SharcOperator.Equal, "User3"));

        Assert.True(reader.Read());
        Assert.Equal(23L, reader.GetInt64(2)); // age = 20+3
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterEqualReal_ReturnsMatchingRows()
    {
        // balance = 100.50 + i, so User7 has balance=107.50
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("balance", SharcOperator.Equal, 107.50));

        Assert.True(reader.Read());
        Assert.Equal("User7", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_MultipleFilters_AndSemantics()
    {
        // age > 22 AND age < 26 → User3 (23), User4 (24), User5 (25)
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("age", SharcOperator.GreaterThan, 22L),
            new SharcFilter("age", SharcOperator.LessThan, 26L));

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    [Fact]
    public void CreateReader_FilterNoMatches_ReadReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users",
            new SharcFilter("age", SharcOperator.Equal, 999L));

        Assert.False(reader.Read());
    }

    [Fact]
    public void CreateReader_FilterWithProjection_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        var columns = new[] { "name", "age" };
        var filters = new[] { new SharcFilter("age", SharcOperator.LessOrEqual, 22L) };
        using var reader = db.CreateReader("users", columns, filters);

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0)); // projected column 0 = name

        // age 21, 22 → User1, User2
        Assert.Equal(2, names.Count);
        Assert.Contains("User1", names);
        Assert.Contains("User2", names);
    }

    [Fact]
    public void CreateReader_FilterInvalidColumn_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        using var db = SharcDatabase.OpenMemory(data);

        Assert.Throws<ArgumentException>(() =>
            db.CreateReader("users",
                new SharcFilter("nonexistent", SharcOperator.Equal, 1L)));
    }
}
