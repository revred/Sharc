// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Buffers;
using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using Xunit;

namespace Sharc.Tests.Write;

internal static class SharcDatabaseExtensions
{
    public static void Execute(this SharcDatabase db, string sql, AgentInfo? agent = null)
    {
        using var tx = db.BeginTransaction();
        tx.Execute(sql, agent);
        tx.Commit();
    }
}

public sealed class SharcSchemaWriterTests
{
    private const int PageSize = 4096;

    private static SharcDatabase CreateTestDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
        return SharcDatabase.Create(path);
    }

    [Fact]
    public void CreateTable_IfNotExists_DoesNotThrowIfTableExists()
    {
        using var db = CreateTestDatabase();
        Assert.Equal(5, db.Schema.Tables.Count); // sqlite_master + 4 sharc system tables
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        Assert.Equal(6, db.Schema.Tables.Count);
        
        // Should not throw
        db.Execute("CREATE TABLE IF NOT EXISTS T1 (id INTEGER)");
    }

    [Fact]
    public void CreateTable_ThrowsIfTableExistsWithoutIfNotExists()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        
        Assert.Throws<InvalidOperationException>(() => db.Execute("CREATE TABLE T1 (id INTEGER)"));
    }

    [Fact]
    public void AlterTable_AddColumn_UpdatesSqliteMaster()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER)");
        db.Execute("ALTER TABLE T1 ADD COLUMN name TEXT");

        var table = db.Schema.GetTable("T1");
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("name", table.Columns[1].Name);
        Assert.Equal("TEXT", table.Columns[1].DeclaredType);

        // Verify the SQL in sqlite_master is updated
        using var reader = db.CreateReader("sqlite_master");
        bool found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "T1")
            {
                string sql = reader.GetString(4);
                Assert.Contains("name TEXT", sql);
                found = true;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void AlterTable_RenameTo_UpdatesSqliteMaster()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE OldName (id INTEGER)");
        db.Execute("ALTER TABLE OldName RENAME TO NewName");

        Assert.Throws<KeyNotFoundException>(() => db.Schema.GetTable("OldName"));
        var table = db.Schema.GetTable("NewName");
        Assert.NotNull(table);

        // Verify sqlite_master
        using var reader = db.CreateReader("sqlite_master");
        bool found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "NewName")
            {
                Assert.Equal("table", reader.GetString(0));
                Assert.Equal("NewName", reader.GetString(2)); // tbl_name
                Assert.Contains("CREATE TABLE NewName", reader.GetString(4));
                found = true;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void CreateTable_QuotedIdentifiers_HandledCorrectly()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE \"Complex Table Name\" ([Wrapped Col] INTEGER)");

        var table = db.Schema.GetTable("Complex Table Name");
        Assert.NotNull(table);
        Assert.Equal("Wrapped Col", table.Columns[0].Name);
    }

    [Fact]
    public void CreateTable_AgentEnforcement_ThrowsIfUnauthorized()
    {
        using var db = CreateTestDatabase();
        var agent = new AgentInfo(
            "UnauthorizedAgent", 
            AgentClass.User, 
            new byte[32], 
            0, // AuthorityCeiling
            "", // WriteScope
            "", // ReadScope
            0, 0, // Validity
            "", // Parent
            false, // CoSign
            new byte[64] // Signature
        );

        Assert.Throws<UnauthorizedAccessException>(() => db.Execute("CREATE TABLE Forbidden (id INTEGER)", agent));
    }

    // ── CREATE INDEX tests ───────────────────────────────────────────

    [Fact]
    public void CreateIndex_EmptyTable_RegistersInSchema()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx_t1_name ON T1(name)");

        Assert.Single(db.Schema.Indexes);
        Assert.Equal("idx_t1_name", db.Schema.Indexes[0].Name);
        Assert.Equal("T1", db.Schema.Indexes[0].TableName);
        Assert.Single(db.Schema.Indexes[0].Columns);
        Assert.Equal("name", db.Schema.Indexes[0].Columns[0].Name);
    }

    [Fact]
    public void CreateIndex_IfNotExists_DoesNotThrowIfExists()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx_t1_name ON T1(name)");

        // Should not throw
        db.Execute("CREATE INDEX IF NOT EXISTS idx_t1_name ON T1(name)");
    }

    [Fact]
    public void CreateIndex_ThrowsIfIndexExists()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx_t1_name ON T1(name)");

        Assert.Throws<InvalidOperationException>(() =>
            db.Execute("CREATE INDEX idx_t1_name ON T1(name)"));
    }

    [Fact]
    public void CreateUniqueIndex_SetsIsUniqueFlag()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE UNIQUE INDEX idx_t1_name ON T1(name)");

        Assert.True(db.Schema.Indexes[0].IsUnique);
    }

    [Fact]
    public void CreateIndex_InvalidTable_Throws()
    {
        using var db = CreateTestDatabase();

        Assert.Throws<KeyNotFoundException>(() =>
            db.Execute("CREATE INDEX idx ON nonexistent(col)"));
    }

    [Fact]
    public void CreateIndex_InvalidColumn_Throws()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY)");

        Assert.Throws<ArgumentException>(() =>
            db.Execute("CREATE INDEX idx ON T1(missing_col)"));
    }

    [Fact]
    public void CreateIndex_MultiColumn_RegistersAllColumns()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, a TEXT, b TEXT, c TEXT)");
        db.Execute("CREATE INDEX idx_t1_abc ON T1(a, b, c)");

        Assert.Equal(3, db.Schema.Indexes[0].Columns.Count);
        Assert.Equal("a", db.Schema.Indexes[0].Columns[0].Name);
        Assert.Equal("b", db.Schema.Indexes[0].Columns[1].Name);
        Assert.Equal("c", db.Schema.Indexes[0].Columns[2].Name);
    }

    [Fact]
    public void CreateIndex_RootPage_IsLeafIndex()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx ON T1(name)");

        // Read the root page and verify it has LeafIndex type (0x0A)
        Span<byte> page = stackalloc byte[PageSize];
        db.PageSource.ReadPage((uint)db.Schema.Indexes[0].RootPage, page);
        Assert.Equal(0x0A, page[0]); // BTreePageType.LeafIndex
    }

    [Fact]
    public void CreateIndex_SqliteMaster_HasCorrectFields()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx ON T1(name)");

        using var reader = db.CreateReader("sqlite_master");
        bool found = false;
        while (reader.Read())
        {
            if (reader.GetString(0) == "index" && reader.GetString(1) == "idx")
            {
                Assert.Equal("T1", reader.GetString(2)); // tbl_name
                Assert.True(reader.GetInt64(3) > 1);     // rootpage > 1
                Assert.Contains("CREATE INDEX", reader.GetString(4));
                found = true;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_PopulatesIndex()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");

        // Insert rows first
        using var writer = SharcWriter.From(db);
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 5 + 13, System.Text.Encoding.UTF8.GetBytes("Alice")));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 3 + 13, System.Text.Encoding.UTF8.GetBytes("Bob")));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 7 + 13, System.Text.Encoding.UTF8.GetBytes("Charlie")));

        // CREATE INDEX on non-empty table should now succeed
        db.Execute("CREATE INDEX idx_name ON T1(name)");

        // Verify index exists in schema
        var index = db.Schema.Indexes.First(i => i.Name == "idx_name");
        Assert.Equal("T1", index.TableName);

        // Verify index B-tree contains all 3 entries in sorted order
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);
        var entries = new List<string>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            // Index record = [indexed_column, rowid]
            entries.Add(record[0].AsString());
        }

        Assert.Equal(3, entries.Count);
        // BINARY collation: Alice < Bob < Charlie
        Assert.Equal("Alice", entries[0]);
        Assert.Equal("Bob", entries[1]);
        Assert.Equal("Charlie", entries[2]);
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_IntegerColumn_PopulatesIndex()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, score INTEGER)");

        using var writer = SharcWriter.From(db);
        writer.Insert("T1", ColumnValue.Null(), ColumnValue.FromInt64(4, 30));
        writer.Insert("T1", ColumnValue.Null(), ColumnValue.FromInt64(4, 10));
        writer.Insert("T1", ColumnValue.Null(), ColumnValue.FromInt64(4, 20));

        db.Execute("CREATE INDEX idx_score ON T1(score)");

        // Verify via index cursor — entries should be sorted by score
        var index = db.Schema.Indexes.First(i => i.Name == "idx_score");
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);
        var scores = new List<long>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            scores.Add(record[0].AsInt64());
        }

        Assert.Equal(3, scores.Count);
        Assert.Equal(10, scores[0]);
        Assert.Equal(20, scores[1]);
        Assert.Equal(30, scores[2]);
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_LargeTable_SplitsCorrectly()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");

        // Insert 200+ rows to force page splits in the index B-tree
        using var writer = SharcWriter.From(db);
        for (int i = 0; i < 250; i++)
        {
            string name = $"name_{i:D4}"; // zero-padded for predictable sort order
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            writer.Insert("T1",
                ColumnValue.Null(),
                ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes));
        }

        db.Execute("CREATE INDEX idx_name ON T1(name)");

        // Verify all 250 entries are present and in sorted order
        var index = db.Schema.Indexes.First(i => i.Name == "idx_name");
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);
        var entries = new List<string>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            entries.Add(record[0].AsString());
        }

        Assert.Equal(250, entries.Count);
        // Verify sorted (BINARY collation)
        for (int i = 1; i < entries.Count; i++)
        {
            Assert.True(
                string.CompareOrdinal(entries[i - 1], entries[i]) < 0,
                $"Entry {i - 1} ('{entries[i - 1]}') should be < entry {i} ('{entries[i]}')");
        }
    }

    [Fact]
    public void CreateIndex_UniqueIndex_NonEmptyTable_PopulatesIndex()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, email TEXT)");

        using var writer = SharcWriter.From(db);
        var aliceBytes = System.Text.Encoding.UTF8.GetBytes("alice@test.com");
        var bobBytes = System.Text.Encoding.UTF8.GetBytes("bob@test.com");
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * aliceBytes.Length + 13, aliceBytes));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * bobBytes.Length + 13, bobBytes));

        db.Execute("CREATE UNIQUE INDEX idx_email ON T1(email)");

        // Verify index is unique and populated
        var index = db.Schema.Indexes.First(i => i.Name == "idx_email");
        Assert.True(index.IsUnique);

        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);
        var entries = new List<string>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            entries.Add(record[0].AsString());
        }

        Assert.Equal(2, entries.Count);
        Assert.Equal("alice@test.com", entries[0]);
        Assert.Equal("bob@test.com", entries[1]);
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_TrailingRowIdInRecord()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");

        using var writer = SharcWriter.From(db);
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 5 + 13, System.Text.Encoding.UTF8.GetBytes("Alice")));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 3 + 13, System.Text.Encoding.UTF8.GetBytes("Bob")));

        db.Execute("CREATE INDEX idx ON T1(name)");

        // Verify the trailing rowid is present in each index record
        var index = db.Schema.Indexes.First(i => i.Name == "idx");
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            // Index record has [name, rowid]
            Assert.Equal(2, record.Length);
            rowIds.Add(record[1].AsInt64());
        }

        Assert.Equal(2, rowIds.Count);
        // Sorted by name (BINARY): "Alice" < "Bob"
        // Alice was inserted first (rowid=1), Bob second (rowid=2)
        Assert.Equal(1, rowIds[0]); // Alice's rowid
        Assert.Equal(2, rowIds[1]); // Bob's rowid
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_MultiColumn_SortedByAllColumns()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, a TEXT, b TEXT)");

        using var writer = SharcWriter.From(db);
        // Insert with same first column, different second column
        var xBytes = System.Text.Encoding.UTF8.GetBytes("X");
        var aBytes = System.Text.Encoding.UTF8.GetBytes("A");
        var bBytes = System.Text.Encoding.UTF8.GetBytes("B");
        var yBytes = System.Text.Encoding.UTF8.GetBytes("Y");
        var cBytes = System.Text.Encoding.UTF8.GetBytes("C");

        writer.Insert("T1", ColumnValue.Null(),
            ColumnValue.Text(2 * 1 + 13, xBytes),
            ColumnValue.Text(2 * 1 + 13, bBytes)); // X, B
        writer.Insert("T1", ColumnValue.Null(),
            ColumnValue.Text(2 * 1 + 13, xBytes),
            ColumnValue.Text(2 * 1 + 13, aBytes)); // X, A
        writer.Insert("T1", ColumnValue.Null(),
            ColumnValue.Text(2 * 1 + 13, yBytes),
            ColumnValue.Text(2 * 1 + 13, cBytes)); // Y, C

        db.Execute("CREATE INDEX idx_ab ON T1(a, b)");

        var index = db.Schema.Indexes.First(i => i.Name == "idx_ab");
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);

        var entries = new List<(string a, string b)>();
        while (cursor.MoveNext())
        {
            var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
            // Multi-column index: [a, b, rowid]
            Assert.Equal(3, record.Length);
            entries.Add((record[0].AsString(), record[1].AsString()));
        }

        Assert.Equal(3, entries.Count);
        // Sorted by record payload (BINARY): "X,A" < "X,B" < "Y,C"
        Assert.Equal(("X", "A"), entries[0]);
        Assert.Equal(("X", "B"), entries[1]);
        Assert.Equal(("Y", "C"), entries[2]);
    }

    [Fact]
    public void CreateIndex_NonEmptyTable_SeekFindsCorrectEntry()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");

        using var writer = SharcWriter.From(db);
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 5 + 13, System.Text.Encoding.UTF8.GetBytes("Alice")));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 3 + 13, System.Text.Encoding.UTF8.GetBytes("Bob")));
        writer.Insert("T1",
            ColumnValue.Null(),
            ColumnValue.Text(2 * 7 + 13, System.Text.Encoding.UTF8.GetBytes("Charlie")));

        db.Execute("CREATE INDEX idx ON T1(name)");

        // Verify SeekFirst works on the populated index
        var index = db.Schema.Indexes.First(i => i.Name == "idx");
        using var cursor = db.BTreeReader.CreateIndexCursor((uint)index.RootPage);

        bool found = cursor.SeekFirst(System.Text.Encoding.UTF8.GetBytes("Bob"));
        Assert.True(found);

        var record = db.RecordDecoder.DecodeRecord(cursor.Payload);
        Assert.Equal("Bob", record[0].AsString());
        Assert.Equal(2, record[1].AsInt64()); // Bob's rowid
    }

    [Fact]
    public void CreateIndex_LinkedToTable_ViaTableInfoIndexes()
    {
        using var db = CreateTestDatabase();
        db.Execute("CREATE TABLE T1 (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE INDEX idx ON T1(name)");

        var table = db.Schema.GetTable("T1");
        Assert.Single(table.Indexes);
        Assert.Equal("idx", table.Indexes[0].Name);
    }
}
