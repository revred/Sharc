// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers.Binary;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

/// <summary>
/// Tests for <see cref="LeafPageScanner"/>, a scan-optimized cursor that
/// pre-collects leaf page numbers and iterates cells without B-tree stack navigation.
/// Each test verifies equivalence with <see cref="BTreeCursor"/> for the same data.
/// </summary>
public class LeafPageScannerTests
{
    private const int PageSize = 4096;

    #region Test Infrastructure (shared with BTreeCursorTests)

    private static byte[] CreateDatabase(int pageCount, Action<byte[], int>? setupPages = null)
    {
        var data = new byte[PageSize * pageCount];
        WriteHeader(data, PageSize, pageCount);
        setupPages?.Invoke(data, PageSize);
        return data;
    }

    private static void WriteHeader(byte[] data, int pageSize, int pageCount)
    {
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[20] = 0; data[21] = 64; data[22] = 32; data[23] = 32;
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1;
    }

    private static void WriteLeafTablePage(byte[] db, int pageOffset, (long rowId, byte[] payload)[] cells)
    {
        db[pageOffset] = 0x0D; // Leaf table
        var cellBytes = new List<byte[]>();
        foreach (var (rowId, payload) in cells)
        {
            var cell = new byte[18 + payload.Length];
            int off = VarintDecoder.Write(cell, payload.Length);
            off += VarintDecoder.Write(cell.AsSpan(off), rowId);
            payload.CopyTo(cell, off);
            off += payload.Length;
            cellBytes.Add(cell[..off]);
        }

        int cellContentStart = PageSize;
        var cellOffsets = new List<int>();
        foreach (var cell in cellBytes)
        {
            cellContentStart -= cell.Length;
            cell.CopyTo(db, pageOffset + cellContentStart);
            cellOffsets.Add(cellContentStart);
        }

        ushort cellCount = (ushort)cells.Length;
        db[pageOffset + 3] = (byte)(cellCount >> 8);
        db[pageOffset + 4] = (byte)(cellCount & 0xFF);
        db[pageOffset + 5] = (byte)(cellContentStart >> 8);
        db[pageOffset + 6] = (byte)(cellContentStart & 0xFF);

        int pointerOffset = pageOffset + 8;
        foreach (var cellOff in cellOffsets)
        {
            db[pointerOffset] = (byte)(cellOff >> 8);
            db[pointerOffset + 1] = (byte)(cellOff & 0xFF);
            pointerOffset += 2;
        }
    }

    private static void WriteInteriorTablePage(byte[] db, int pageOffset,
        (uint leftChild, long rowId)[] cells, uint rightChild)
    {
        db[pageOffset] = 0x05; // Interior table
        var cellBytes = new List<byte[]>();
        foreach (var (leftChild2, rowId) in cells)
        {
            var cell = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(cell, leftChild2);
            int len = 4 + VarintDecoder.Write(cell.AsSpan(4), rowId);
            cellBytes.Add(cell[..len]);
        }

        int cellContentStart = PageSize;
        var cellOffsets = new List<int>();
        foreach (var cell in cellBytes)
        {
            cellContentStart -= cell.Length;
            cell.CopyTo(db, pageOffset + cellContentStart);
            cellOffsets.Add(cellContentStart);
        }

        ushort cellCount = (ushort)cells.Length;
        db[pageOffset + 3] = (byte)(cellCount >> 8);
        db[pageOffset + 4] = (byte)(cellCount & 0xFF);
        db[pageOffset + 5] = (byte)(cellContentStart >> 8);
        db[pageOffset + 6] = (byte)(cellContentStart & 0xFF);
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(pageOffset + 8), rightChild);

        int pointerOffset = pageOffset + 12;
        foreach (var cellOff in cellOffsets)
        {
            db[pointerOffset] = (byte)(cellOff >> 8);
            db[pointerOffset + 1] = (byte)(cellOff & 0xFF);
            pointerOffset += 2;
        }
    }

    private static byte[] MakeSimpleRecord()
    {
        return [0x02, 0x09]; // header_size=2, serial_type=9 (constant 1)
    }

    private static IBTreeCursor CreateScanCursor(byte[] db, uint rootPage)
    {
        var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        return reader.CreateScanCursor(rootPage);
    }

    #endregion

    // --- Scan equivalence tests ---

    [Fact]
    public void MoveNext_SingleLeafPage_EnumeratesAllCells()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record), (2, record), (3, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(3, rowIds.Count);
        Assert.Equal(1L, rowIds[0]);
        Assert.Equal(2L, rowIds[1]);
        Assert.Equal(3L, rowIds[2]);
    }

    [Fact]
    public void MoveNext_EmptyLeaf_ReturnsFalseImmediately()
    {
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, []);
        });

        using var cursor = CreateScanCursor(db, 2);

        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_PayloadIsAccessible()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(record.Length, cursor.PayloadSize);
        Assert.True(cursor.Payload.SequenceEqual(record));
    }

    [Fact]
    public void MoveNext_TwoLeafPagesWithInterior_EnumeratesInOrder()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                cells: [(3, 2)],
                rightChild: 4);
            WriteLeafTablePage(data, 2 * ps, [(1, record), (2, record)]);
            WriteLeafTablePage(data, 3 * ps, [(3, record), (4, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(new long[] { 1, 2, 3, 4 }, rowIds.ToArray());
    }

    [Fact]
    public void MoveNext_ThreeLeafPagesWithTwoInteriorCells_EnumeratesAll()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(5, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                cells: [(3, 2), (4, 4)],
                rightChild: 5);
            WriteLeafTablePage(data, 2 * ps, [(1, record), (2, record)]);
            WriteLeafTablePage(data, 3 * ps, [(3, record), (4, record)]);
            WriteLeafTablePage(data, 4 * ps, [(5, record), (6, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, rowIds.ToArray());
    }

    [Fact]
    public void MoveNext_OverflowPayload_AssemblesCorrectly()
    {
        int totalPayload = 5000;
        var fullPayload = new byte[totalPayload];
        for (int i = 0; i < totalPayload; i++)
            fullPayload[i] = (byte)(i & 0xFF);

        int usable = PageSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(totalPayload, usable);

        var db = new byte[PageSize * 3];
        WriteHeader(db, PageSize, 3);

        int pageOffset = PageSize;
        db[pageOffset] = 0x0D;

        var cellBuf = new byte[18 + inlineSize + 4];
        int off = VarintDecoder.Write(cellBuf, totalPayload);
        off += VarintDecoder.Write(cellBuf.AsSpan(off), 1L);
        Array.Copy(fullPayload, 0, cellBuf, off, inlineSize);
        off += inlineSize;
        BinaryPrimitives.WriteUInt32BigEndian(cellBuf.AsSpan(off), 3);
        off += 4;
        var cellData = cellBuf[..off];

        int cellStart = PageSize - cellData.Length;
        cellData.CopyTo(db, pageOffset + cellStart);

        db[pageOffset + 3] = 0; db[pageOffset + 4] = 1;
        db[pageOffset + 5] = (byte)(cellStart >> 8);
        db[pageOffset + 6] = (byte)(cellStart & 0xFF);
        db[pageOffset + 8] = (byte)(cellStart >> 8);
        db[pageOffset + 9] = (byte)(cellStart & 0xFF);

        int ovfOffset = PageSize * 2;
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(ovfOffset), 0);
        int remaining = totalPayload - inlineSize;
        Array.Copy(fullPayload, inlineSize, db, ovfOffset + 4, remaining);

        using var cursor = CreateScanCursor(db, 2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.Equal(totalPayload, cursor.PayloadSize);
        Assert.Equal(fullPayload, cursor.Payload.ToArray());
    }

    [Fact]
    public void Reset_RestartsFromBeginning()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record), (2, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        // First pass
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.True(cursor.MoveNext());
        Assert.Equal(2L, cursor.RowId);
        Assert.False(cursor.MoveNext());

        // Reset and second pass
        cursor.Reset();
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.True(cursor.MoveNext());
        Assert.Equal(2L, cursor.RowId);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        Assert.Throws<NotSupportedException>(() => cursor.Seek(1));
    }

    [Fact]
    public void MoveLast_ThrowsNotSupportedException()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var cursor = CreateScanCursor(db, 2);

        Assert.Throws<NotSupportedException>(() => cursor.MoveLast());
    }

    // --- Cross-verification: LeafPageScanner matches BTreeCursor ---

    [Fact]
    public void CrossVerify_MultiLevelTree_IdenticalResults()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(5, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                cells: [(3, 2), (4, 4)],
                rightChild: 5);
            WriteLeafTablePage(data, 2 * ps, [(1, record), (2, record)]);
            WriteLeafTablePage(data, 3 * ps, [(3, record), (4, record)]);
            WriteLeafTablePage(data, 4 * ps, [(5, record), (6, record)]);
        });

        // Collect results from BTreeCursor
        var source1 = new MemoryPageSource(db);
        var header1 = DatabaseHeader.Parse(db);
        var reader1 = new BTreeReader(source1, header1);
        using var btreeCursor = reader1.CreateCursor(2);
        var btreeRowIds = new List<long>();
        var btreePayloads = new List<byte[]>();
        while (btreeCursor.MoveNext())
        {
            btreeRowIds.Add(btreeCursor.RowId);
            btreePayloads.Add(btreeCursor.Payload.ToArray());
        }

        // Collect results from LeafPageScanner
        using var scanCursor = CreateScanCursor(db, 2);
        var scanRowIds = new List<long>();
        var scanPayloads = new List<byte[]>();
        while (scanCursor.MoveNext())
        {
            scanRowIds.Add(scanCursor.RowId);
            scanPayloads.Add(scanCursor.Payload.ToArray());
        }

        // Verify identical
        Assert.Equal(btreeRowIds, scanRowIds);
        Assert.Equal(btreePayloads.Count, scanPayloads.Count);
        for (int i = 0; i < btreePayloads.Count; i++)
            Assert.Equal(btreePayloads[i], scanPayloads[i]);
    }
}
