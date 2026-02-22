// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

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
/// Tests for <see cref="BTreeMutator.Delete"/> — cell removal from table B-trees.
/// </summary>
public sealed class BTreeMutatorDeleteTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    /// <summary>
    /// Creates a minimal in-memory SQLite database with a single empty table
    /// rooted at page 2. Page 1 = database header + sqlite_master.
    /// </summary>
    private static MemoryPageSource CreateDatabaseWithEmptyTable()
    {
        var data = new byte[PageSize * 2];

        // Page 1: database header (100 bytes) + sqlite_master B-tree
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2);   // page count = 2
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);   // schema cookie = 1
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);   // schema format = 4

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

        // Page 2: empty leaf table page
        int page2Off = PageSize;
        var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(page2Off), tableHdr);

        return new MemoryPageSource(data);
    }

    private static uint InsertRow(BTreeMutator mutator, uint root, long rowId, long id, string name)
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.FromInt64(1, id),
            ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
        };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        var recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);
        return mutator.Insert(root, rowId, recBuf);
    }

    private static List<long> ScanAllRowIds(IPageSource source, uint root)
    {
        var rowIds = new List<long>();
        using var cursor = new BTreeCursor<IPageSource>(source, root, UsableSize);
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);
        return rowIds;
    }

    // ── Round 1: Delete skeleton ───────────────────────────────────

    [Fact]
    public void Delete_EmptyTree_ReturnsFalse()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        var (found, root) = mutator.Delete(2, 1);

        Assert.False(found);
        Assert.Equal(2u, root);
    }

    [Fact]
    public void Delete_RowIdNotFound_ReturnsFalse()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "one");
        root = InsertRow(mutator, root, 2, 20, "two");
        root = InsertRow(mutator, root, 3, 30, "three");

        var (found, newRoot) = mutator.Delete(root, 99);

        Assert.False(found);
        Assert.Equal(root, newRoot);

        // All 3 rows still present
        var rowIds = ScanAllRowIds(shadow, root);
        Assert.Equal(3, rowIds.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, rowIds);
    }

    // ── Round 2: Single-page cell removal ──────────────────────────

    [Fact]
    public void Delete_SingleRow_TreeBecomesEmpty()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertRow(mutator, 2, 1, 42, "hello");

        var (found, newRoot) = mutator.Delete(root, 1);

        Assert.True(found);
        Assert.Empty(ScanAllRowIds(shadow, newRoot));
        Assert.Equal(0L, mutator.GetMaxRowId(newRoot));
    }

    [Fact]
    public void Delete_FirstRowOfThree_RemainingTwoInOrder()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "one");
        root = InsertRow(mutator, root, 2, 20, "two");
        root = InsertRow(mutator, root, 3, 30, "three");

        var (found, newRoot) = mutator.Delete(root, 1);

        Assert.True(found);
        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(new long[] { 2, 3 }, rowIds);
    }

    [Fact]
    public void Delete_MiddleRowOfThree_RemainingTwoInOrder()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "one");
        root = InsertRow(mutator, root, 2, 20, "two");
        root = InsertRow(mutator, root, 3, 30, "three");

        var (found, newRoot) = mutator.Delete(root, 2);

        Assert.True(found);
        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(new long[] { 1, 3 }, rowIds);
    }

    [Fact]
    public void Delete_LastRowOfThree_RemainingTwoInOrder()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "one");
        root = InsertRow(mutator, root, 2, 20, "two");
        root = InsertRow(mutator, root, 3, 30, "three");

        var (found, newRoot) = mutator.Delete(root, 3);

        Assert.True(found);
        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(new long[] { 1, 2 }, rowIds);
        Assert.Equal(2L, mutator.GetMaxRowId(newRoot));
    }

    [Fact]
    public void Delete_AllRowsOneByOne_TreeEmpty()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 5; i++)
            root = InsertRow(mutator, root, i, i * 10, $"row{i}");

        // Delete in non-sequential order
        foreach (var id in new long[] { 3, 1, 5, 2, 4 })
        {
            var (found, newRoot) = mutator.Delete(root, id);
            Assert.True(found);
            root = newRoot;
        }

        Assert.Empty(ScanAllRowIds(shadow, root));
        Assert.Equal(0L, mutator.GetMaxRowId(root));
    }

    [Fact]
    public void Delete_MultipleDeletes_FragmentedFreeBytesAccumulate()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "aaaa");
        root = InsertRow(mutator, root, 2, 20, "bbbb");
        root = InsertRow(mutator, root, 3, 30, "cccc");

        // Delete rows 1 and 3
        var (_, r1) = mutator.Delete(root, 1);
        root = r1;
        var (_, r2) = mutator.Delete(root, 3);
        root = r2;

        // Read raw page header and check FragmentedFreeBytes > 0
        var pageBuf = new byte[PageSize];
        shadow.ReadPage(root, pageBuf);
        var hdr = BTreePageHeader.Parse(pageBuf.AsSpan(0));
        Assert.True(hdr.FragmentedFreeBytes > 0);
        Assert.Equal(1, hdr.CellCount);
    }

    // ── Round 3: Edge cases ────────────────────────────────────────

    [Fact]
    public void Delete_LowestCellRemoved_CellContentOffsetUpdated()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // Insert 2 rows — second row has lowest cell offset (grows downward)
        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "first");
        root = InsertRow(mutator, root, 2, 20, "second");

        // Read header before delete
        var pageBuf = new byte[PageSize];
        shadow.ReadPage(root, pageBuf);
        var hdrBefore = BTreePageHeader.Parse(pageBuf.AsSpan(0));
        ushort contentOffsetBefore = hdrBefore.CellContentOffset;

        // Delete row 2 (which is at the lowest cell content offset)
        var (found, newRoot) = mutator.Delete(root, 2);
        Assert.True(found);

        // After delete, CellContentOffset should be higher (pointing to remaining row 1)
        shadow.ReadPage(newRoot, pageBuf);
        var hdrAfter = BTreePageHeader.Parse(pageBuf.AsSpan(0));
        Assert.True(hdrAfter.CellContentOffset >= contentOffsetBefore);
        Assert.Equal(1, hdrAfter.CellCount);
    }

    [Fact]
    public void Delete_SameRowIdTwice_SecondReturnsFalse()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertRow(mutator, 2, 1, 42, "hello");

        var (found1, r1) = mutator.Delete(root, 1);
        Assert.True(found1);

        var (found2, _) = mutator.Delete(r1, 1);
        Assert.False(found2);
    }

    // ── Round 4: Multi-page tree ───────────────────────────────────

    private static uint InsertManyRows(BTreeMutator mutator, uint root, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string name = $"user_{i:D5}";
            root = InsertRow(mutator, root, i, i, name);
        }
        return root;
    }

    [Fact]
    public void Delete_MultiPageTree_DeleteFromLeaf_RowDisappears()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        var (found, newRoot) = mutator.Delete(root, 50);
        Assert.True(found);

        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(199, rowIds.Count);
        Assert.DoesNotContain(50L, rowIds);
    }

    [Fact]
    public void Delete_MultiPageTree_DeleteLastRowId_RowDisappears()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        var (found, newRoot) = mutator.Delete(root, 200);
        Assert.True(found);

        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(199, rowIds.Count);
        Assert.DoesNotContain(200L, rowIds);
    }

    [Fact]
    public void Delete_MultiPageTree_DeleteFirstRowId_RowDisappears()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        var (found, newRoot) = mutator.Delete(root, 1);
        Assert.True(found);

        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(199, rowIds.Count);
        Assert.DoesNotContain(1L, rowIds);
        Assert.Equal(2L, rowIds[0]);
    }

    [Fact]
    public void Delete_MultiPageTree_DeletesAcrossLeaves_AllCorrect()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        foreach (var id in new long[] { 10, 50, 100, 150, 200 })
        {
            var (found, newRoot) = mutator.Delete(root, id);
            Assert.True(found);
            root = newRoot;
        }

        var rowIds = ScanAllRowIds(shadow, root);
        Assert.Equal(195, rowIds.Count);
        foreach (var id in new long[] { 10, 50, 100, 150, 200 })
            Assert.DoesNotContain(id, rowIds);
    }

    [Fact]
    public void Delete_ThenInsert_NewRowUsesNextRowId()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 5; i++)
            root = InsertRow(mutator, root, i, i * 10, $"row{i}");

        // Delete row 3
        var (_, r) = mutator.Delete(root, 3);
        root = r;

        // Insert new row — should get rowid 6 (max=5 + 1), not 3
        long nextRowId = mutator.GetMaxRowId(root) + 1;
        Assert.Equal(6L, nextRowId);
        root = InsertRow(mutator, root, nextRowId, 999, "new");

        var rowIds = ScanAllRowIds(shadow, root);
        Assert.Equal(5, rowIds.Count);
        Assert.Equal(new long[] { 1, 2, 4, 5, 6 }, rowIds);
    }

    [Fact]
    public void Delete_LeafDelete_RootPageUnchanged()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        var (_, newRoot) = mutator.Delete(root, 50);
        Assert.Equal(root, newRoot);
    }

    [Fact]
    public void Delete_FromOneLeaf_SiblingLeafUnmodified()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertManyRows(mutator, 2, 200);

        // Capture all row data before delete
        var beforeRows = new Dictionary<long, byte[]>();
        using (var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize))
        {
            while (cursor.MoveNext())
                beforeRows[cursor.RowId] = cursor.Payload.ToArray();
        }

        var (_, newRoot) = mutator.Delete(root, 1);

        // Verify all other rows have identical payload
        using var cursor2 = new BTreeCursor<ShadowPageSource>(shadow, newRoot, UsableSize);
        while (cursor2.MoveNext())
        {
            Assert.True(beforeRows.ContainsKey(cursor2.RowId));
            Assert.Equal(beforeRows[cursor2.RowId], cursor2.Payload.ToArray());
        }
    }
}
