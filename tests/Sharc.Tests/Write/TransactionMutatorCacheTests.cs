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
/// Tests for Transaction's cached BTreeMutator lifecycle.
/// Validates that a single BTreeMutator is shared across operations
/// within a transaction and properly disposed on commit/rollback/dispose.
/// </summary>
public sealed class TransactionMutatorCacheTests
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
    public void FetchMutator_FirstCall_CreatesNewMutator()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var tx = db.BeginTransaction();

        var mutator = tx.FetchMutator(UsableSize);

        Assert.NotNull(mutator);
    }

    [Fact]
    public void FetchMutator_SecondCall_ReturnsSameInstance()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var tx = db.BeginTransaction();

        var first = tx.FetchMutator(UsableSize);
        var second = tx.FetchMutator(UsableSize);

        Assert.Same(first, second);
    }

    [Fact]
    public void Dispose_Transaction_DisposesMutator()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        BTreeMutator mutator;

        using (var tx = db.BeginTransaction())
        {
            mutator = tx.FetchMutator(UsableSize);
            // Insert a row so the mutator has cached pages
            SharcWriter.InsertCore(tx, "test", MakeRow(10, "hello"));
            Assert.True(mutator.CachedPageCount >= 1);
            tx.Commit();
        }

        // After dispose, the mutator should be disposed (Insert should throw)
        var rec = new byte[4];
        Assert.Throws<ObjectDisposedException>(() => { mutator.Insert(2, 1, rec); });
    }

    [Fact]
    public void Commit_Transaction_DisposesMutator()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var tx = db.BeginTransaction();

        var mutator = tx.FetchMutator(UsableSize);
        SharcWriter.InsertCore(tx, "test", MakeRow(10, "hello"));

        tx.Commit();

        // After commit, the mutator should be disposed
        var rec = new byte[4];
        Assert.Throws<ObjectDisposedException>(() => { mutator.Insert(2, 1, rec); });
    }

    [Fact]
    public void Rollback_Transaction_DisposesMutator()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var tx = db.BeginTransaction();

        var mutator = tx.FetchMutator(UsableSize);
        SharcWriter.InsertCore(tx, "test", MakeRow(10, "hello"));

        tx.Rollback();

        // After rollback, the mutator should be disposed
        var rec = new byte[4];
        Assert.Throws<ObjectDisposedException>(() => { mutator.Insert(2, 1, rec); });
    }

    [Fact]
    public void InsertBatch_100Rows_AllReadable()
    {
        var data = CreateDatabaseBytes();
        using var db = SharcDatabase.OpenMemory(data);
        using var writer = SharcWriter.From(db);

        var records = new List<ColumnValue[]>();
        for (int i = 0; i < 100; i++)
            records.Add(MakeRow(i * 10, $"row_{i:D3}"));

        long[] rowIds = writer.InsertBatch("test", records);

        Assert.Equal(100, rowIds.Length);

        // Verify all rows readable
        using var reader = db.CreateReader("test");
        int count = 0;
        while (reader.Read()) count++;
        Assert.Equal(100, count);
    }
}
