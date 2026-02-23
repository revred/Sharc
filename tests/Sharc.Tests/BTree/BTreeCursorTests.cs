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

using System.Buffers.Binary;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Sharc.Exceptions;
using Xunit;

namespace Sharc.Tests.BTree;

public class BTreeCursorTests
{
    private const int PageSize = 4096;

    /// <summary>
    /// Creates a minimal database with a valid header on page 1, then writes
    /// a table leaf b-tree on the specified page.
    /// </summary>
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
    /// Writes a leaf table page at the given offset within the database byte array.
    /// Each cell is a simple record: [payloadSize varint][rowId varint][payload bytes].
    /// The payload is a minimal SQLite record: [headerSize=2 varint][serialType=9 varint] (constant 1).
    /// </summary>
    private static void WriteLeafTablePage(byte[] db, int pageOffset, (long rowId, byte[] payload)[] cells)
    {
        // Write page type
        db[pageOffset] = 0x0D; // Leaf table

        // Build cells and calculate positions
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

        // Place cells from the end of the page toward the beginning
        int cellContentStart = PageSize;
        var cellOffsets = new List<int>();
        foreach (var cell in cellBytes)
        {
            cellContentStart -= cell.Length;
            cell.CopyTo(db, pageOffset + cellContentStart);
            cellOffsets.Add(cellContentStart);
        }

        // Write cell count
        ushort cellCount = (ushort)cells.Length;
        db[pageOffset + 3] = (byte)(cellCount >> 8);
        db[pageOffset + 4] = (byte)(cellCount & 0xFF);

        // Write cell content offset
        db[pageOffset + 5] = (byte)(cellContentStart >> 8);
        db[pageOffset + 6] = (byte)(cellContentStart & 0xFF);

        // Write cell pointer array (starts at offset 8 for leaf pages)
        int pointerOffset = pageOffset + 8;
        foreach (var cellOff in cellOffsets)
        {
            db[pointerOffset] = (byte)(cellOff >> 8);
            db[pointerOffset + 1] = (byte)(cellOff & 0xFF);
            pointerOffset += 2;
        }
    }

    /// <summary>
    /// Writes an interior table page with cells pointing to child pages.
    /// Each cell is: [4-byte left child page][rowId varint].
    /// </summary>
    private static void WriteInteriorTablePage(byte[] db, int pageOffset,
        (uint leftChild, long rowId)[] cells, uint rightChild)
    {
        db[pageOffset] = 0x05; // Interior table

        // Build cells
        var cellBytes = new List<byte[]>();
        foreach (var (leftChild2, rowId) in cells)
        {
            var cell = new byte[13]; // 4 + max 9
            BinaryPrimitives.WriteUInt32BigEndian(cell, leftChild2);
            int len = 4 + VarintDecoder.Write(cell.AsSpan(4), rowId);
            cellBytes.Add(cell[..len]);
        }

        // Place cells from end of page
        int cellContentStart = PageSize;
        var cellOffsets = new List<int>();
        foreach (var cell in cellBytes)
        {
            cellContentStart -= cell.Length;
            cell.CopyTo(db, pageOffset + cellContentStart);
            cellOffsets.Add(cellContentStart);
        }

        // Cell count
        ushort cellCount = (ushort)cells.Length;
        db[pageOffset + 3] = (byte)(cellCount >> 8);
        db[pageOffset + 4] = (byte)(cellCount & 0xFF);

        // Cell content offset
        db[pageOffset + 5] = (byte)(cellContentStart >> 8);
        db[pageOffset + 6] = (byte)(cellContentStart & 0xFF);

        // Right child (offset 8 for interior pages)
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

    /// <summary>
    /// Builds a minimal SQLite record payload with one integer column (constant 1).
    /// Record format: [header_size=2][serial_type=9] â†’ body is empty (constant 1).
    /// </summary>
    private static byte[] MakeSimpleRecord()
    {
        // header_size varint = 2, serial_type varint = 9 (constant 1), no body
        return [0x02, 0x09];
    }

    [Fact]
    public void MoveNext_SingleLeafPage_EnumeratesAllCells()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [
                (1, record),
                (2, record),
                (3, record),
            ]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

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
            // Write an empty leaf page (0 cells)
            WriteLeafTablePage(data, ps, []);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

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

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(record.Length, cursor.PayloadSize);
        Assert.True(cursor.Payload.SequenceEqual(record));
    }

    [Fact]
    public void MoveNext_TwoLeafPagesWithInterior_EnumeratesInOrder()
    {
        // Page layout:
        // Page 1: db header
        // Page 2: interior page â†’ left child = page 3, right child = page 4
        // Page 3: leaf page with rows 1, 2
        // Page 4: leaf page with rows 3, 4
        var record = MakeSimpleRecord();
        var db = CreateDatabase(4, (data, ps) =>
        {
            // Page 2 (offset ps) = interior page
            WriteInteriorTablePage(data, ps,
                cells: [(3, 2)],  // left child = page 3, rowId key = 2
                rightChild: 4);

            // Page 3 (offset 2*ps) = leaf with rows 1, 2
            WriteLeafTablePage(data, 2 * ps, [(1, record), (2, record)]);

            // Page 4 (offset 3*ps) = leaf with rows 3, 4
            WriteLeafTablePage(data, 3 * ps, [(3, record), (4, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(4, rowIds.Count);
        Assert.Equal(1L, rowIds[0]);
        Assert.Equal(2L, rowIds[1]);
        Assert.Equal(3L, rowIds[2]);
        Assert.Equal(4L, rowIds[3]);
    }

    [Fact]
    public void MoveNext_Page1Root_AccountsForDatabaseHeaderOffset()
    {
        // Put the b-tree root on page 1 (the sqlite_schema table).
        // Page 1 has 100-byte DB header, so b-tree header starts at offset 100.
        var record = MakeSimpleRecord();
        var db = CreateDatabase(1);

        // Write leaf table page at page 1, offset 100
        int headerOffset = 100;
        db[headerOffset] = 0x0D; // Leaf table
        db[headerOffset + 3] = 0; db[headerOffset + 4] = 1; // 1 cell

        // Build cell
        var cell = new byte[18 + record.Length];
        int off = VarintDecoder.Write(cell, record.Length);
        off += VarintDecoder.Write(cell.AsSpan(off), 1L);
        record.CopyTo(cell, off);
        off += record.Length;
        var cellData = cell[..off];

        // Place cell at end of page
        int cellStart = PageSize - cellData.Length;
        cellData.CopyTo(db, cellStart);

        // Cell content offset
        db[headerOffset + 5] = (byte)(cellStart >> 8);
        db[headerOffset + 6] = (byte)(cellStart & 0xFF);

        // Cell pointer at offset 108 (100 + 8 for leaf header)
        db[headerOffset + 8] = (byte)(cellStart >> 8);
        db[headerOffset + 9] = (byte)(cellStart & 0xFF);

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(1); // page 1 root

        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_ThreeLeafPagesWithTwoInteriorCells_EnumeratesAll()
    {
        // Page 2: interior with cells [(left=3, key=2), (left=4, key=4)], right=5
        // Page 3: leaf with rows 1, 2
        // Page 4: leaf with rows 3, 4
        // Page 5: leaf with rows 5, 6
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

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, rowIds.ToArray());
    }

    // --- Overflow payload assembly ---

    [Fact]
    public void MoveNext_OverflowPayload_AssemblesCorrectly()
    {
        // Create a payload large enough to overflow.
        // For usablePageSize 4096: inline threshold X = 4096 - 35 = 4061.
        // M = ((4096-12)*32/255)-23 = 489
        // Use a 5000-byte payload → inline = CalculateInlinePayloadSize(5000, 4096) = 908
        // Remaining 4092 bytes go to an overflow page.
        int totalPayload = 5000;
        var fullPayload = new byte[totalPayload];
        for (int i = 0; i < totalPayload; i++)
            fullPayload[i] = (byte)(i & 0xFF);

        int usable = PageSize; // no reserved
        int inlineSize = CellParser.CalculateInlinePayloadSize(totalPayload, usable);
        int overflowDataSize = usable - 4; // 4092 bytes of data per overflow page

        // Build the database: page 1 = header, page 2 = leaf, page 3 = overflow
        var db = new byte[PageSize * 3];
        WriteHeader(db, PageSize, 3);

        // --- Build the cell manually on page 2 ---
        int pageOffset = PageSize;
        db[pageOffset] = 0x0D; // Leaf table

        var cellBuf = new byte[18 + inlineSize + 4]; // varints + inline + overflow ptr
        int off = VarintDecoder.Write(cellBuf, totalPayload);
        off += VarintDecoder.Write(cellBuf.AsSpan(off), 1L); // rowId = 1
        // Copy inline portion of payload
        Array.Copy(fullPayload, 0, cellBuf, off, inlineSize);
        off += inlineSize;
        // Write overflow page pointer (page 3)
        BinaryPrimitives.WriteUInt32BigEndian(cellBuf.AsSpan(off), 3);
        off += 4;
        var cellData = cellBuf[..off];

        // Place cell at end of page
        int cellStart = PageSize - cellData.Length;
        cellData.CopyTo(db, pageOffset + cellStart);

        // Write page 2 header fields
        db[pageOffset + 3] = 0; db[pageOffset + 4] = 1; // 1 cell
        db[pageOffset + 5] = (byte)(cellStart >> 8);
        db[pageOffset + 6] = (byte)(cellStart & 0xFF);
        db[pageOffset + 8] = (byte)(cellStart >> 8);
        db[pageOffset + 9] = (byte)(cellStart & 0xFF);

        // --- Build overflow page (page 3) ---
        int ovfOffset = PageSize * 2;
        // Next overflow page = 0 (no more)
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(ovfOffset), 0);
        // Copy remaining payload data
        int remaining = totalPayload - inlineSize;
        Array.Copy(fullPayload, inlineSize, db, ovfOffset + 4, remaining);

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.Equal(totalPayload, cursor.PayloadSize);

        // Verify full payload was assembled correctly
        var assembledPayload = cursor.Payload.ToArray();
        Assert.Equal(fullPayload, assembledPayload);
    }

    [Fact]
    public void MoveNext_OverflowCycleDetected_ThrowsCorruptPageException()
    {
        // Payload must need 2+ overflow pages so the cycle on page 3 → page 3 triggers.
        // For pageSize 4096: overflowDataSize = 4092, inlineSize ≈ 908.
        // 10000 - 908 = 9092, needs ⌈9092/4092⌉ = 3 overflow pages.
        // But page 3 points back to itself, so on second visit → cycle detected.
        int totalPayload = 10000;
        var fullPayload = new byte[totalPayload];
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
        BinaryPrimitives.WriteUInt32BigEndian(cellBuf.AsSpan(off), 3); // overflow → page 3
        off += 4;
        var cellData = cellBuf[..off];

        int cellStart = PageSize - cellData.Length;
        cellData.CopyTo(db, pageOffset + cellStart);

        db[pageOffset + 3] = 0; db[pageOffset + 4] = 1;
        db[pageOffset + 5] = (byte)(cellStart >> 8);
        db[pageOffset + 6] = (byte)(cellStart & 0xFF);
        db[pageOffset + 8] = (byte)(cellStart >> 8);
        db[pageOffset + 9] = (byte)(cellStart & 0xFF);

        // Overflow page 3 points back to itself → cycle on second visit
        int ovfOffset = PageSize * 2;
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(ovfOffset), 3); // cycle!

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        Assert.Throws<CorruptPageException>(() => cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_MultiCellWithOverflow_PayloadCorrectPerCell()
    {
        // Two cells: first is small (inline), second overflows
        var smallRecord = MakeSimpleRecord();
        int totalPayload = 5000;
        var largePayload = new byte[totalPayload];
        for (int i = 0; i < totalPayload; i++)
            largePayload[i] = (byte)((i + 0x42) & 0xFF);

        int usable = PageSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(totalPayload, usable);

        // page 1 = header, page 2 = leaf, page 3 = overflow
        var db = new byte[PageSize * 3];
        WriteHeader(db, PageSize, 3);

        int pageOffset = PageSize;
        db[pageOffset] = 0x0D;

        // Build cell 1 (small, inline)
        var cell1Buf = new byte[18 + smallRecord.Length];
        int off1 = VarintDecoder.Write(cell1Buf, smallRecord.Length);
        off1 += VarintDecoder.Write(cell1Buf.AsSpan(off1), 1L);
        smallRecord.CopyTo(cell1Buf, off1);
        off1 += smallRecord.Length;
        var cell1 = cell1Buf[..off1];

        // Build cell 2 (overflow)
        var cell2Buf = new byte[18 + inlineSize + 4];
        int off2 = VarintDecoder.Write(cell2Buf, totalPayload);
        off2 += VarintDecoder.Write(cell2Buf.AsSpan(off2), 2L);
        Array.Copy(largePayload, 0, cell2Buf, off2, inlineSize);
        off2 += inlineSize;
        BinaryPrimitives.WriteUInt32BigEndian(cell2Buf.AsSpan(off2), 3);
        off2 += 4;
        var cell2 = cell2Buf[..off2];

        // Place cells from end of page
        int cell2Start = PageSize - cell2.Length;
        cell2.CopyTo(db, pageOffset + cell2Start);
        int cell1Start = cell2Start - cell1.Length;
        cell1.CopyTo(db, pageOffset + cell1Start);

        // Header fields
        db[pageOffset + 3] = 0; db[pageOffset + 4] = 2; // 2 cells
        db[pageOffset + 5] = (byte)(cell1Start >> 8);
        db[pageOffset + 6] = (byte)(cell1Start & 0xFF);
        // Cell pointers
        db[pageOffset + 8] = (byte)(cell1Start >> 8);
        db[pageOffset + 9] = (byte)(cell1Start & 0xFF);
        db[pageOffset + 10] = (byte)(cell2Start >> 8);
        db[pageOffset + 11] = (byte)(cell2Start & 0xFF);

        // Overflow page 3
        int ovfOffset = PageSize * 2;
        BinaryPrimitives.WriteUInt32BigEndian(db.AsSpan(ovfOffset), 0);
        int remaining = totalPayload - inlineSize;
        Array.Copy(largePayload, inlineSize, db, ovfOffset + 4, remaining);

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        using var cursor = reader.CreateCursor(2);

        // Cell 1: small inline
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
        Assert.Equal(smallRecord.Length, cursor.PayloadSize);
        Assert.True(cursor.Payload.SequenceEqual(smallRecord));

        // Cell 2: overflow assembled
        Assert.True(cursor.MoveNext());
        Assert.Equal(2L, cursor.RowId);
        Assert.Equal(totalPayload, cursor.PayloadSize);
        Assert.Equal(largePayload, cursor.Payload.ToArray());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        var cursor = reader.CreateCursor(2);

        cursor.Dispose();
        cursor.Dispose(); // should not throw
    }

    [Fact]
    public void MoveNext_AfterDispose_ReturnsFalse()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader<MemoryPageSource>(source, header);
        var cursor = reader.CreateCursor(2);
        cursor.Dispose();

        // After Dispose, _exhausted is set — MoveNext returns false (no throw).
        // Disposed check is a Debug.Assert, not a runtime branch.
        Assert.False(cursor.MoveNext());
    }
}
