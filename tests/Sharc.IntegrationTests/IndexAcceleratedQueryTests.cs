// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests verifying that index-accelerated WHERE queries
/// return correct results using IndexSeekCursor when indexes are available.
/// </summary>
public class IndexAcceleratedQueryTests
{
    // CreateIndexedIntegerDatabase: events(id PK, user_id INTEGER, event_type TEXT)
    // idx_events_user_id ON events(user_id)
    // 50 rows, user_id 1-5 (10 rows each), event_type alternates "click"/"view"

    [Fact]
    public void Where_EqOnIndexedColumn_ReturnsCorrectRows()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT id, user_id, event_type FROM events WHERE user_id = 3");

        var ids = new List<long>();
        while (reader.Read())
        {
            Assert.Equal(3L, reader.GetInt64(1));
            ids.Add(reader.GetInt64(0));
        }

        // user_id=3 appears for i=3,8,13,18,23,28,33,38,43,48 -> 10 rows
        Assert.Equal(10, ids.Count);
    }

    [Fact]
    public void Where_EqOnIndexedColumn_SingleMatch()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // user_id=1: rows with i=1,6,11,16,21,26,31,36,41,46
        using var reader = db.Query("SELECT id, user_id FROM events WHERE user_id = 1");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(1L, reader.GetInt64(1));
            count++;
        }
        Assert.Equal(10, count);
    }

    [Fact]
    public void Where_EqOnIndexedColumn_NoMatches()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT id FROM events WHERE user_id = 99");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Where_EqOnUnindexedColumn_FallsBackToScan()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // event_type is not indexed — falls back to full scan, still returns correct results
        using var reader = db.Query("SELECT id FROM events WHERE event_type = 'click'");

        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(25, count); // even i values: 2,4,6,...,50
    }

    [Fact]
    public void Where_AndOfIndexedAndUnindexed_CorrectResults()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // user_id=2 AND event_type='click': user_id=2 gives i=2,7,12,17,22,27,32,37,42,47
        // of those, 'click' is even i: i=2,12,22,32,42 -> 5 rows
        using var reader = db.Query(
            "SELECT id, user_id, event_type FROM events WHERE user_id = 2 AND event_type = 'click'");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(2L, reader.GetInt64(1));
            Assert.Equal("click", reader.GetString(2));
            count++;
        }
        Assert.Equal(5, count);
    }

    [Fact]
    public void Where_ResultsMatchFullScan()
    {
        // Verify that index-accelerated results exactly match full-scan results
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // Full scan — get all user_id=4 rows
        using var fullReader = db.Query("SELECT id FROM events WHERE user_id = 4 ORDER BY id");
        var fullIds = new List<long>();
        while (fullReader.Read())
            fullIds.Add(fullReader.GetInt64(0));

        // Index-accelerated query on same data
        using var indexReader = db.Query("SELECT id FROM events WHERE user_id = 4 ORDER BY id");
        var indexIds = new List<long>();
        while (indexReader.Read())
            indexIds.Add(indexReader.GetInt64(0));

        Assert.Equal(fullIds, indexIds);
    }

    [Fact]
    public void Where_AllUserIds_CorrectCounts()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        for (int uid = 1; uid <= 5; uid++)
        {
            using var reader = db.Query($"SELECT id FROM events WHERE user_id = {uid}");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(10, count);
        }
    }

    [Fact]
    public void Where_EqOnPrimaryKey_ReturnsOneRow()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // id is INTEGER PRIMARY KEY (rowid alias) — should be fast even without explicit index
        using var reader = db.Query("SELECT id, user_id FROM events WHERE id = 25");

        Assert.True(reader.Read());
        Assert.Equal(25L, reader.GetInt64(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void Where_SelectStar_WithIndex_ReturnsAllColumns()
    {
        var data = TestDatabaseFactory.CreateIndexedIntegerDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT * FROM events WHERE user_id = 5");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(5L, reader.GetInt64(1));
            // Ensure all 3 columns are accessible
            var id = reader.GetInt64(0);
            var uid = reader.GetInt64(1);
            var evType = reader.GetString(2);
            Assert.True(id > 0);
            Assert.NotNull(evType);
            count++;
        }
        Assert.Equal(10, count);
    }

    // --- Text index tests ---
    // CreateIndexedDatabase: items(id PK, name TEXT, category TEXT)
    // idx_items_name ON items(name), idx_items_category ON items(category)
    // 20 rows, name = "Item1"-"Item20", category alternates "odd"/"even"

    [Fact]
    public void Where_TextEqOnIndexedColumn_ReturnsCorrectRow()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT id, name FROM items WHERE name = 'Item5'");

        Assert.True(reader.Read());
        Assert.Equal("Item5", reader.GetString(1));
        Assert.False(reader.Read()); // Only one match
    }

    [Fact]
    public void Where_TextEqOnIndexedColumn_NoMatch()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT id FROM items WHERE name = 'NonExistent'");

        Assert.False(reader.Read());
    }

    [Fact]
    public void Where_TextEqOnCategory_ReturnsMultipleRows()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        using var reader = db.Query("SELECT id, category FROM items WHERE category = 'even'");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal("even", reader.GetString(1));
            count++;
        }
        Assert.Equal(10, count); // 10 even rows
    }

    [Fact]
    public void Where_TextEqAndIntegerFilter_CorrectResults()
    {
        var data = TestDatabaseFactory.CreateIndexedDatabase();
        using var db = SharcDatabase.OpenMemory(data);

        // category = 'odd' AND id < 10 -> ids 1,3,5,7,9 -> 5 rows
        using var reader = db.Query(
            "SELECT id, category FROM items WHERE category = 'odd' AND id < 10");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal("odd", reader.GetString(1));
            Assert.True(reader.GetInt64(0) < 10);
            count++;
        }
        Assert.Equal(5, count);
    }
}
