// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests that <see cref="SharcDatabase.CreateReader(string, string[]?, SharcFilter[]?, IFilterStar?)"/>
/// reuses pooled readers after warmup, eliminating allocation on repeated calls.
/// </summary>
public sealed class ReaderPoolTests
{
    // ─── Pool Reuse ────────────────────────────────────────────────

    [Fact]
    public void CreateReader_SecondCall_ReturnsSameReaderInstance()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // First call — allocates
        var reader1 = db.CreateReader("users");
        reader1.Dispose();

        // Second call — should reuse from pool
        var reader2 = db.CreateReader("users");
        reader2.Dispose();

        Assert.Same(reader1, reader2);
    }

    [Fact]
    public void CreateReader_PooledReader_SeekReturnsCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Warmup — populate pool
        var reader = db.CreateReader("users");
        Assert.True(reader.Seek(5));
        Assert.Equal("User5", reader.GetString(1));
        reader.Dispose();

        // Second call — from pool
        reader = db.CreateReader("users");
        Assert.True(reader.Seek(3));
        Assert.Equal("User3", reader.GetString(1));
        reader.Dispose();
    }

    [Fact]
    public void CreateReader_PooledReader_FullScanReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Warmup
        var reader = db.CreateReader("users");
        while (reader.Read()) { }
        reader.Dispose();

        // Reuse — full scan
        reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read()) count++;
        reader.Dispose();

        Assert.Equal(10, count);
    }

    [Fact]
    public void CreateReader_TwoSlots_BothReused()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Fill both pool slots with same-table readers
        var reader1 = db.CreateReader("users");
        var reader2 = db.CreateReader("users");
        reader1.Dispose();
        reader2.Dispose();

        // Both should be recycled
        var reader3 = db.CreateReader("users");
        var reader4 = db.CreateReader("users");
        reader3.Dispose();
        reader4.Dispose();

        // At least one must be reused (pool has 2 slots)
        Assert.True(
            ReferenceEquals(reader1, reader3) || ReferenceEquals(reader1, reader4) ||
            ReferenceEquals(reader2, reader3) || ReferenceEquals(reader2, reader4));
    }

    // ─── No Pooling for Filtered/Projected ─────────────────────────

    [Fact]
    public void CreateReader_WithProjection_NotPooled()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Projected readers should NOT be pooled (different field configuration)
        var reader1 = db.CreateReader("users", "name");
        reader1.Dispose();

        var reader2 = db.CreateReader("users", "name");
        reader2.Dispose();

        // Projected readers bypass pool — different instances
        Assert.NotSame(reader1, reader2);
    }

    // ─── Correctness After Multiple Cycles ─────────────────────────

    [Fact]
    public void CreateReader_MultipleCycles_ConsistentResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        for (int i = 0; i < 10; i++)
        {
            using var reader = db.CreateReader("users");
            Assert.True(reader.Seek(7));
            Assert.Equal("User7", reader.GetString(1));
            Assert.Equal(27, reader.GetInt32(2));
        }
    }

    [Fact]
    public void CreateReader_SeekAfterScan_WorksCorrectly()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // First: full scan
        var reader = db.CreateReader("users");
        while (reader.Read()) { }
        reader.Dispose();

        // Second: seek (from pool)
        reader = db.CreateReader("users");
        Assert.True(reader.Seek(1));
        Assert.Equal("User1", reader.GetString(1));
        reader.Dispose();

        // Third: full scan again (from pool)
        reader = db.CreateReader("users");
        int count = 0;
        while (reader.Read()) count++;
        reader.Dispose();

        Assert.Equal(10, count);
    }

    [Fact]
    public void CreateReader_PooledReader_AllColumnsAccessible()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Warmup
        var reader = db.CreateReader("users");
        reader.Dispose();

        // Verify all columns accessible on pooled reader
        reader = db.CreateReader("users");
        Assert.True(reader.Seek(2));
        var id = reader.GetInt64(0);
        var name = reader.GetString(1);
        var age = reader.GetInt32(2);
        var balance = reader.GetDouble(3);
        reader.Dispose();

        Assert.Equal(2, id);
        Assert.Equal("User2", name);
        Assert.Equal(22, age);
        Assert.Equal(102.5, balance);
    }

    // ─── Pool Does Not Interfere With PreparedReader ───────────────

    [Fact]
    public void CreateReader_DoesNotConflictWithPreparedReader()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        // CreateReader uses pool
        var coldReader = db.CreateReader("users");
        Assert.True(coldReader.Seek(5));
        Assert.Equal("User5", coldReader.GetString(1));
        coldReader.Dispose();

        // PreparedReader uses its own reusable reader
        using var hotReader = prepared.CreateReader();
        Assert.True(hotReader.Seek(5));
        Assert.Equal("User5", hotReader.GetString(1));

        // Pool reuse still works
        var coldReader2 = db.CreateReader("users");
        Assert.Same(coldReader, coldReader2);
        Assert.True(coldReader2.Seek(3));
        Assert.Equal("User3", coldReader2.GetString(1));
        coldReader2.Dispose();
    }
}