// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Defensive tests for new API hardening: CreateSnapshot, InsertBatch(commitInterval),
/// GetMaxRowId, and edge cases that could cause catastrophic failures if unchecked.
/// </summary>
public sealed class ApiRobustnessTests : IDisposable
{
    private readonly string _dbPath;

    public ApiRobustnessTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharc_robust_{Guid.NewGuid()}.db");
    }

    // ── CreateSnapshot ────────────────────────────────────────────

    [Fact]
    public void CreateSnapshot_SmallDatabase_Succeeds()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath);
        using var snapshot = db.CreateSnapshot();

        using var reader = snapshot.CreateReader("users", "name");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public void CreateSnapshot_ExceedsMaxSnapshotBytes_ThrowsInvalidOperation()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(5);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath);

        // Set an artificially low limit to trigger the guard
        long originalLimit = SharcDatabase.MaxSnapshotBytes;
        try
        {
            SharcDatabase.MaxSnapshotBytes = 100; // 100 bytes — way too small for any real db
            var ex = Assert.Throws<InvalidOperationException>(() => db.CreateSnapshot());
            Assert.Contains("exceeds the maximum snapshot size", ex.Message);
        }
        finally
        {
            SharcDatabase.MaxSnapshotBytes = originalLimit;
        }
    }

    [Fact]
    public void CreateSnapshot_DisposedDatabase_ThrowsObjectDisposed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(1);
        File.WriteAllBytes(_dbPath, data);

        var db = SharcDatabase.Open(_dbPath);
        db.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.CreateSnapshot());
    }

    [Fact]
    public void CreateSnapshot_SnapshotIsIsolated_OriginalChangesNotVisible()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        writer.Insert("users",
            ColumnValue.Null(),
            ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes("Alice")),
            ColumnValue.FromInt64(2, 30),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());

        // Take snapshot with 1 row
        using var snapshot = db.CreateSnapshot();

        // Insert another row into original
        writer.Insert("users",
            ColumnValue.Null(),
            ColumnValue.Text(23, System.Text.Encoding.UTF8.GetBytes("Bob")),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(50.0),
            ColumnValue.Null());

        // Snapshot should still see only 1 row
        using var snapReader = snapshot.CreateReader("users", "name");
        int snapCount = 0;
        while (snapReader.Read()) snapCount++;
        Assert.Equal(1, snapCount);

        // Original should see 2 rows
        using var origReader = db.CreateReader("users", "name");
        int origCount = 0;
        while (origReader.Read()) origCount++;
        Assert.Equal(2, origCount);
    }

    // ── InsertBatch with commitInterval ───────────────────────────

    [Fact]
    public void InsertBatch_WithCommitInterval_NullTableName_ThrowsArgumentNullException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentNullException>(() =>
            writer.InsertBatch(null!, Array.Empty<ColumnValue[]>(), 100));
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_EmptyTableName_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentException>(() =>
            writer.InsertBatch("", Array.Empty<ColumnValue[]>(), 100));
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_NullRecords_ThrowsArgumentNull()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentNullException>(() =>
            writer.InsertBatch("users", null!, 100));
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_ZeroInterval_ThrowsArgumentOutOfRange()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.InsertBatch("users", Array.Empty<ColumnValue[]>(), 0));
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_NegativeInterval_ThrowsArgumentOutOfRange()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.InsertBatch("users", Array.Empty<ColumnValue[]>(), -5));
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_EmptyRecords_ReturnsEmptyArray()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var rowIds = writer.InsertBatch("users", Array.Empty<ColumnValue[]>(), 100);
        Assert.Empty(rowIds);
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_IntervalLargerThanRecordCount_CommitsAll()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var records = new[]
        {
            new[] { ColumnValue.Null(), ColumnValue.Text(25, "Alice"u8.ToArray()), ColumnValue.FromInt64(2, 30), ColumnValue.FromDouble(100.0), ColumnValue.Null() },
            new[] { ColumnValue.Null(), ColumnValue.Text(23, "Bob"u8.ToArray()), ColumnValue.FromInt64(2, 25), ColumnValue.FromDouble(50.0), ColumnValue.Null() },
        };

        var rowIds = writer.InsertBatch("users", records, commitInterval: 999);
        Assert.Equal(2, rowIds.Length);
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_IntervalOf1_CommitsEachRow()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var records = new[]
        {
            new[] { ColumnValue.Null(), ColumnValue.Text(25, "Alice"u8.ToArray()), ColumnValue.FromInt64(2, 30), ColumnValue.FromDouble(100.0), ColumnValue.Null() },
            new[] { ColumnValue.Null(), ColumnValue.Text(23, "Bob"u8.ToArray()), ColumnValue.FromInt64(2, 25), ColumnValue.FromDouble(50.0), ColumnValue.Null() },
            new[] { ColumnValue.Null(), ColumnValue.Text(27, "Carol"u8.ToArray()), ColumnValue.FromInt64(2, 28), ColumnValue.FromDouble(75.0), ColumnValue.Null() },
        };

        var rowIds = writer.InsertBatch("users", records, commitInterval: 1);
        Assert.Equal(3, rowIds.Length);

        // Verify all rows readable
        using var reader = db.CreateReader("users", "name");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public void InsertBatch_WithCommitInterval_DisposedWriter_ThrowsObjectDisposed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        var writer = SharcWriter.From(db);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            writer.InsertBatch("users", Array.Empty<ColumnValue[]>(), 100));
    }

    // ── GetMaxRowId ──────────────────────────────────────────────

    [Fact]
    public void GetMaxRowId_EmptyTable_ReturnsZero()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Equal(0L, writer.GetMaxRowId("users"));
    }

    [Fact]
    public void GetMaxRowId_NonExistentTable_ThrowsInvalidOperation()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<InvalidOperationException>(() => writer.GetMaxRowId("no_such_table"));
    }

    [Fact]
    public void GetMaxRowId_NullTableName_ThrowsArgumentNullException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentNullException>(() => writer.GetMaxRowId(null!));
    }

    [Fact]
    public void GetMaxRowId_EmptyTableName_ThrowsArgumentException()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        Assert.Throws<ArgumentException>(() => writer.GetMaxRowId(""));
    }

    [Fact]
    public void GetMaxRowId_AfterInserts_ReturnsMaxRowId()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        for (int i = 0; i < 5; i++)
        {
            writer.Insert("users",
                ColumnValue.Null(),
                ColumnValue.Text(25, System.Text.Encoding.UTF8.GetBytes($"User{i}")),
                ColumnValue.FromInt64(2, 20 + i),
                ColumnValue.FromDouble(100.0 + i),
                ColumnValue.Null());
        }

        Assert.Equal(5L, writer.GetMaxRowId("users"));
    }

    [Fact]
    public void GetMaxRowId_DisposedWriter_ThrowsObjectDisposed()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        var writer = SharcWriter.From(db);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.GetMaxRowId("users"));
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
