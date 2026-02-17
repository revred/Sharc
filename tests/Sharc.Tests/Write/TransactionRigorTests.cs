using System.IO;
using Sharc.Core;
using Sharc.Core.IO;
using Sharc.Core.Trust;
using Xunit;

namespace Sharc.Tests.Write;

public sealed class TransactionRigorTests
{
    private const int PageSize = 4096;

    private static SharcDatabase CreateTestDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
        return SharcDatabase.Create(path);
    }

    [Fact]
    public void Rollback_AfterLargeDataInsertion_RestoresPreviousState()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE Users (id INTEGER PRIMARY KEY, name TEXT)");

        // 1. Initial state: 5 system tables + Users
        Assert.Equal(6, db.Schema.Tables.Count);
        Assert.Contains(db.Schema.Tables, t => t.Name == "sqlite_master");
        Assert.Contains(db.Schema.Tables, t => t.Name == "Users");

        // 2. Start transaction and insert many rows
        using (var writer = SharcWriter.From(db))
        using (var tx = writer.BeginTransaction())
        {
            for (int i = 1; i <= 1000; i++)
            {
                tx.Insert("Users", ColumnValue.FromInt64(0, i), ColumnValue.Text(0, System.Text.Encoding.UTF8.GetBytes($"User{i}")));
            }
            // NO COMMIT
        }

        // 3. Verify they are NOT there
        using (var reader = db.CreateReader("Users"))
        {
            Assert.False(reader.Read());
        }
    }


    [Fact]
    public void AtomicCommit_DDL_And_DML_Together()
    {
        using var db = CreateTestDatabase();
        
        using (var writer = SharcWriter.From(db))
        using (var tx = writer.BeginTransaction())
        {
            tx.Execute("CREATE TABLE T1 (id INTEGER)");
            tx.Insert("T1", ColumnValue.FromInt64(0, 42));
            tx.Commit();
        }

        // Verify both schema update and data write are persisted
        var table = db.Schema.GetTable("T1");
        Assert.NotNull(table);
        
        using (var reader = db.CreateReader("T1"))
        {
            Assert.True(reader.Read());
            Assert.Equal(42L, reader.GetInt64(0));
        }
    }

    [Fact]
    public void Rollback_DDL_PreservesOldSchema()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE Existing (id INTEGER)");

        using (var writer = SharcWriter.From(db))
        using (var tx = writer.BeginTransaction())
        {
            tx.Execute("CREATE TABLE NewOne (id INTEGER)");
            // NO COMMIT
        }

        // Should still only have 5 system tables and "Existing"
        Assert.Equal(6, db.Schema.Tables.Count);
        Assert.Contains(db.Schema.Tables, t => t.Name == "Existing");
        Assert.Throws<KeyNotFoundException>(() => db.Schema.GetTable("NewOne"));
    }

    [Fact]
    public void ConcurrentTransactions_AreSerializedByLock()
    {
        // Sharc uses a single-writer lock. Let's verify that starting a second write fails.
        using var db = CreateTestDatabase();
        using var writer = SharcWriter.From(db);
        using var tx1 = writer.BeginTransaction();
        
        Assert.ThrowsAny<Exception>(() => writer.BeginTransaction());
    }
}
