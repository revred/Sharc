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

public sealed class PreparedReaderTests
{
    // ─── Lifecycle ────────────────────────────────────────────────

    [Fact]
    public void PrepareReader_ReturnsNonNull()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.PrepareReader("users");

        Assert.NotNull(prepared);
    }

    [Fact]
    public void PrepareReader_WithProjection_ReturnsNonNull()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        using var prepared = db.PrepareReader("users", "name", "age");

        Assert.NotNull(prepared);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var prepared = db.PrepareReader("users");
        prepared.Dispose();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        var prepared = db.PrepareReader("users");
        prepared.Dispose();
        prepared.Dispose(); // second dispose is no-op
    }

    // ─── CreateReader ─────────────────────────────────────────────

    [Fact]
    public void CreateReader_ReturnsNonNull()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        using var reader = prepared.CreateReader();

        Assert.NotNull(reader);
    }

    [Fact]
    public void CreateReader_ReturnsSameInstance()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        var reader1 = prepared.CreateReader();
        reader1.Dispose();
        var reader2 = prepared.CreateReader();

        Assert.Same(reader1, reader2);
    }

    // ─── Seek Correctness ─────────────────────────────────────────

    [Fact]
    public void Seek_ExistingRow_ReturnsTrue()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.True(reader.Seek(5));
    }

    [Fact]
    public void Seek_NonExistentRow_ReturnsFalse()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.False(reader.Seek(999));
    }

    [Fact]
    public void Seek_ReadsCorrectValue()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.True(reader.Seek(3));
        Assert.Equal("User3", reader.GetString(1)); // name column
        Assert.Equal(23, reader.GetInt32(2));        // age = 20 + 3
    }

    [Fact]
    public void Seek_FirstRow_ReturnsCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.True(reader.Seek(1));
        Assert.Equal("User1", reader.GetString(1));
    }

    [Fact]
    public void Seek_LastRow_ReturnsCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.True(reader.Seek(10));
        Assert.Equal("User10", reader.GetString(1));
    }

    [Fact]
    public void Seek_MultipleSeeks_ReturnCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        Assert.True(reader.Seek(7));
        Assert.Equal("User7", reader.GetString(1));

        Assert.True(reader.Seek(2));
        Assert.Equal("User2", reader.GetString(1));

        Assert.True(reader.Seek(10));
        Assert.Equal("User10", reader.GetString(1));
    }

    // ─── Reuse Across CreateReader Calls ──────────────────────────

    [Fact]
    public void Reuse_SeekAfterReset_ReturnsCorrectData()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        // First use
        var reader = prepared.CreateReader();
        Assert.True(reader.Seek(5));
        Assert.Equal("User5", reader.GetString(1));
        reader.Dispose();

        // Second use — same reader, reset via CreateReader
        reader = prepared.CreateReader();
        Assert.True(reader.Seek(8));
        Assert.Equal("User8", reader.GetString(1));
        reader.Dispose();
    }

    [Fact]
    public void Reuse_ConsistentResults_AcrossMultipleResets()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        for (int i = 0; i < 5; i++)
        {
            var reader = prepared.CreateReader();
            Assert.True(reader.Seek(3));
            Assert.Equal("User3", reader.GetString(1));
            Assert.Equal(23, reader.GetInt32(2));
            reader.Dispose();
        }
    }

    // ─── Read() Scan After Reuse ──────────────────────────────────

    [Fact]
    public void Read_FullScan_ReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();

        int count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(10, count);
    }

    [Fact]
    public void Read_AfterReuse_ReturnsAllRows()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users");

        // First pass: seek
        var reader = prepared.CreateReader();
        Assert.True(reader.Seek(5));
        reader.Dispose();

        // Second pass: full scan
        reader = prepared.CreateReader();
        int count = 0;
        while (reader.Read())
            count++;
        Assert.Equal(10, count);
        reader.Dispose();
    }

    // ─── Projection ───────────────────────────────────────────────

    [Fact]
    public void Projection_ReturnsOnlyRequestedColumns()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users", "name", "age");
        using var reader = prepared.CreateReader();

        Assert.Equal(2, reader.FieldCount);
        Assert.True(reader.Seek(4));
        Assert.Equal("User4", reader.GetString(0)); // name is now ordinal 0
        Assert.Equal(24, reader.GetInt32(1));        // age is now ordinal 1
    }

    [Fact]
    public void Projection_ReusePreservesProjection()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);
        using var prepared = db.PrepareReader("users", "name", "age");

        // First use
        var reader = prepared.CreateReader();
        Assert.True(reader.Seek(1));
        Assert.Equal("User1", reader.GetString(0));
        reader.Dispose();

        // Reuse — projection must still be active
        reader = prepared.CreateReader();
        Assert.Equal(2, reader.FieldCount);
        Assert.True(reader.Seek(6));
        Assert.Equal("User6", reader.GetString(0));
        Assert.Equal(26, reader.GetInt32(1));
        reader.Dispose();
    }

    // ─── MatchesCreateReader ──────────────────────────────────────

    [Fact]
    public void Seek_MatchesCreateReaderResults()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(10);
        using var db = SharcDatabase.OpenMemory(data);

        // Get expected values from CreateReader
        using var baseline = db.CreateReader("users");
        Assert.True(baseline.Seek(5));
        var expectedName = baseline.GetString(1);
        var expectedAge = baseline.GetInt32(2);

        // Verify PreparedReader returns identical results
        using var prepared = db.PrepareReader("users");
        using var reader = prepared.CreateReader();
        Assert.True(reader.Seek(5));
        Assert.Equal(expectedName, reader.GetString(1));
        Assert.Equal(expectedAge, reader.GetInt32(2));
    }
}