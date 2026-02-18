// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Tests for table root page caching in <see cref="SharcWriter"/>.
/// Verifies that repeated operations to the same table do not
/// re-scan sqlite_master for every call (ADR-016 Tier 1).
/// </summary>
public sealed class TableRootCacheTests
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

    private static int CountRows(SharcDatabase db, string tableName)
    {
        using var reader = db.CreateReader(tableName);
        int count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Fact]
    public void Insert_SameTable_Multiple_CachePopulated()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // First insert populates the cache
        writer.Insert("test", MakeRow(1, "first"));
        Assert.Equal(1, writer.TableRootCacheCount);

        // Second insert reuses cached root page — no re-scan
        writer.Insert("test", MakeRow(2, "second"));
        Assert.Equal(1, writer.TableRootCacheCount);

        // Verify data integrity
        Assert.Equal(2, CountRows(db, "test"));
    }

    [Fact]
    public void Delete_AfterInsert_UsesCachedRootPage()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "one"));
        writer.Insert("test", MakeRow(2, "two"));
        Assert.Equal(1, writer.TableRootCacheCount);

        // Delete should use the cached root page
        bool found = writer.Delete("test", 1);
        Assert.True(found);
        Assert.Equal(1, CountRows(db, "test"));
    }

    [Fact]
    public void Update_AfterInsert_UsesCachedRootPage()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "original"));
        Assert.Equal(1, writer.TableRootCacheCount);

        // Update should use the cached root page
        bool found = writer.Update("test", 1, MakeRow(1, "updated"));
        Assert.True(found);
    }

    [Fact]
    public void ExplicitTransaction_MultipleOps_CacheShared()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        using var tx = writer.BeginTransaction();
        tx.Insert("test", MakeRow(1, "a"));
        tx.Insert("test", MakeRow(2, "b"));
        tx.Delete("test", 1);
        tx.Commit();

        // Cache should have been populated via the transaction
        Assert.Equal(1, CountRows(db, "test"));
    }

    [Fact]
    public void InsertMany_CausesRootSplit_CacheUpdated()
    {
        var data = CreateDatabaseWithEmptyTable();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Insert enough rows to cause at least one root split
        // Each row is ~40 bytes, page holds ~100 cells, so 150 should split
        for (int i = 0; i < 150; i++)
        {
            writer.Insert("test", MakeRow(i, $"row_{i:D5}"));
        }

        // Verify all rows readable after potential root splits
        Assert.Equal(150, CountRows(db, "test"));

        // Cache should still have exactly one entry for "test"
        Assert.Equal(1, writer.TableRootCacheCount);
    }
}
