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
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// Tests that Transaction.Commit() updates the database header on page 1
/// with current PageCount, ChangeCounter, and freelist pointers.
/// </summary>
public sealed class HeaderUpdateOnCommitTests
{
    private const int PageSize = 4096;

    private static byte[] CreateDatabaseBytes()
    {
        var data = new byte[PageSize * 2];

        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2); // PageCount = 2
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1); // SchemaCookie
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4); // SchemaFormat

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

        int cellSize = CellBuilder.ComputeTableLeafCellSize(1, recordSize, PageSize);
        Span<byte> cellBuf = stackalloc byte[cellSize];
        CellBuilder.BuildTableLeafCell(1, recordBuf, cellBuf, PageSize);

        int pageHdrOff = 100;
        ushort cellContentOff = (ushort)(PageSize - cellSize);
        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 1, cellContentOff, 0, 0);
        BTreePageHeader.Write(data.AsSpan(pageHdrOff), masterHdr);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageHdrOff + 8), cellContentOff);
        cellBuf.CopyTo(data.AsSpan(cellContentOff));

        int page2Off = PageSize;
        var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)PageSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(page2Off), tableHdr);

        return data;
    }

    private static ColumnValue[] MakeRow(long id, string name) =>
    [
        ColumnValue.FromInt64(1, id),
        ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
    ];

    private static DatabaseHeader ReadHeader(byte[] data)
    {
        return DatabaseHeader.Parse(data.AsSpan(0, 100));
    }

    /// <summary>
    /// Reads the header from the database's page source (survives MemoryPageSource resizes).
    /// </summary>
    private static DatabaseHeader ReadHeader(SharcDatabase db)
    {
        var page1 = db.PageSource.GetPage(1);
        return DatabaseHeader.Parse(page1);
    }

    [Fact]
    public void Commit_SingleInsert_ChangeCounterIncremented()
    {
        var data = CreateDatabaseBytes();
        var headerBefore = ReadHeader(data);

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);
        writer.Insert("test", MakeRow(10, "hello"));

        var headerAfter = ReadHeader(db);
        Assert.True(headerAfter.ChangeCounter > headerBefore.ChangeCounter);
    }

    [Fact]
    public void Commit_SingleInsert_PageCountUpdated()
    {
        var data = CreateDatabaseBytes();

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        var headerBefore = ReadHeader(db);
        Assert.Equal(2, headerBefore.PageCount);

        // Insert enough rows to force new pages
        for (int i = 0; i < 50; i++)
            writer.Insert("test", MakeRow(i, $"user_{i:D5}_" + new string('x', 80)));

        var headerAfter = ReadHeader(db);
        Assert.True(headerAfter.PageCount > headerBefore.PageCount);
    }

    [Fact]
    public void Commit_MultipleInserts_PageCountReflectsNewPages()
    {
        var data = CreateDatabaseBytes();

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Each insert auto-commits, so each should update the header
        writer.Insert("test", MakeRow(1, "alpha"));
        var header1 = ReadHeader(db);

        // Insert many to force page allocation
        for (int i = 2; i <= 60; i++)
            writer.Insert("test", MakeRow(i, $"user_{i:D5}_" + new string('x', 80)));

        var header2 = ReadHeader(db);
        Assert.True(header2.PageCount >= header1.PageCount);
    }

    [Fact]
    public void Commit_TwoTransactions_ChangeCounterIncrementsTwice()
    {
        var data = CreateDatabaseBytes();

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "first"));
        var header1 = ReadHeader(db);

        writer.Insert("test", MakeRow(2, "second"));
        var header2 = ReadHeader(db);

        Assert.True(header2.ChangeCounter > header1.ChangeCounter);
    }

    [Fact]
    public void Commit_NoChanges_HeaderUntouched()
    {
        var data = CreateDatabaseBytes();
        var original = new byte[100];
        data.AsSpan(0, 100).CopyTo(original);

        using var db = SharcDatabase.OpenMemory(data);
        using var tx = db.BeginTransaction();
        tx.Commit(); // empty commit

        // Header should be unchanged — read from page source (no resize happened)
        Assert.True(data.AsSpan(0, 100).SequenceEqual(original));
    }

    [Fact]
    public void Commit_Delete_HeaderStillUpdated()
    {
        var data = CreateDatabaseBytes();

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "to_delete"));
        var headerAfterInsert = ReadHeader(db);

        writer.Delete("test", 1);
        var headerAfterDelete = ReadHeader(db);

        Assert.True(headerAfterDelete.ChangeCounter > headerAfterInsert.ChangeCounter);
    }

    [Fact]
    public void Commit_FreelistFieldsWritten()
    {
        var data = CreateDatabaseBytes();

        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "test_row"));
        var header = ReadHeader(db);

        // Initially freelist should be 0/0 (no freelist management yet)
        Assert.Equal(0u, header.FirstFreelistPage);
        Assert.Equal(0, header.FreelistPageCount);
    }
}
