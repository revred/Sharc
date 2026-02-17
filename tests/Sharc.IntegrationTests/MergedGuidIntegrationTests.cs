/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Integration tests for merged GUID columns (__hi/__lo convention):
/// schema detection, read path (GetGuid dual dispatch), and write path (ExpandMergedColumns).
/// </summary>
public class MergedGuidIntegrationTests
{
    // ─── Schema Detection ───

    [Fact]
    public void Schema_HiLoColumns_DetectedAsMergedGuid()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE entities (id INTEGER PRIMARY KEY, owner_guid__hi INTEGER NOT NULL, owner_guid__lo INTEGER NOT NULL)");
        });

        using var db = SharcDatabase.OpenMemory(data);
        var table = db.Schema.GetTable("entities");

        // Logical columns: [id, owner_guid]
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal("owner_guid", table.Columns[1].Name);
        Assert.True(table.Columns[1].IsMergedGuidColumn);
        Assert.True(table.Columns[1].IsGuidColumn);
        Assert.Equal(3, table.PhysicalColumnCount);
        Assert.True(table.HasMergedColumns);
    }

    [Fact]
    public void Schema_MixedColumns_OnlyHiLoPairsMerged()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE mixed (id INTEGER PRIMARY KEY, name TEXT, ref_guid__hi INTEGER, ref_guid__lo INTEGER, score REAL)");
        });

        using var db = SharcDatabase.OpenMemory(data);
        var table = db.Schema.GetTable("mixed");

        // Logical: [id, name, ref_guid, score]
        Assert.Equal(4, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal("name", table.Columns[1].Name);
        Assert.Equal("ref_guid", table.Columns[2].Name);
        Assert.True(table.Columns[2].IsMergedGuidColumn);
        Assert.Equal("score", table.Columns[3].Name);
        Assert.False(table.Columns[3].IsMergedGuidColumn);
        Assert.Equal(5, table.PhysicalColumnCount);
    }

    // ─── Read Path ───

    [Fact]
    public void GetGuid_MergedColumns_ReturnsCorrectGuid()
    {
        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        var (hi, lo) = GuidCodec.ToInt64Pair(guid);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE items (id INTEGER PRIMARY KEY, item_guid__hi INTEGER NOT NULL, item_guid__lo INTEGER NOT NULL)");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO items (item_guid__hi, item_guid__lo) VALUES ($hi, $lo)";
            cmd.Parameters.AddWithValue("$hi", hi);
            cmd.Parameters.AddWithValue("$lo", lo);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items");

        Assert.True(reader.Read());
        // Logical column 1 is the merged item_guid
        var actual = reader.GetGuid(1);
        Assert.Equal(guid, actual);
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetGuid_MultipleRows_AllRoundTrip()
    {
        var guids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE refs (id INTEGER PRIMARY KEY, ref__hi INTEGER NOT NULL, ref__lo INTEGER NOT NULL)");

            foreach (var guid in guids)
            {
                var (hi, lo) = GuidCodec.ToInt64Pair(guid);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO refs (ref__hi, ref__lo) VALUES ($hi, $lo)";
                cmd.Parameters.AddWithValue("$hi", hi);
                cmd.Parameters.AddWithValue("$lo", lo);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("refs");

        for (int i = 0; i < guids.Length; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(guids[i], reader.GetGuid(1));
        }
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetGuid_EmptyGuid_MergedColumns_RoundTrips()
    {
        var (hi, lo) = GuidCodec.ToInt64Pair(Guid.Empty);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE t (id INTEGER PRIMARY KEY, g__hi INTEGER, g__lo INTEGER)");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO t (g__hi, g__lo) VALUES ($hi, $lo)";
            cmd.Parameters.AddWithValue("$hi", hi);
            cmd.Parameters.AddWithValue("$lo", lo);
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("t");

        Assert.True(reader.Read());
        Assert.Equal(Guid.Empty, reader.GetGuid(1));
    }

    [Fact]
    public void FieldCount_MergedColumns_ReturnsLogicalCount()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE fc (id INTEGER PRIMARY KEY, g__hi INTEGER, g__lo INTEGER, name TEXT)");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO fc (g__hi, g__lo, name) VALUES (0, 0, 'test')";
            cmd.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("fc");

        // Logical: [id, g, name] = 3
        Assert.Equal(3, reader.FieldCount);
        Assert.True(reader.Read());
        Assert.Equal("test", reader.GetString(2)); // name is logical ordinal 2
    }

    // ─── Write + Read Round-Trip ───

    [Fact]
    public void Insert_MergedGuid_ThenRead_RoundTrips()
    {
        var guid = Guid.NewGuid();

        // Create a database with __hi/__lo columns
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE entities (id INTEGER PRIMARY KEY, entity_guid__hi INTEGER NOT NULL, entity_guid__lo INTEGER NOT NULL, label TEXT)");
        });

        // Write via SharcWriter (logical: [id, entity_guid, label])
        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        long rowId = writer.Insert("entities",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.FromGuid(guid),
            ColumnValue.Text(2 * 5 + 13, "hello"u8.ToArray()));

        Assert.Equal(1L, rowId);

        // Read back and verify
        using var reader = db.CreateReader("entities");
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(guid, reader.GetGuid(1));
        Assert.Equal("hello", reader.GetString(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public void InsertBatch_MergedGuids_ThenRead_AllRoundTrip()
    {
        var guids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE batch (id INTEGER PRIMARY KEY, bg__hi INTEGER NOT NULL, bg__lo INTEGER NOT NULL)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var records = guids.Select((g, i) => new[]
        {
            ColumnValue.FromInt64(1, i + 1),
            ColumnValue.FromGuid(g)
        });

        var rowIds = writer.InsertBatch("batch", records);
        Assert.Equal(3, rowIds.Length);

        // Read back
        using var reader = db.CreateReader("batch");
        for (int i = 0; i < guids.Length; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(guids[i], reader.GetGuid(1));
        }
        Assert.False(reader.Read());
    }

    [Fact]
    public void Update_MergedGuid_ThenRead_ReturnsUpdatedGuid()
    {
        var originalGuid = Guid.NewGuid();
        var updatedGuid = Guid.NewGuid();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE upd (id INTEGER PRIMARY KEY, ug__hi INTEGER NOT NULL, ug__lo INTEGER NOT NULL)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        // Insert original
        long rowId = writer.Insert("upd",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.FromGuid(originalGuid));

        // Update with new GUID
        bool updated = writer.Update("upd", rowId,
            ColumnValue.FromInt64(1, 1),
            ColumnValue.FromGuid(updatedGuid));

        Assert.True(updated);

        // Read back
        using var reader = db.CreateReader("upd");
        Assert.True(reader.Read());
        Assert.Equal(updatedGuid, reader.GetGuid(1));
    }

    [Fact]
    public void Insert_NullMergedGuid_ThenRead_ReturnsNull()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE nullable (id INTEGER PRIMARY KEY, ng__hi INTEGER, ng__lo INTEGER)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        writer.Insert("nullable",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.Null());

        using var reader = db.CreateReader("nullable");
        Assert.True(reader.Read());
        Assert.True(reader.IsNull(1));
    }

    // ─── Coexistence: BLOB(16) and Merged in same database ───

    [Fact]
    public void Coexistence_BlobAndMergedGuids_BothWork()
    {
        var blobGuid = Guid.NewGuid();
        var mergedGuid = Guid.NewGuid();
        var blobBytes = new byte[16];
        GuidCodec.Encode(blobGuid, blobBytes);
        var (hi, lo) = GuidCodec.ToInt64Pair(mergedGuid);

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE blob_guids (id INTEGER PRIMARY KEY, bg GUID)");
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE merged_guids (id INTEGER PRIMARY KEY, mg__hi INTEGER NOT NULL, mg__lo INTEGER NOT NULL)");

            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "INSERT INTO blob_guids (bg) VALUES ($g)";
            cmd1.Parameters.AddWithValue("$g", blobBytes);
            cmd1.ExecuteNonQuery();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "INSERT INTO merged_guids (mg__hi, mg__lo) VALUES ($hi, $lo)";
            cmd2.Parameters.AddWithValue("$hi", hi);
            cmd2.Parameters.AddWithValue("$lo", lo);
            cmd2.ExecuteNonQuery();
        });

        using var db = SharcDatabase.OpenMemory(data);

        // BLOB(16) path
        var blobTable = db.Schema.GetTable("blob_guids");
        Assert.True(blobTable.Columns[1].IsGuidColumn);
        Assert.False(blobTable.Columns[1].IsMergedGuidColumn);

        using var blobReader = db.CreateReader("blob_guids");
        Assert.True(blobReader.Read());
        Assert.Equal(blobGuid, blobReader.GetGuid(1));

        // Merged path
        var mergedTable = db.Schema.GetTable("merged_guids");
        Assert.True(mergedTable.Columns[1].IsMergedGuidColumn);
        Assert.True(mergedTable.HasMergedColumns);

        using var mergedReader = db.CreateReader("merged_guids");
        Assert.True(mergedReader.Read());
        Assert.Equal(mergedGuid, mergedReader.GetGuid(1));
    }
}
