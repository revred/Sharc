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
/// Tests for InsertBatch pre-sizing and correctness with various collection types.
/// </summary>
public sealed class InsertBatchOptimizationTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

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

    [Fact]
    public void InsertBatch_ArrayInput_ReturnsCorrectRowIds()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        ColumnValue[][] records =
        [
            MakeRow(10, "alpha"),
            MakeRow(20, "beta"),
            MakeRow(30, "gamma"),
        ];

        long[] rowIds = writer.InsertBatch("test", records);

        Assert.Equal(3, rowIds.Length);
        Assert.Equal(1L, rowIds[0]);
        Assert.Equal(2L, rowIds[1]);
        Assert.Equal(3L, rowIds[2]);
    }

    [Fact]
    public void InsertBatch_ListInput_ReturnsCorrectRowIds()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        var records = new List<ColumnValue[]>
        {
            MakeRow(10, "one"),
            MakeRow(20, "two"),
            MakeRow(30, "three"),
            MakeRow(40, "four"),
            MakeRow(50, "five"),
        };

        long[] rowIds = writer.InsertBatch("test", records);

        Assert.Equal(5, rowIds.Length);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i + 1L, rowIds[i]);
    }

    [Fact]
    public void InsertBatch_LazyEnumerable_ReturnsCorrectRowIds()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        // Lazy enumerable (not ICollection)
        static IEnumerable<ColumnValue[]> Generate()
        {
            for (int i = 1; i <= 10; i++)
                yield return MakeRow(i * 10, $"row_{i}");
        }

        long[] rowIds = writer.InsertBatch("test", Generate());

        Assert.Equal(10, rowIds.Length);
    }

    [Fact]
    public void InsertBatch_EmptyCollection_ReturnsEmptyArray()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        long[] rowIds = writer.InsertBatch("test", Array.Empty<ColumnValue[]>());

        Assert.Empty(rowIds);
    }
}
