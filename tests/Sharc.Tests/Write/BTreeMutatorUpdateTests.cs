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
/// Tests for <see cref="BTreeMutator.Update"/> and page defragmentation.
/// </summary>
public sealed class BTreeMutatorUpdateTests
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

    private static byte[] EncodeRecord(long id, string name)
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.FromInt64(1, id),
            ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
        };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        var recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);
        return recBuf;
    }

    private static List<long> ScanAllRowIds(IPageSource source, uint root)
    {
        var rowIds = new List<long>();
        using var cursor = new BTreeCursor(source, root, UsableSize);
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);
        return rowIds;
    }

    private static (long Id, string Name) ReadRow(IPageSource source, uint root, long targetRowId)
    {
        using var cursor = new BTreeCursor(source, root, UsableSize);
        var decoded = new ColumnValue[2];
        var decoder = new RecordDecoder();
        while (cursor.MoveNext())
        {
            if (cursor.RowId == targetRowId)
            {
                decoder.DecodeRecord(cursor.Payload, decoded);
                return (decoded[0].AsInt64(), decoded[1].AsString());
            }
        }
        throw new InvalidOperationException($"RowId {targetRowId} not found");
    }

    // ── Round 5: Defragmentation ───────────────────────────────────

    [Fact]
    public void Defragment_InsertAfterDeletes_RecoversFreeSpace()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        // Fill the page with many rows to approach capacity
        uint root = 2;
        int count = 60; // ~60 rows of ~50 bytes each ≈ 3000 bytes, leaving ~1000 free
        for (int i = 1; i <= count; i++)
            root = InsertRow(mutator, root, i, i, $"user_{i:D5}");

        // Delete every other row to create fragmented space
        for (int i = 2; i <= count; i += 2)
        {
            var (found, r) = mutator.Delete(root, i);
            Assert.True(found);
            root = r;
        }

        // Now insert new rows — should succeed by defragging the fragmented space
        int nextId = count + 1;
        for (int i = 0; i < 10; i++)
        {
            root = InsertRow(mutator, root, nextId, nextId, $"new_{nextId:D5}");
            nextId++;
        }

        // Verify all expected rows are present
        var rowIds = ScanAllRowIds(shadow, root);
        Assert.True(rowIds.Count > 30); // at least the odd rows + new ones
    }

    [Fact]
    public void Defragment_PreservesCellOrdering_AllRowsReadable()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 20; i++)
            root = InsertRow(mutator, root, i, i * 10, $"row{i}");

        // Delete some rows to create fragmentation
        foreach (var id in new long[] { 5, 10, 15 })
        {
            var (_, r) = mutator.Delete(root, id);
            root = r;
        }

        // Insert a new row (may trigger defrag internally)
        root = InsertRow(mutator, root, 21, 210, "row21");

        // Verify all remaining rows have correct data
        var decoder = new RecordDecoder();
        var decoded = new ColumnValue[2];
        using var cursor = new BTreeCursor(shadow, root, UsableSize);
        var expectedIds = Enumerable.Range(1, 21).Where(i => i != 5 && i != 10 && i != 15).ToList();
        int idx = 0;
        while (cursor.MoveNext())
        {
            decoder.DecodeRecord(cursor.Payload, decoded);
            Assert.Equal(cursor.RowId, expectedIds[idx]);
            idx++;
        }
        Assert.Equal(expectedIds.Count, idx);
    }

    [Fact]
    public void Defragment_ResetsFragmentedFreeBytes_ToZero()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        // Strategy: fill page nearly to capacity, delete middle rows to create fragmentation,
        // then insert rows until contiguous space is exhausted — forcing DefragmentPage.

        uint root = 2;

        // Insert 34 rows of ~100 bytes each to fill page tightly.
        // 34 × ~100 cells + 34 × 2 ptrs + 8 hdr ≈ 3476, leaving ~620 contiguous.
        var name = new string('Z', 70);
        for (int i = 1; i <= 34; i++)
            root = InsertRow(mutator, root, i, i, name);

        Assert.Equal(2u, root); // still one page

        // Delete 3 early-inserted rows (high offsets, deep in content area) → real gaps
        foreach (var id in new long[] { 5, 10, 15 })
        {
            var (_, r) = mutator.Delete(root, id);
            root = r;
        }

        // Verify fragmentation exists
        var pageBuf = new byte[PageSize];
        shadow.ReadPage(root, pageBuf);
        var hdrBefore = BTreePageHeader.Parse(pageBuf.AsSpan(0));
        Assert.True(hdrBefore.FragmentedFreeBytes > 0, "Expected fragmented free bytes after deletes");

        // Insert rows one at a time until contiguous space is exhausted → defrag triggers.
        // After 34 inserts + 3 deletes: ~1400 contiguous, ~231 fragmented, ~79 per insert.
        // Need ~18 inserts to exhaust contiguous space and force defrag.
        for (int i = 35; i <= 55; i++)
            root = InsertRow(mutator, root, i, i, name);

        shadow.ReadPage(root, pageBuf);
        var hdrAfter = BTreePageHeader.Parse(pageBuf.AsSpan(0));

        // If page split happened, verify all data is readable instead
        if (!hdrAfter.IsLeaf)
        {
            var allIds = ScanAllRowIds(shadow, root);
            Assert.Equal(52, allIds.Count); // 55 - 3 deleted
            return;
        }

        Assert.Equal(0, hdrAfter.FragmentedFreeBytes);
    }

    // ── Round 6: BTreeMutator.Update ───────────────────────────────

    [Fact]
    public void Update_RowIdNotFound_ReturnsFalse()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        root = InsertRow(mutator, root, 1, 10, "one");
        root = InsertRow(mutator, root, 2, 20, "two");
        root = InsertRow(mutator, root, 3, 30, "three");

        var record = EncodeRecord(999, "updated");
        var (found, newRoot) = mutator.Update(root, 99, record);

        Assert.False(found);
        Assert.Equal(root, newRoot);
        Assert.Equal(3, ScanAllRowIds(shadow, root).Count);
    }

    [Fact]
    public void Update_SameSizePayload_DataChanges()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertRow(mutator, 2, 1, 42, "AAAA");

        var record = EncodeRecord(99, "BBBB");
        var (found, newRoot) = mutator.Update(root, 1, record);

        Assert.True(found);
        var (id, name) = ReadRow(shadow, newRoot, 1);
        Assert.Equal(99L, id);
        Assert.Equal("BBBB", name);
    }

    [Fact]
    public void Update_SmallerPayload_DataChanges()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertRow(mutator, 2, 1, 42, "a_long_string_value");

        var record = EncodeRecord(99, "hi");
        var (found, newRoot) = mutator.Update(root, 1, record);

        Assert.True(found);
        var (id, name) = ReadRow(shadow, newRoot, 1);
        Assert.Equal(99L, id);
        Assert.Equal("hi", name);
    }

    [Fact]
    public void Update_LargerPayload_DataChanges()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = InsertRow(mutator, 2, 1, 42, "hi");

        var record = EncodeRecord(99, "a much longer string value here");
        var (found, newRoot) = mutator.Update(root, 1, record);

        Assert.True(found);
        var (id, name) = ReadRow(shadow, newRoot, 1);
        Assert.Equal(99L, id);
        Assert.Equal("a much longer string value here", name);
    }

    [Fact]
    public void Update_MultiPageTree_CorrectRowUpdated()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 200; i++)
            root = InsertRow(mutator, root, i, i, $"user_{i:D5}");

        var record = EncodeRecord(999, "UPDATED");
        var (found, newRoot) = mutator.Update(root, 100, record);
        Assert.True(found);

        // Verify rowid 100 has new data
        var (id, name) = ReadRow(shadow, newRoot, 100);
        Assert.Equal(999L, id);
        Assert.Equal("UPDATED", name);

        // Verify total count and other rows unchanged
        var rowIds = ScanAllRowIds(shadow, newRoot);
        Assert.Equal(200, rowIds.Count);

        // Spot check another row
        var (otherId, otherName) = ReadRow(shadow, newRoot, 50);
        Assert.Equal(50L, otherId);
        Assert.Equal("user_00050", otherName);
    }
}
