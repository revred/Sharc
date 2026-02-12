/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Primitives;
using Xunit;

namespace Sharc.Tests.DataStructures;

/// <summary>
/// Advanced BTreeCursor tests covering deep traversal, single-cell leaves,
/// ascending rowid invariant, and post-traversal behavior.
/// </summary>
public class BTreeCursorAdvancedTests
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
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pageCount);
        data[47] = 4;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(56), 1);
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

    // --- Three-level tree: Interior â†’ Interior â†’ Leaf ---
    // Tests that the cursor correctly descends through multiple interior levels.

    [Fact]
    public void MoveNext_ThreeLevelTree_EnumeratesAllLeaves()
    {
        // Page layout:
        // Page 1: db header
        // Page 2: root interior â†’ left=3(sub-interior), right=6(sub-interior)
        // Page 3: sub-interior â†’ left=4(leaf), right=5(leaf)
        // Page 4: leaf rows 1,2
        // Page 5: leaf rows 3,4
        // Page 6: sub-interior â†’ left=7(leaf), right=8(leaf)
        // Page 7: leaf rows 5,6
        // Page 8: leaf rows 7,8
        var record = MakeSimpleRecord();
        var db = CreateDatabase(8, (data, ps) =>
        {
            // Page 2: root interior, left=3, key=4, right=6
            WriteInteriorTablePage(data, ps, [(3, 4)], 6);

            // Page 3: sub-interior, left=4, key=2, right=5
            WriteInteriorTablePage(data, 2 * ps, [(4, 2)], 5);

            // Page 4: leaf, rows 1,2
            WriteLeafTablePage(data, 3 * ps, [(1, record), (2, record)]);

            // Page 5: leaf, rows 3,4
            WriteLeafTablePage(data, 4 * ps, [(3, record), (4, record)]);

            // Page 6: sub-interior, left=7, key=6, right=8
            WriteInteriorTablePage(data, 5 * ps, [(7, 6)], 8);

            // Page 7: leaf, rows 5,6
            WriteLeafTablePage(data, 6 * ps, [(5, record), (6, record)]);

            // Page 8: leaf, rows 7,8
            WriteLeafTablePage(data, 7 * ps, [(7, record), (8, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, rowIds.ToArray());
    }

    // --- Single cell per leaf (degenerate tree) ---

    [Fact]
    public void MoveNext_SingleCellPerLeaf_TraversesAll()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(5, (data, ps) =>
        {
            // Page 2: interior, cells pointing to pages 3,4,5 (one row each)
            WriteInteriorTablePage(data, ps,
                [(3, 1), (4, 2)], 5);

            WriteLeafTablePage(data, 2 * ps, [(1, record)]);
            WriteLeafTablePage(data, 3 * ps, [(2, record)]);
            WriteLeafTablePage(data, 4 * ps, [(3, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(new long[] { 1, 2, 3 }, rowIds.ToArray());
    }

    // --- RowIds always ascending (b-tree invariant) ---

    [Fact]
    public void MoveNext_RowIds_AlwaysAscending()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(4, (data, ps) =>
        {
            WriteInteriorTablePage(data, ps,
                [(3, 5)], 4);

            WriteLeafTablePage(data, 2 * ps, [(1, record), (3, record), (5, record)]);
            WriteLeafTablePage(data, 3 * ps, [(7, record), (9, record), (11, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        long prev = long.MinValue;
        while (cursor.MoveNext())
        {
            Assert.True(cursor.RowId > prev, $"RowId {cursor.RowId} not greater than previous {prev}");
            prev = cursor.RowId;
        }
    }

    // --- After full traversal, MoveNext returns false repeatedly ---

    [Fact]
    public void MoveNext_AfterFullTraversal_ReturnsFalseRepeatedly()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record), (2, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        while (cursor.MoveNext()) { } // exhaust

        Assert.False(cursor.MoveNext());
        Assert.False(cursor.MoveNext());
        Assert.False(cursor.MoveNext());
    }

    // --- Payload is accessible after MoveNext ---

    [Fact]
    public void Payload_AfterMoveNext_ContainsRecordBytes()
    {
        var record = MakeSimpleRecord();
        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, [(1, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        Assert.True(cursor.MoveNext());
        Assert.Equal(record.Length, cursor.PayloadSize);
        Assert.True(cursor.Payload.SequenceEqual(record));
    }

    // --- Large number of cells on a single leaf page ---

    [Fact]
    public void MoveNext_ManySmallCells_EnumeratesAll()
    {
        var record = MakeSimpleRecord();
        // Create 50 cells on a single leaf page (each ~4 bytes, easily fits in 4096)
        var cells = new (long, byte[])[50];
        for (int i = 0; i < 50; i++)
            cells[i] = (i + 1, record);

        var db = CreateDatabase(2, (data, ps) =>
        {
            WriteLeafTablePage(data, ps, cells);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        int count = 0;
        while (cursor.MoveNext())
            count++;

        Assert.Equal(50, count);
    }

    // --- Interior page with many children ---

    [Fact]
    public void MoveNext_InteriorWithFiveChildren_EnumeratesAll()
    {
        // 5 leaf pages, each with 2 rows = 10 total
        var record = MakeSimpleRecord();
        var db = CreateDatabase(7, (data, ps) =>
        {
            // Page 2: interior with 4 cells + right child = 5 children
            WriteInteriorTablePage(data, ps,
                [(3, 2), (4, 4), (5, 6), (6, 8)], 7);

            WriteLeafTablePage(data, 2 * ps, [(1, record), (2, record)]);
            WriteLeafTablePage(data, 3 * ps, [(3, record), (4, record)]);
            WriteLeafTablePage(data, 4 * ps, [(5, record), (6, record)]);
            WriteLeafTablePage(data, 5 * ps, [(7, record), (8, record)]);
            WriteLeafTablePage(data, 6 * ps, [(9, record), (10, record)]);
        });

        using var source = new MemoryPageSource(db);
        var header = DatabaseHeader.Parse(db);
        var reader = new BTreeReader(source, header);
        using var cursor = reader.CreateCursor(2);

        var rowIds = new List<long>();
        while (cursor.MoveNext())
            rowIds.Add(cursor.RowId);

        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i).ToArray(), rowIds.ToArray());
    }
}
