// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for schema mutation persistence — verifies that CREATE TABLE,
/// multi-table creation, schema cookie updates, and DDL + DML in the same
/// transaction all persist correctly across close/reopen cycles.
/// </summary>
public sealed class SchemaMutationStressTests : IDisposable
{
    private readonly string _dbPath;

    public SchemaMutationStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"schema_stress_{Guid.NewGuid()}.db");
    }

    /// <summary>Executes DDL via a single auto-committed transaction.</summary>
    private static void ExecuteDdl(SharcDatabase db, string sql)
    {
        using var tx = db.BeginTransaction();
        tx.Execute(sql);
        tx.Commit();
    }

    [Fact]
    public void CreateTable_ThenInsert_DataReadableAfterReopen()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            ExecuteDdl(db, "CREATE TABLE notes (id INTEGER PRIMARY KEY, content TEXT)");
            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= 50; i++)
            {
                writer.Insert("notes",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"Note_{i:D5}")));
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            var table = db.Schema.GetTable("notes");
            Assert.NotNull(table);
            Assert.Equal(2, table.Columns.Count);

            using var reader = db.CreateReader("notes", "content");
            int count = 0;
            while (reader.Read())
            {
                Assert.StartsWith("Note_", reader.GetString(0));
                count++;
            }
            Assert.Equal(50, count);
        }
    }

    [Fact]
    public void CreateMultipleTables_AllPersisted()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            ExecuteDdl(db, "CREATE TABLE t1 (id INTEGER PRIMARY KEY, val TEXT)");
            ExecuteDdl(db, "CREATE TABLE t2 (id INTEGER PRIMARY KEY, num INTEGER)");
            ExecuteDdl(db, "CREATE TABLE t3 (id INTEGER PRIMARY KEY, data BLOB)");

            using var writer = SharcWriter.From(db);
            writer.Insert("t1", ColumnValue.FromInt64(1, 1), ColumnValue.Text(2 * 5 + 13, "hello"u8.ToArray()));
            writer.Insert("t2", ColumnValue.FromInt64(1, 1), ColumnValue.FromInt64(2, 42));
            writer.Insert("t3", ColumnValue.FromInt64(1, 1), ColumnValue.Blob(2 * 3 + 12, new byte[] { 1, 2, 3 }));
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            Assert.NotNull(db.Schema.GetTable("t1"));
            Assert.NotNull(db.Schema.GetTable("t2"));
            Assert.NotNull(db.Schema.GetTable("t3"));

            using var r1 = db.CreateReader("t1", "val");
            Assert.True(r1.Read());
            Assert.Equal("hello", r1.GetString(0));

            using var r2 = db.CreateReader("t2", "num");
            Assert.True(r2.Read());
            Assert.Equal(42L, r2.GetInt64(0));

            using var r3 = db.CreateReader("t3", "data");
            Assert.True(r3.Read());
            Assert.Equal(new byte[] { 1, 2, 3 }, r3.GetBlob(0));
        }
    }

    [Fact]
    public void DDL_And_DML_InSameTransaction_AllPersisted()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            using var writer = SharcWriter.From(db);
            using var tx = writer.BeginTransaction();

            tx.Execute("CREATE TABLE orders (id INTEGER PRIMARY KEY, product TEXT, qty INTEGER)");

            for (int i = 1; i <= 20; i++)
            {
                tx.Insert("orders",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 10 + 13, System.Text.Encoding.UTF8.GetBytes($"Prod_{i:D4}")),
                    ColumnValue.FromInt64(2, i * 5));
            }

            tx.Commit();
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            Assert.NotNull(db.Schema.GetTable("orders"));

            using var reader = db.CreateReader("orders", "product", "qty");
            int count = 0;
            while (reader.Read())
            {
                Assert.StartsWith("Prod_", reader.GetString(0));
                count++;
            }
            Assert.Equal(20, count);
        }
    }

    [Fact]
    public void DDL_Rollback_TableDoesNotExistAfterReopen()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            ExecuteDdl(db, "CREATE TABLE existing (id INTEGER PRIMARY KEY, val TEXT)");

            using var writer = SharcWriter.From(db);
            using var tx = writer.BeginTransaction();
            tx.Execute("CREATE TABLE phantom (id INTEGER PRIMARY KEY)");
            // No commit — rollback on dispose
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            Assert.NotNull(db.Schema.GetTable("existing"));
            Assert.Throws<KeyNotFoundException>(() => db.Schema.GetTable("phantom"));
        }
    }

    [Fact]
    public void CreateTable_WithLargeData_SplitsCorrectly()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            ExecuteDdl(db, "CREATE TABLE docs (id INTEGER PRIMARY KEY, title TEXT, body TEXT)");

            using var writer = SharcWriter.From(db);
            for (int i = 1; i <= 200; i++)
            {
                string body = $"Document {i} body " + new string('X', 200);
                writer.Insert("docs",
                    ColumnValue.FromInt64(1, i),
                    ColumnValue.Text(2 * 15 + 13, System.Text.Encoding.UTF8.GetBytes($"Title_{i:D5}")),
                    ColumnValue.Text(2 * System.Text.Encoding.UTF8.GetByteCount(body) + 13,
                        System.Text.Encoding.UTF8.GetBytes(body)));
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("docs", "title", "body");
            int count = 0;
            while (reader.Read())
            {
                string title = reader.GetString(0);
                string body = reader.GetString(1);
                Assert.StartsWith("Title_", title);
                Assert.Contains("Document", body);
                count++;
            }
            Assert.Equal(200, count);
        }
    }

    [Fact]
    public void SequentialTransactions_EachPersisted()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            ExecuteDdl(db, "CREATE TABLE log (id INTEGER PRIMARY KEY, msg TEXT)");

            using var writer = SharcWriter.From(db);

            // 5 sequential transactions
            for (int batch = 0; batch < 5; batch++)
            {
                using var tx = writer.BeginTransaction();
                for (int i = 1; i <= 20; i++)
                {
                    int globalId = batch * 20 + i;
                    tx.Insert("log",
                        ColumnValue.FromInt64(1, globalId),
                        ColumnValue.Text(2 * 12 + 13,
                            System.Text.Encoding.UTF8.GetBytes($"Batch{batch}_R{i:D3}")));
                }
                tx.Commit();
            }
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var reader = db.CreateReader("log", "msg");
            int count = 0;
            while (reader.Read())
            {
                string msg = reader.GetString(0);
                Assert.Contains("Batch", msg);
                count++;
            }
            Assert.Equal(100, count);
        }
    }

    [Fact]
    public void CreateTable_InsertIntoMultipleTables_InOneTx()
    {
        using (var db = SharcDatabase.Create(_dbPath))
        {
            using var writer = SharcWriter.From(db);
            using var tx = writer.BeginTransaction();

            tx.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
            tx.Execute("CREATE TABLE roles (id INTEGER PRIMARY KEY, role TEXT)");

            tx.Insert("users", ColumnValue.FromInt64(1, 1), ColumnValue.Text(2 * 5 + 13, "Alice"u8.ToArray()));
            tx.Insert("users", ColumnValue.FromInt64(1, 2), ColumnValue.Text(2 * 3 + 13, "Bob"u8.ToArray()));
            tx.Insert("roles", ColumnValue.FromInt64(1, 1), ColumnValue.Text(2 * 5 + 13, "admin"u8.ToArray()));
            tx.Insert("roles", ColumnValue.FromInt64(1, 2), ColumnValue.Text(2 * 4 + 13, "user"u8.ToArray()));

            tx.Commit();
        }

        using (var db = SharcDatabase.Open(_dbPath))
        {
            using var r1 = db.CreateReader("users", "name");
            int count1 = 0;
            while (r1.Read()) count1++;
            Assert.Equal(2, count1);

            using var r2 = db.CreateReader("roles", "role");
            int count2 = 0;
            while (r2.Read()) count2++;
            Assert.Equal(2, count2);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
