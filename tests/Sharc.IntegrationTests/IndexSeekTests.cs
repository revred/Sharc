// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

public class IndexSeekTests
{
    [Fact]
    public void SeekIndex_SingleColumnMatch_FindsCorrectRow()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("items");
        bool found = reader.SeekIndex("idx_items_name", "Item5");

        Assert.True(found);
        Assert.Equal("Item5", reader.GetString(1));
    }

    [Fact]
    public void SeekIndex_NoMatch_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("items");
        bool found = reader.SeekIndex("idx_items_name", "NonExistent");

        Assert.False(found);
    }

    [Fact]
    public void SeekIndex_IntegerKey_FindsCorrectRow()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("events");
        bool found = reader.SeekIndex("idx_events_user_id", 3L);

        Assert.True(found);
        Assert.Equal(3L, reader.GetInt64(1)); // user_id column
    }

    [Fact]
    public void SeekIndex_CategoryFilter_FindsFirstMatch()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("items");
        bool found = reader.SeekIndex("idx_items_category", "even");

        Assert.True(found);
        Assert.Equal("even", reader.GetString(2));
    }

    [Fact]
    public void SeekIndex_AfterSeek_CanContinueReading()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("items");
        bool found = reader.SeekIndex("idx_items_name", "Item1");

        Assert.True(found);
        // After SeekIndex positioned on the row, Read() should advance to the next
        Assert.True(reader.Read());
    }

    [Fact]
    public void SeekIndex_UnknownIndex_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("items");
        Assert.Throws<ArgumentException>(() => reader.SeekIndex("nonexistent_idx", "value"));
    }

    [Fact]
    public void SeekIndex_RealKey_FindsCorrectRow()
    {
        var data = TestDatabaseFactory.CreateIndexedRealDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("points");
        bool found = reader.SeekIndex("idx_points_x", 3.5);

        Assert.True(found);
        Assert.Equal(3.5, reader.GetDouble(1));
    }

    [Fact]
    public void SeekIndex_RealKey_NoMatch_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateIndexedRealDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.CreateReader("points");
        bool found = reader.SeekIndex("idx_points_x", 42.25);

        Assert.False(found);
    }
}
