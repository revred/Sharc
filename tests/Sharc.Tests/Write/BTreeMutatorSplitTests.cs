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
/// Tests for BTreeMutator page split correctness, especially after the
/// cell descriptor optimization that replaces per-cell .ToArray() allocations.
/// </summary>
public sealed class BTreeMutatorSplitTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    private static MemoryPageSource CreateDatabaseWithEmptyTable()
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

        return new MemoryPageSource(data);
    }

    private static (byte[] record, int size) BuildNamedRecord(int value, string name)
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.FromInt64(1, value),
            ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
        };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        var recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);
        return (recBuf, recSize);
    }

    [Fact]
    public void Insert_TriggersSingleSplit_AllRowsReadable()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // ~100-byte cells → ~40 per page → 50 rows triggers one split
        uint root = 2;
        for (int i = 1; i <= 50; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        var decoded = new ColumnValue[2];
        var decoder = new RecordDecoder();
        for (int i = 1; i <= 50; i++)
        {
            Assert.True(cursor.MoveNext(), $"Expected row {i}");
            Assert.Equal((long)i, cursor.RowId);
            decoder.DecodeRecord(cursor.Payload, decoded);
            Assert.Equal((long)i, decoded[0].AsInt64());
        }
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Insert_TriggersMultipleSplits_AllRowsReadable()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // 300 large rows → multiple leaf splits + interior splits
        uint root = 2;
        for (int i = 1; i <= 300; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(300, count);
    }

    [Fact]
    public void Insert_TriggersRootSplit_AllRowsReadable()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // Root split occurs when the first leaf (page 2) overflows.
        // With root retention, the root page number should stay 2.
        uint root = 2;
        for (int i = 1; i <= 100; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        // Root page should still be 2 (root retention)
        Assert.Equal(2u, root);

        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(100, count);
    }

    [Fact]
    public void Delete_AfterSplit_ThenInsert_DataIntact()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 100; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        // Delete some rows
        for (int i = 1; i <= 100; i += 2)
        {
            var (found, newRoot) = mutator.Delete(root, i);
            Assert.True(found);
            root = newRoot;
        }

        // Insert new rows
        for (int i = 101; i <= 120; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        // Count: 50 remaining from first batch + 20 new = 70
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(70, count);
    }

    [Fact]
    public void Delete_AlternatingRows_ThenInsert_DefragWorksCorrectly()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // Insert 30 rows (fits on one page with small records)
        uint root = 2;
        for (int i = 1; i <= 30; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"row{i:D2}");
            root = mutator.Insert(root, i, rec);
        }

        // Delete alternating to fragment the page
        for (int i = 2; i <= 30; i += 2)
        {
            var (found, newRoot) = mutator.Delete(root, i);
            Assert.True(found);
            root = newRoot;
        }

        // Insert more rows — defrag may trigger
        for (int i = 31; i <= 50; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"row{i:D2}");
            root = mutator.Insert(root, i, rec);
        }

        // Remaining: 15 (odd 1-30) + 20 (31-50) = 35
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(35, count);
    }
}