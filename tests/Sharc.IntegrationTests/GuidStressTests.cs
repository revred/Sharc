// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.Primitives;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Stress tests for GUID native type — verifies BLOB(16) and merged (__hi/__lo)
/// round-trips at scale, boundary GUIDs, mixed-column schemas, write+read cycles,
/// projection, filtering, and batch operations under realistic workloads.
/// </summary>
public sealed class GuidStressTests
{
    // ── BLOB(16) Read Stress ──

    [Fact]
    public void BlobGuid_100Rows_AllRoundTrip()
    {
        var guids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE items (id INTEGER PRIMARY KEY, g GUID)");

            foreach (var guid in guids)
            {
                var bytes = new byte[16];
                GuidCodec.Encode(guid, bytes);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO items (g) VALUES ($g)";
                cmd.Parameters.AddWithValue("$g", bytes);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("items");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(guids[count], reader.GetGuid(1));
            count++;
        }
        Assert.Equal(100, count);
    }

    [Fact]
    public void BlobGuid_BoundaryValues_AllRoundTrip()
    {
        // Test edge-case GUIDs: Empty, all-ones, sequential, known patterns
        var guids = new[]
        {
            Guid.Empty,
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            new Guid("00000000-0000-0000-0000-000000000001"),
            new Guid("01020304-0506-0708-090A-0B0C0D0E0F10"),
            new Guid("80000000-0000-0000-8000-000000000000"), // sign bit boundary
        };

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE boundary (id INTEGER PRIMARY KEY, g GUID)");

            foreach (var guid in guids)
            {
                var bytes = new byte[16];
                GuidCodec.Encode(guid, bytes);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO boundary (g) VALUES ($g)";
                cmd.Parameters.AddWithValue("$g", bytes);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("boundary");

        for (int i = 0; i < guids.Length; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(guids[i], reader.GetGuid(1));
        }
        Assert.False(reader.Read());
    }

    // ── Merged Column Read Stress ──

    [Fact]
    public void MergedGuid_100Rows_AllRoundTrip()
    {
        var guids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE entities (id INTEGER PRIMARY KEY, eg__hi INTEGER NOT NULL, eg__lo INTEGER NOT NULL)");

            foreach (var guid in guids)
            {
                var (hi, lo) = GuidCodec.ToInt64Pair(guid);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO entities (eg__hi, eg__lo) VALUES ($hi, $lo)";
                cmd.Parameters.AddWithValue("$hi", hi);
                cmd.Parameters.AddWithValue("$lo", lo);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("entities");

        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(guids[count], reader.GetGuid(1));
            count++;
        }
        Assert.Equal(100, count);
    }

    [Fact]
    public void MergedGuid_BoundaryValues_AllRoundTrip()
    {
        var guids = new[]
        {
            Guid.Empty,
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            new Guid("80000000-0000-0000-8000-000000000000"),
            new Guid("7FFFFFFF-FFFF-FFFF-7FFF-FFFFFFFFFFFF"),
        };

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE bounds (id INTEGER PRIMARY KEY, bg__hi INTEGER NOT NULL, bg__lo INTEGER NOT NULL)");

            foreach (var guid in guids)
            {
                var (hi, lo) = GuidCodec.ToInt64Pair(guid);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO bounds (bg__hi, bg__lo) VALUES ($hi, $lo)";
                cmd.Parameters.AddWithValue("$hi", hi);
                cmd.Parameters.AddWithValue("$lo", lo);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("bounds");

        for (int i = 0; i < guids.Length; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(guids[i], reader.GetGuid(1));
        }
        Assert.False(reader.Read());
    }

    // ── Write + Read Round-Trip Stress ──

    [Fact]
    public void WriteMergedGuid_50Rows_ThenRead_AllCorrect()
    {
        var guids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE wrt (id INTEGER PRIMARY KEY, wg__hi INTEGER NOT NULL, wg__lo INTEGER NOT NULL, label TEXT)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        for (int i = 0; i < guids.Length; i++)
        {
            writer.Insert("wrt",
                ColumnValue.FromInt64(1, i + 1),
                ColumnValue.FromGuid(guids[i]),
                ColumnValue.Text(2 * 8 + 13, System.Text.Encoding.UTF8.GetBytes($"item_{i:D3}")));
        }

        using var reader = db.CreateReader("wrt");
        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(guids[count], reader.GetGuid(1));
            Assert.Equal($"item_{count:D3}", reader.GetString(2));
            count++;
        }
        Assert.Equal(50, count);
    }

    [Fact]
    public void WriteBatch_MergedGuids_ThenRead_AllCorrect()
    {
        var guids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();

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
        Assert.Equal(30, rowIds.Length);

        using var reader = db.CreateReader("batch");
        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(guids[count], reader.GetGuid(1));
            count++;
        }
        Assert.Equal(30, count);
    }

    // ── Update GUID ──

    [Fact]
    public void UpdateMergedGuid_10Times_LastValueCorrect()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE upd (id INTEGER PRIMARY KEY, ug__hi INTEGER NOT NULL, ug__lo INTEGER NOT NULL)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var initial = Guid.NewGuid();
        long rowId = writer.Insert("upd",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.FromGuid(initial));

        Guid lastGuid = initial;
        for (int i = 0; i < 10; i++)
        {
            lastGuid = Guid.NewGuid();
            writer.Update("upd", rowId,
                ColumnValue.FromInt64(1, 1),
                ColumnValue.FromGuid(lastGuid));
        }

        using var reader = db.CreateReader("upd");
        Assert.True(reader.Read());
        Assert.Equal(lastGuid, reader.GetGuid(1));
        Assert.False(reader.Read());
    }

    // ── NULL GUID ──

    [Fact]
    public void NullMergedGuid_IsNull_ReturnsTrue()
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

        // Also insert a non-null for contrast
        writer.Insert("nullable",
            ColumnValue.FromInt64(1, 2),
            ColumnValue.FromGuid(Guid.NewGuid()));

        using var reader = db.CreateReader("nullable");
        Assert.True(reader.Read());
        Assert.True(reader.IsNull(1));

        Assert.True(reader.Read());
        Assert.False(reader.IsNull(1));
    }

    // ── Multiple GUID columns ──

    [Fact]
    public void MultipleGuidColumns_AllCorrect()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE multi (id INTEGER PRIMARY KEY, a__hi INTEGER, a__lo INTEGER, b__hi INTEGER, b__lo INTEGER)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();

        writer.Insert("multi",
            ColumnValue.FromInt64(1, 1),
            ColumnValue.FromGuid(guidA),
            ColumnValue.FromGuid(guidB));

        using var reader = db.CreateReader("multi");
        Assert.True(reader.Read());
        Assert.Equal(guidA, reader.GetGuid(1));
        Assert.Equal(guidB, reader.GetGuid(2));
    }

    // ── Projection with GUIDs ──

    [Fact]
    public void Projection_GuidAndText_BothAccessible()
    {
        var guids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE proj (id INTEGER PRIMARY KEY, pg__hi INTEGER NOT NULL, pg__lo INTEGER NOT NULL, name TEXT)");

            for (int i = 0; i < guids.Length; i++)
            {
                var (hi, lo) = GuidCodec.ToInt64Pair(guids[i]);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO proj (pg__hi, pg__lo, name) VALUES ($hi, $lo, $name)";
                cmd.Parameters.AddWithValue("$hi", hi);
                cmd.Parameters.AddWithValue("$lo", lo);
                cmd.Parameters.AddWithValue("$name", $"proj_{i}");
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);

        // Full scan — verify GUID and text are both accessible
        using var reader = db.CreateReader("proj");
        int count = 0;
        while (reader.Read())
        {
            Assert.Equal(guids[count], reader.GetGuid(1)); // pg (merged)
            Assert.Equal($"proj_{count}", reader.GetString(2)); // name
            count++;
        }
        Assert.Equal(20, count);
    }

    // ── Schema detection stress ──

    [Fact]
    public void Schema_MultipleGuidColumns_AllDetected()
    {
        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE complex (id INTEGER PRIMARY KEY, owner__hi INTEGER, owner__lo INTEGER, name TEXT, ref__hi INTEGER, ref__lo INTEGER, score REAL)");
        });

        using var db = SharcDatabase.OpenMemory(data);
        var table = db.Schema.GetTable("complex");

        // Logical: [id, owner, name, ref, score] = 5 columns
        Assert.Equal(5, table.Columns.Count);
        Assert.Equal("owner", table.Columns[1].Name);
        Assert.True(table.Columns[1].IsMergedGuidColumn);
        Assert.Equal("name", table.Columns[2].Name);
        Assert.False(table.Columns[2].IsMergedGuidColumn);
        Assert.Equal("ref", table.Columns[3].Name);
        Assert.True(table.Columns[3].IsMergedGuidColumn);
        Assert.Equal("score", table.Columns[4].Name);
        Assert.False(table.Columns[4].IsMergedGuidColumn);
        Assert.Equal(7, table.PhysicalColumnCount);
        Assert.True(table.HasMergedColumns);
    }

    // ── Seek by rowid with GUID columns ──

    [Fact]
    public void Seek_MergedGuid_CorrectValue()
    {
        var guids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE seekable (id INTEGER PRIMARY KEY, sg__hi INTEGER NOT NULL, sg__lo INTEGER NOT NULL)");

            for (int i = 0; i < guids.Length; i++)
            {
                var (hi, lo) = GuidCodec.ToInt64Pair(guids[i]);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO seekable (sg__hi, sg__lo) VALUES ($hi, $lo)";
                cmd.Parameters.AddWithValue("$hi", hi);
                cmd.Parameters.AddWithValue("$lo", lo);
                cmd.ExecuteNonQuery();
            }
        });

        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("seekable");

        // Seek to row 25
        Assert.True(reader.Seek(25));
        Assert.Equal(guids[24], reader.GetGuid(1));

        // Seek to row 50
        Assert.True(reader.Seek(50));
        Assert.Equal(guids[49], reader.GetGuid(1));

        // Seek to row 1
        Assert.True(reader.Seek(1));
        Assert.Equal(guids[0], reader.GetGuid(1));
    }

    // ── Delete + re-read ──

    [Fact]
    public void DeleteGuidRow_ThenScan_RowGone()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();

        var data = TestDatabaseFactory.CreateDatabaseWith(conn =>
        {
            TestDatabaseFactory.Execute(conn,
                "CREATE TABLE del (id INTEGER PRIMARY KEY, dg__hi INTEGER NOT NULL, dg__lo INTEGER NOT NULL)");
        });

        using var db = SharcDatabase.OpenMemory(data, new SharcOpenOptions { Writable = true });
        using var writer = SharcWriter.From(db);

        writer.Insert("del", ColumnValue.FromInt64(1, 1), ColumnValue.FromGuid(guid1));
        writer.Insert("del", ColumnValue.FromInt64(1, 2), ColumnValue.FromGuid(guid2));
        writer.Insert("del", ColumnValue.FromInt64(1, 3), ColumnValue.FromGuid(guid3));

        // Delete row 2
        writer.Delete("del", 2);

        using var reader = db.CreateReader("del");
        var remaining = new List<Guid>();
        while (reader.Read())
            remaining.Add(reader.GetGuid(1));

        Assert.Equal(2, remaining.Count);
        Assert.Contains(guid1, remaining);
        Assert.Contains(guid3, remaining);
        Assert.DoesNotContain(guid2, remaining);
    }

    // ── GuidCodec.ToInt64Pair / FromInt64Pair stress ──

    [Fact]
    public void GuidCodec_1000RandomGuids_PairRoundTrip()
    {
        for (int i = 0; i < 1000; i++)
        {
            var original = Guid.NewGuid();
            var (hi, lo) = GuidCodec.ToInt64Pair(original);
            var reconstructed = GuidCodec.FromInt64Pair(hi, lo);
            Assert.Equal(original, reconstructed);
        }
    }

    [Fact]
    public void GuidCodec_EncodeDecodeCycle_1000Guids()
    {
        var buffer = new byte[16];
        for (int i = 0; i < 1000; i++)
        {
            var original = Guid.NewGuid();
            GuidCodec.Encode(original, buffer);
            var decoded = GuidCodec.Decode(buffer.AsSpan());
            Assert.Equal(original, decoded);
        }
    }
}
