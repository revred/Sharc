// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for the transaction lifecycle — verifies commit/rollback persistence,
/// interleaved DML, page count consistency after grow + rollback, and multi-transaction
/// sequential correctness.
/// </summary>
public sealed class TransactionStressTests : IDisposable
{
    private readonly string _dbPath;

    public TransactionStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tx_stress_{Guid.NewGuid()}.db");
    }

    [Fact]
    public void Commit_PersistsAllInserts_ReadableAfterReopen()
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
                    ColumnValue.FromInt64(2, 25),
                    ColumnValue.FromDouble(100.0),
                    ColumnValue.Null());
            }
            tx.Commit();
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(rowCount, count);
        }
    }

    [Fact]
    public void Rollback_DiscardsAllInserts_EmptyAfterReopen()
    {
        const int rowCount = 200;
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
                    ColumnValue.FromInt64(2, 25),
                    ColumnValue.FromDouble(100.0),
                    ColumnValue.Null());
            }
            // Explicit rollback — no commit
            tx.Rollback();
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public void DisposeWithoutCommit_ImplicitRollback()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            using (var tx = writer.BeginTransaction())
            {
                for (int i = 1; i <= 100; i++)
                {
                    tx.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                // tx.Dispose() without Commit — should rollback
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            Assert.False(reader.Read());
        }
    }

    [Fact]
    public void CommitThenInsertMore_BothBatchesPersisted()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // First transaction
            using (var tx1 = writer.BeginTransaction())
            {
                for (int i = 1; i <= 100; i++)
                {
                    tx1.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                tx1.Commit();
            }

            // Second transaction
            using (var tx2 = writer.BeginTransaction())
            {
                for (int i = 101; i <= 200; i++)
                {
                    tx2.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                tx2.Commit();
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(200, count);
        }
    }

    [Fact]
    public void CommitThenRollback_OnlyFirstBatchPersisted()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // First transaction: committed
            using (var tx1 = writer.BeginTransaction())
            {
                for (int i = 1; i <= 50; i++)
                {
                    tx1.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                tx1.Commit();
            }

            // Second transaction: rolled back
            using (var tx2 = writer.BeginTransaction())
            {
                for (int i = 51; i <= 100; i++)
                {
                    tx2.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                // No commit — rollback on dispose
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(50, count);
        }
    }

    [Fact]
    public void Transaction_InsertThenDeleteWithinSameTx_NetResultPersisted()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);
            using var tx = writer.BeginTransaction();

            // Insert 100 rows
            for (int i = 1; i <= 100; i++)
            {
                tx.Insert("users",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"User_{i:D5}")),
                    ColumnValue.FromInt64(2, 25),
                    ColumnValue.FromDouble(100.0),
                    ColumnValue.Null());
            }

            // Delete first 30 within same transaction
            for (int i = 1; i <= 30; i++)
                tx.Delete("users", i);

            tx.Commit();
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users");
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(70, count);
        }
    }

    [Fact]
    public void Transaction_UpdateWithinTx_CommittedValuesReadable()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using (var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true }))
        {
            using var writer = SharcWriter.From(db);

            // First: insert base rows
            using (var tx1 = writer.BeginTransaction())
            {
                for (int i = 1; i <= 50; i++)
                {
                    tx1.Insert("users",
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 8 + 13, System.Text.Encoding.UTF8.GetBytes($"Old_{i:D5}")),
                        ColumnValue.FromInt64(2, 25),
                        ColumnValue.FromDouble(100.0),
                        ColumnValue.Null());
                }
                tx1.Commit();
            }

            // Second: update all rows
            using (var tx2 = writer.BeginTransaction())
            {
                for (int i = 1; i <= 50; i++)
                {
                    tx2.Update("users", i,
                        ColumnValue.FromInt64(1, i),
                        ColumnValue.Text(2 * 8 + 13, System.Text.Encoding.UTF8.GetBytes($"New_{i:D5}")),
                        ColumnValue.FromInt64(2, 30),
                        ColumnValue.FromDouble(200.0),
                        ColumnValue.Null());
                }
                tx2.Commit();
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("users", "name");
            int count = 0;
            while (reader.Read())
            {
                string name = reader.GetString(0);
                Assert.StartsWith("New_", name);
                count++;
            }
            Assert.Equal(50, count);
        }
    }

    [Fact]
    public void OperationAfterCommit_Throws()
    {
        var data = TestDatabaseFactory.CreateUsersDatabase(0);
        File.WriteAllBytes(_dbPath, data);

        using var db = SharcDatabase.Open(_dbPath, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);
        using var tx = writer.BeginTransaction();

        tx.Insert("users",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Text(2 * 4 + 13, "test"u8.ToArray()),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(100.0),
            ColumnValue.Null());
        tx.Commit();

        Assert.Throws<InvalidOperationException>(() =>
            tx.Insert("users",
                ColumnValue.FromInt64(1, 2),
                ColumnValue.Text(2 * 4 + 13, "bad"u8.ToArray()),
                ColumnValue.FromInt64(2, 25),
                ColumnValue.FromDouble(100.0),
                ColumnValue.Null()));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
