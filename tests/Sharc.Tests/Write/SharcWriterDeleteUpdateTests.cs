// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Trust;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Tests for <see cref="SharcWriter.Delete"/>, <see cref="SharcWriter.Update"/>,
/// <see cref="SharcWriteTransaction.Delete"/>, and <see cref="SharcWriteTransaction.Update"/>.
/// </summary>
public sealed class SharcWriterDeleteUpdateTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    /// <summary>
    /// Creates a valid 2-page SQLite database with sqlite_master entry pointing to
    /// an empty "test" table on page 2. Schema: test(id INTEGER, name TEXT).
    /// </summary>
    private static byte[] CreateDatabaseWithEmptyTable()
    {
        var data = new byte[PageSize * 2];

        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);

        string sql = "CREATE TABLE test(id INTEGER, name TEXT)";
        var sqlBytes = Encoding.UTF8.GetBytes(sql);
        var cols = new ColumnValue[5];
        cols[0] = ColumnValue.Text(2 * 5 + 13, Encoding.UTF8.GetBytes("table"));
        cols[1] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));
        cols[2] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));
        cols[3] = ColumnValue.FromInt64(1, 2);
        cols[4] = ColumnValue.Text(2 * sqlBytes.Length + 13, sqlBytes);

        int recordSize = RecordEncoder.ComputeEncodedSize(cols);
        Span<byte> recordBuf = stackalloc byte[recordSize];
        RecordEncoder.EncodeRecord(cols, recordBuf);

        int cellSize = CellBuilder.ComputeTableLeafCellSize(1, recordSize, UsableSize);
        Span<byte> cellBuf = stackalloc byte[cellSize];
        CellBuilder.BuildTableLeafCell(1, recordBuf, cellBuf, UsableSize);

        int pageHdrOff = 100;
        ushort cellContentOff = (ushort)(PageSize - cellSize);
        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 1, cellContentOff, 0, 0);
        BTreePageHeader.Write(data.AsSpan(pageHdrOff), masterHdr);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageHdrOff + 8), cellContentOff);
        cellBuf.CopyTo(data.AsSpan(cellContentOff));

        int page2Off = PageSize;
        var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(page2Off), tableHdr);

        return data;
    }

    private static ColumnValue[] MakeRow(long id, string name) =>
    [
        ColumnValue.FromInt64(1, id),
        ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
    ];

    /// <summary>Agent with WriteScope restricted to "other_table.*" — denied for "test".</summary>
    private static AgentInfo MakeDeniedAgent() =>
        new("test-agent", AgentClass.User, Array.Empty<byte>(), 0,
            "other_table.*", "*", 0, 0, "", false, Array.Empty<byte>());

    private static int CountRows(SharcDatabase db, string tableName)
    {
        using var reader = db.CreateReader(tableName);
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    // ── SharcWriter.Delete ──────────────────────────────────────────

    [Fact]
    public void SharcWriter_Delete_AutoCommits_RowRemoved()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        writer.Insert("test", MakeRow(20, "two"));
        writer.Insert("test", MakeRow(30, "three"));

        bool found = writer.Delete("test", 2); // rowid 2

        Assert.True(found);
        Assert.Equal(2, CountRows(db, "test"));
    }

    [Fact]
    public void SharcWriter_Delete_NonexistentRow_ReturnsFalse()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        writer.Insert("test", MakeRow(20, "two"));
        writer.Insert("test", MakeRow(30, "three"));

        bool found = writer.Delete("test", 999);

        Assert.False(found);
        Assert.Equal(3, CountRows(db, "test"));
    }

    [Fact]
    public void SharcWriter_Delete_AgentDenied_ThrowsUnauthorized()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        var agent = MakeDeniedAgent();

        Assert.Throws<UnauthorizedAccessException>(() => writer.Delete(agent, "test", 1));
    }

    // ── SharcWriter.Update ──────────────────────────────────────────

    [Fact]
    public void SharcWriter_Update_AutoCommits_DataChanged()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "original"));

        bool found = writer.Update("test", 1, MakeRow(99, "updated"));

        Assert.True(found);
        using var reader = db.CreateReader("test");
        Assert.True(reader.Read());
        Assert.Equal(99L, reader.GetInt64(0));
        Assert.Equal("updated", reader.GetString(1));
    }

    [Fact]
    public void SharcWriter_Update_NonexistentRow_ReturnsFalse()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));

        bool found = writer.Update("test", 999, MakeRow(99, "nope"));

        Assert.False(found);
        Assert.Equal(1, CountRows(db, "test"));
    }

    [Fact]
    public void SharcWriter_Update_AgentDenied_ThrowsUnauthorized()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        var agent = MakeDeniedAgent();

        Assert.Throws<UnauthorizedAccessException>(() =>
            writer.Update(agent, "test", 1, MakeRow(99, "nope")));
    }

    // ── SharcWriteTransaction.Delete ─────────────────────────────────

    [Fact]
    public void SharcWriteTransaction_Delete_CommitsWithOtherChanges()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        writer.Insert("test", MakeRow(20, "two"));
        writer.Insert("test", MakeRow(30, "three"));

        using (var tx = writer.BeginTransaction())
        {
            tx.Insert("test", MakeRow(40, "four"));
            tx.Delete("test", 1); // delete rowid 1
            tx.Commit();
        }

        // 3 original - 1 deleted + 1 inserted = 3
        Assert.Equal(3, CountRows(db, "test"));
    }

    [Fact]
    public void SharcWriteTransaction_Update_CommitsWithOtherChanges()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        writer.Insert("test", MakeRow(20, "two"));

        using (var tx = writer.BeginTransaction())
        {
            tx.Insert("test", MakeRow(30, "three"));
            tx.Update("test", 1, MakeRow(99, "UPDATED"));
            tx.Commit();
        }

        Assert.Equal(3, CountRows(db, "test"));

        // Verify the update took effect by reading all rows
        using var reader = db.CreateReader("test");
        bool foundUpdated = false;
        while (reader.Read())
        {
            if (reader.GetInt64(0) == 99L)
            {
                Assert.Equal("UPDATED", reader.GetString(1));
                foundUpdated = true;
            }
        }
        Assert.True(foundUpdated);
    }

    [Fact]
    public void SharcWriteTransaction_Delete_Rollback_RowStillExists()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "one"));
        writer.Insert("test", MakeRow(20, "two"));

        using (var tx = writer.BeginTransaction())
        {
            tx.Delete("test", 2); // delete rowid 2
            tx.Rollback();
        }

        // Rollback means the delete never happened
        Assert.Equal(2, CountRows(db, "test"));
    }

    [Fact]
    public void SharcWriteTransaction_Update_Rollback_OriginalDataPreserved()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(10, "original"));

        using (var tx = writer.BeginTransaction())
        {
            tx.Update("test", 1, MakeRow(99, "CHANGED"));
            tx.Rollback();
        }

        // Rollback means the update never happened — original data intact
        using var reader = db.CreateReader("test");
        Assert.True(reader.Read());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.Equal("original", reader.GetString(1));
    }
}
