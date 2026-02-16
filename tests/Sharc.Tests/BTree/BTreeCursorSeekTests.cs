/*-------------------------------------------------------------------------------------------------!
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

/// <summary>
/// Tests for BTreeCursor.Seek — binary search on leaf and interior pages.
/// </summary>
public class BTreeCursorSeekTests
{
    private const int PageSize = 4096;

    #region Helpers (same patterns as BTreeCursorTests)

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
        db[pageOffset] = 0x0D;
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
        db[pageOffset] = 0x05;
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

    private static byte[] MakeSimpleRecord() => [0x02, 0x09];

    #endregion

    // --- Seek: exact match on single leaf ---

    [Fact]
    public void Seek_ExactMatch_ReturnsTrueAndPositions()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(10, record), (20, record), (30, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        bool exact = cursor.Seek(20);

        Assert.True(exact);
        Assert.Equal(20L, cursor.RowId);
    }

    // --- Seek: no match positions at successor ---

    [Fact]
    public void Seek_NoMatch_ReturnsFalseAndPositionsAtNext()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(10, record), (20, record), (30, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        bool exact = cursor.Seek(15); // Between 10 and 20

        Assert.False(exact);
        Assert.Equal(20L, cursor.RowId); // Positioned at next row after 15
    }

    // --- Seek: beyond all rows exhausts cursor ---

    [Fact]
    public void Seek_BeyondAll_ExhaustsCursor()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(10, record), (20, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        bool exact = cursor.Seek(99);

        Assert.False(exact);
        Assert.False(cursor.MoveNext()); // Cursor exhausted
    }

    // --- Seek: before all rows positions at first ---

    [Fact]
    public void Seek_BeforeAll_PositionsAtFirst()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(10, record), (20, record), (30, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        bool exact = cursor.Seek(1); // Before all rows

        Assert.False(exact);
        Assert.Equal(10L, cursor.RowId);
    }

    // --- Seek: multi-level tree descends through interior ---

    [Fact]
    public void Seek_MultiLevel_DescendsThroughInterior()
    {
        // Page 2: interior → left child=3 (key=20), right child=4
        // Page 3: leaf rows 10, 20
        // Page 4: leaf rows 30, 40
        var record = MakeSimpleRecord();
        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                cells: [(3, 20)],
                rightChild: 4);
            WriteLeafTablePage(data, 2 * ps, [(10, record), (20, record)]);
            WriteLeafTablePage(data, 3 * ps, [(30, record), (40, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        // Seek to a row in the right leaf
        bool exact = cursor.Seek(30);

        Assert.True(exact);
        Assert.Equal(30L, cursor.RowId);
    }

    // --- Seek then MoveNext continues traversal ---

    [Fact]
    public void Seek_ThenMoveNext_ContinuesFromSeekPosition()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                cells: [(3, 20)],
                rightChild: 4);
            WriteLeafTablePage(data, 2 * ps, [(10, record), (20, record)]);
            WriteLeafTablePage(data, 3 * ps, [(30, record), (40, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        cursor.Seek(20); // Position at row 20
        Assert.Equal(20L, cursor.RowId);

        // MoveNext should advance to 30, 40
        Assert.True(cursor.MoveNext());
        Assert.Equal(30L, cursor.RowId);

        Assert.True(cursor.MoveNext());
        Assert.Equal(40L, cursor.RowId);

        Assert.False(cursor.MoveNext()); // End
    }

    // --- Seek after Dispose throws ---

    [Fact]
    public void Seek_AfterDispose_ThrowsObjectDisposed()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        var cursor = reader.CreateCursor(2);
        cursor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cursor.Seek(1));
    }
}
