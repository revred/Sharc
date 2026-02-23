// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.BTree;

public class IndexBTreeCursorTests
{
    private const int PageSize = 4096;

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

    /// <summary>
    /// Builds a minimal index record payload with one integer key column and a rowid.
    /// Record format: [headerSize varint] [keySerialType varint] [rowidSerialType varint] [keyValue bytes] [rowidValue bytes]
    /// Serial type 1 = 8-bit integer (1 byte).
    /// </summary>
    private static byte[] MakeIndexRecord(byte keyValue, byte rowidValue)
    {
        // headerSize=3 (covers the header-size varint itself + 2 serial type varints)
        // serialType=1 (INT8, 1 byte) for key
        // serialType=1 (INT8, 1 byte) for rowid
        // body: keyValue (1 byte), rowidValue (1 byte)
        return [0x03, 0x01, 0x01, keyValue, rowidValue];
    }

    /// <summary>
    /// Builds an index record with a 4-byte integer key and 4-byte rowid.
    /// Serial type 4 = 32-bit BE integer (4 bytes).
    /// </summary>
    private static byte[] MakeIndexRecord32(int keyValue, int rowidValue)
    {
        var record = new byte[3 + 4 + 4]; // header(3) + key(4) + rowid(4)
        record[0] = 0x03; // headerSize
        record[1] = 0x04; // serialType 4 (INT32)
        record[2] = 0x04; // serialType 4 (INT32)
        BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(3), keyValue);
        BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(7), rowidValue);
        return record;
    }

    /// <summary>
    /// Writes a leaf index page at the given offset.
    /// Each cell is: [payloadSize varint] [payload bytes].
    /// </summary>
    private static void WriteLeafIndexPage(byte[] db, int pageOffset, byte[][] payloads)
    {
        db[pageOffset] = 0x0A; // Leaf index

        var cellBytes = new List<byte[]>();
        foreach (var payload in payloads)
        {
            var cell = new byte[9 + payload.Length];
            int off = VarintDecoder.Write(cell, payload.Length);
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

        ushort cellCount = (ushort)payloads.Length;
        db[pageOffset + 3] = (byte)(cellCount >> 8);
        db[pageOffset + 4] = (byte)(cellCount & 0xFF);

        db[pageOffset + 5] = (byte)(cellContentStart >> 8);
        db[pageOffset + 6] = (byte)(cellContentStart & 0xFF);

        // Cell pointer array starts at offset 8 for leaf pages
        int pointerOffset = pageOffset + 8;
        foreach (var cellOff in cellOffsets)
        {
            db[pointerOffset] = (byte)(cellOff >> 8);
            db[pointerOffset + 1] = (byte)(cellOff & 0xFF);
            pointerOffset += 2;
        }
    }

    /// <summary>
    /// Writes an interior index page.
    /// Each cell is: [4-byte left child] [payloadSize varint] [payload bytes].
    /// </summary>
    private static void WriteInteriorIndexPage(byte[] db, int pageOffset,
        (uint leftChild, byte[] payload)[] cells, uint rightChild)
    {
        db[pageOffset] = 0x02; // Interior index

        var cellBytes = new List<byte[]>();
        foreach (var (leftChild2, payload) in cells)
        {
            var cell = new byte[4 + 9 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(cell, leftChild2);
            int off = 4 + VarintDecoder.Write(cell.AsSpan(4), payload.Length);
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

        // Right child at offset 8 for interior pages
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(pageOffset + 8), rightChild);

        // Cell pointer array starts at offset 12 for interior pages
        int pointerOffset = pageOffset + 12;
        foreach (var cellOff in cellOffsets)
        {
            db[pointerOffset] = (byte)(cellOff >> 8);
            db[pointerOffset + 1] = (byte)(cellOff & 0xFF);
            pointerOffset += 2;
        }
    }

    [Fact]
    public void MoveNext_SingleLeafPage_EnumeratesAllEntries()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2, r3]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var payloads = new List<byte[]>();
        while (cursor.MoveNext())
        {
            payloads.Add(cursor.Payload.ToArray());
        }

        Assert.Equal(3, payloads.Count);
        Assert.Equal(r1, payloads[0]);
        Assert.Equal(r2, payloads[1]);
        Assert.Equal(r3, payloads[2]);
    }

    [Fact]
    public void MoveNext_EmptyLeaf_ReturnsFalseImmediately()
    {
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, []);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_PayloadMatchesRecord()
    {
        var record = MakeIndexRecord32(42, 7);
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [record]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(record.Length, cursor.PayloadSize);
        Assert.True(cursor.Payload.SequenceEqual(record));
    }

    [Fact]
    public void MoveNext_TwoLeafPagesWithInterior_EnumeratesInOrder()
    {
        // Page 2: interior index -> left child = page 3, right child = page 4
        // Page 3: leaf index with entries (key=10,rowid=1), (key=20,rowid=2)
        // Page 4: leaf index with entries (key=30,rowid=3), (key=40,rowid=4)
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);

        // The interior cell's payload is the divider key — same record format
        var divider = MakeIndexRecord(20, 2);

        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorIndexPage(data, ps,
                cells: [(3, divider)],
                rightChild: 4);

            WriteLeafIndexPage(data, 2 * ps, [r1, r2]);
            WriteLeafIndexPage(data, 3 * ps, [r3, r4]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var payloads = new List<byte[]>();
        while (cursor.MoveNext())
            payloads.Add(cursor.Payload.ToArray());

        Assert.Equal(4, payloads.Count);
        Assert.Equal(r1, payloads[0]);
        Assert.Equal(r2, payloads[1]);
        Assert.Equal(r3, payloads[2]);
        Assert.Equal(r4, payloads[3]);
    }

    [Fact]
    public void MoveNext_ThreeLeafPagesWithTwoInteriorCells_EnumeratesAll()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);
        var r5 = MakeIndexRecord(50, 5);
        var r6 = MakeIndexRecord(60, 6);

        var div1 = MakeIndexRecord(20, 2);
        var div2 = MakeIndexRecord(40, 4);

        var db = CreateDatabase(5, (data, ps) =>
        {
            // Page 2: interior with cells [(left=3, div1), (left=4, div2)], right=5
            WriteInteriorIndexPage(data, ps,
                cells: [(3, div1), (4, div2)],
                rightChild: 5);

            WriteLeafIndexPage(data, 2 * ps, [r1, r2]);
            WriteLeafIndexPage(data, 3 * ps, [r3, r4]);
            WriteLeafIndexPage(data, 4 * ps, [r5, r6]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var payloads = new List<byte[]>();
        while (cursor.MoveNext())
            payloads.Add(cursor.Payload.ToArray());

        Assert.Equal(6, payloads.Count);
        Assert.Equal(r1, payloads[0]);
        Assert.Equal(r6, payloads[5]);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var record = MakeIndexRecord(10, 1);
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [record]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        var cursor = reader.CreateIndexCursor(2);

        cursor.Dispose();
        cursor.Dispose(); // should not throw
    }

    [Fact]
    public void MoveNext_AfterDispose_ThrowsObjectDisposed()
    {
        var record = MakeIndexRecord(10, 1);
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [record]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        var cursor = reader.CreateIndexCursor(2);
        cursor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_PayloadSize_MatchesActualPayload()
    {
        var record = MakeIndexRecord32(999, 42);
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [record]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(record.Length, cursor.PayloadSize);
        Assert.Equal(cursor.PayloadSize, cursor.Payload.Length);
    }

    // ---- SeekFirst tests ----

    [Fact]
    public void SeekFirst_ExactMatch_ReturnsTrueAndPositions()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2, r3]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var found = cursor.SeekFirst(20);

        Assert.True(found);
        Assert.Equal(r2, cursor.Payload.ToArray());
    }

    [Fact]
    public void SeekFirst_NoMatch_ReturnsFalseAndPositionsAtNext()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(30, 3);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Seek for key=20 which doesn't exist; should position at key=30
        var found = cursor.SeekFirst(20);

        Assert.False(found);
        Assert.Equal(r2, cursor.Payload.ToArray());
    }

    [Fact]
    public void SeekFirst_KeyBeyondAll_ExhaustedCursor()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Key 99 is beyond all entries
        var found = cursor.SeekFirst(99);

        Assert.False(found);
        // Cursor should be exhausted — MoveNext returns false
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void SeekFirst_KeyBeforeAll_PositionsAtFirst()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Seek for key=5 (before all entries)
        var found = cursor.SeekFirst(5);

        Assert.False(found);
        Assert.Equal(r1, cursor.Payload.ToArray());
    }

    [Fact]
    public void SeekFirst_ThenMoveNext_ContinuesFromSeekPosition()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2, r3, r4]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        cursor.SeekFirst(20);

        // Collect remaining entries from seek position
        var payloads = new List<byte[]>();
        payloads.Add(cursor.Payload.ToArray()); // current (key=20)
        while (cursor.MoveNext())
            payloads.Add(cursor.Payload.ToArray());

        Assert.Equal(3, payloads.Count); // key=20, 30, 40
        Assert.Equal(r2, payloads[0]);
        Assert.Equal(r3, payloads[1]);
        Assert.Equal(r4, payloads[2]);
    }

    [Fact]
    public void SeekFirst_MultiLevel_SeeksToCorrectLeaf()
    {
        // Three-level tree: interior → two leaf pages
        // Page 2: interior with divider key=20, left child=page 3, right child=page 4
        // Page 3: leaf with keys 10, 20
        // Page 4: leaf with keys 30, 40
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);
        var divider = MakeIndexRecord(20, 2);

        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorIndexPage(data, ps,
                cells: [(3, divider)],
                rightChild: 4);

            WriteLeafIndexPage(data, 2 * ps, [r1, r2]);
            WriteLeafIndexPage(data, 3 * ps, [r3, r4]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Seek for key=30 which is in the right leaf (page 4)
        var found = cursor.SeekFirst(30);

        Assert.True(found);
        Assert.Equal(r3, cursor.Payload.ToArray());
    }

    [Fact]
    public void SeekFirst_MultiLevel_ThenMoveNextCrossesLeafBoundary()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);
        var divider = MakeIndexRecord(20, 2);

        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorIndexPage(data, ps,
                cells: [(3, divider)],
                rightChild: 4);

            WriteLeafIndexPage(data, 2 * ps, [r1, r2]);
            WriteLeafIndexPage(data, 3 * ps, [r3, r4]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Seek to key=20 (last entry in left leaf)
        cursor.SeekFirst(20);

        var payloads = new List<byte[]>();
        payloads.Add(cursor.Payload.ToArray());
        while (cursor.MoveNext())
            payloads.Add(cursor.Payload.ToArray());

        // Should continue from key=20 through key=30, 40
        Assert.Equal(3, payloads.Count);
        Assert.Equal(r2, payloads[0]);
        Assert.Equal(r3, payloads[1]);
        Assert.Equal(r4, payloads[2]);
    }

    [Fact]
    public void SeekFirst_Int32Keys_ExactMatch()
    {
        var r1 = MakeIndexRecord32(100, 1);
        var r2 = MakeIndexRecord32(200, 2);
        var r3 = MakeIndexRecord32(300, 3);
        var r4 = MakeIndexRecord32(400, 4);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [r1, r2, r3, r4]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var found = cursor.SeekFirst(300);

        Assert.True(found);
        Assert.Equal(r3, cursor.Payload.ToArray());
    }

    [Fact]
    public void SeekFirst_EmptyLeaf_ReturnsFalse()
    {
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, []);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        var found = cursor.SeekFirst(42);

        Assert.False(found);
    }

    [Fact]
    public void SeekFirst_AfterDispose_ThrowsObjectDisposed()
    {
        var record = MakeIndexRecord(10, 1);
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafIndexPage(data, ps, [record]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        var cursor = reader.CreateIndexCursor(2);
        cursor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cursor.SeekFirst(10));
    }

    [Fact]
    public void SeekFirst_ThreeLeafPages_SeeksToMiddle()
    {
        var r1 = MakeIndexRecord(10, 1);
        var r2 = MakeIndexRecord(20, 2);
        var r3 = MakeIndexRecord(30, 3);
        var r4 = MakeIndexRecord(40, 4);
        var r5 = MakeIndexRecord(50, 5);
        var r6 = MakeIndexRecord(60, 6);

        var div1 = MakeIndexRecord(20, 2);
        var div2 = MakeIndexRecord(40, 4);

        var db = CreateDatabase(5, (data, ps) =>
        {
            WriteInteriorIndexPage(data, ps,
                cells: [(3, div1), (4, div2)],
                rightChild: 5);

            WriteLeafIndexPage(data, 2 * ps, [r1, r2]);
            WriteLeafIndexPage(data, 3 * ps, [r3, r4]);
            WriteLeafIndexPage(data, 4 * ps, [r5, r6]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateIndexCursor(2);

        // Seek to key=40 in the middle leaf
        var found = cursor.SeekFirst(40);

        Assert.True(found);
        Assert.Equal(r4, cursor.Payload.ToArray());

        // MoveNext should continue to key=50, 60
        var remaining = new List<byte[]>();
        while (cursor.MoveNext())
            remaining.Add(cursor.Payload.ToArray());

        Assert.Equal(2, remaining.Count);
        Assert.Equal(r5, remaining[0]);
        Assert.Equal(r6, remaining[1]);
    }
}
