// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;
using Sharc.Core;
using Sharc.IntegrationTests.Helpers;

namespace Sharc.IntegrationTests;

/// <summary>
/// Tests that B-tree root splits are persisted to sqlite_master.
/// When enough rows are inserted to overflow the initial leaf page, a new root
/// interior page is created. The new root page number must be written back to
/// sqlite_master so that the table is still accessible after close/reopen.
/// </summary>
public sealed class RootSplitPersistenceTests : IDisposable
{
    private readonly string _dbPath;

    public RootSplitPersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"root_split_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void InsertMany_CausesRootSplit_DataReadableAfterReopen()
    {
        const int rowCount = 200;
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        // Insert enough rows to trigger a root split (leaf page holds ~50-100 rows)
        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= rowCount; i++)
            {
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                    ColumnValue.FromInt64(2, 20 + (i % 60)),
                    ColumnValue.FromDouble(1000.0 + i),
                    ColumnValue.Null());
            }
        }

        // Reopen from file — sqlite_master must point to the new root page
        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(rowCount, count);
        }
    }

    [Fact]
    public void InsertMany_InTransaction_RootSplitPersisted()
    {
        const int rowCount = 300;
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            using var tx = writer.BeginTransaction();
            for (int i = 1; i <= rowCount; i++)
            {
                tx.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                    ColumnValue.FromInt64(2, 20 + (i % 60)),
                    ColumnValue.FromDouble(1000.0 + i),
                    ColumnValue.Null());
            }
            tx.Commit();
        }

        // Reopen and verify all rows
        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name", "age");
            int count = 0;
            while (reader.Read())
            {
                count++;
                string name = reader.GetString(0);
                Assert.StartsWith("User_", name);
            }
            Assert.Equal(rowCount, count);
        }
    }

    [Fact]
    public void InsertMany_ThenDeleteSome_ThenReopen_DataConsistent()
    {
        const int insertCount = 150;
        const int deleteCount = 50;
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // Insert enough to split
            for (int i = 1; i <= insertCount; i++)
            {
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                    ColumnValue.FromInt64(2, 25),
                    ColumnValue.FromDouble(100.0),
                    ColumnValue.Null());
            }

            // Delete first 50 rows
            for (int i = 1; i <= deleteCount; i++)
            {
                bool found = writer.Delete("users", i);
                Assert.True(found);
            }
        }

        // Reopen and verify remaining rows
        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(insertCount - deleteCount, count);
        }
    }

    [Fact]
    public void MultipleRootSplits_LargeInsert_AllRowsAccessible()
    {
        // 1000 rows should cause multiple levels of B-tree splits
        const int rowCount = 1000;
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= rowCount; i++)
            {
                writer.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 12 + 13, System.Text.Encoding.UTF8.GetBytes($"LargeUser_{i:D5}")),
                    ColumnValue.FromInt64(2, i % 100),
                    ColumnValue.FromDouble(i * 1.5),
                    ColumnValue.Null());
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(rowCount, count);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
