// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
/// Tests for SharcWriter.Vacuum — compact rebuild of the database.
/// </summary>
public sealed class VacuumTests
{
    private const int PageSize = 4096;

    private static byte[] CreateDatabaseBytes()
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

    private static DatabaseHeader ReadHeader(SharcDatabase db)
    {
        var page1 = db.PageSource.GetPage(1);
        return DatabaseHeader.Parse(page1);
    }

    [Fact]
    public void Vacuum_EmptyTable_NoError()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Vacuum on empty table should not throw
        writer.Vacuum();

        // Table should still be readable
        using var reader = db.CreateReader("test");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Vacuum_AfterDeletes_PageCountDecreases()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Insert many rows to force page growth
        for (int i = 1; i <= 100; i++)
            writer.Insert("test", MakeRow(i, $"user_{i:D5}_" + new string('x', 80)));

        var headerBefore = ReadHeader(db);

        // Delete most rows
        for (int i = 1; i <= 90; i++)
            writer.Delete("test", i);

        // Vacuum to compact
        writer.Vacuum();

        var headerAfter = ReadHeader(db);
        Assert.True(headerAfter.PageCount <= headerBefore.PageCount,
            $"Expected page count to decrease: before={headerBefore.PageCount}, after={headerAfter.PageCount}");
    }

    [Fact]
    public void Vacuum_AllDataIntact_AfterCompaction()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Insert rows
        for (int i = 1; i <= 20; i++)
            writer.Insert("test", MakeRow(i, $"name_{i}"));

        // Delete some
        for (int i = 11; i <= 20; i++)
            writer.Delete("test", i);

        // Vacuum
        writer.Vacuum();

        // Verify remaining rows are intact
        using var reader = db.CreateReader("test");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(10, count);
    }

    [Fact]
    public void Vacuum_FreelistCleared_AfterVacuum()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Insert and delete to create potential freelist entries
        for (int i = 1; i <= 50; i++)
            writer.Insert("test", MakeRow(i, $"user_{i:D5}_" + new string('x', 80)));

        for (int i = 1; i <= 40; i++)
            writer.Delete("test", i);

        // Vacuum should clear the freelist
        writer.Vacuum();

        var header = ReadHeader(db);
        Assert.Equal(0u, header.FirstFreelistPage);
        Assert.Equal(0, header.FreelistPageCount);
    }

    [Fact]
    public void Vacuum_DataReadableAfterMultipleVacuums()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        writer.Insert("test", MakeRow(1, "alpha"));
        writer.Insert("test", MakeRow(2, "beta"));
        writer.Insert("test", MakeRow(3, "gamma"));

        writer.Vacuum();

        // Data still readable after first vacuum
        using (var reader = db.CreateReader("test"))
        {
            int count = 0;
            while (reader.Read()) count++;
            Assert.Equal(3, count);
        }

        // Can still insert after vacuum
        writer.Insert("test", MakeRow(4, "delta"));

        writer.Vacuum();

        // Data still readable after second vacuum
        using (var reader2 = db.CreateReader("test"))
        {
            int count = 0;
            while (reader2.Read()) count++;
            Assert.Equal(4, count);
        }
    }
}