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
/// Tests for <see cref="IBTreeCursor.IsStale"/> and <see cref="IIndexBTreeCursor.IsStale"/> —
/// passive staleness detection that compares cursor snapshot version against current DataVersion.
/// </summary>
public class CursorStalenessTests
{
    private const int PageSize = 4096;

    #region BTreeCursor IsStale

    [Fact]
    public void IsStale_NewCursor_ReturnsFalse()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        Assert.False(cursor.IsStale);
    }

    [Fact]
    public void IsStale_AfterExternalWrite_ReturnsTrue()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        // External write changes the DataVersion
        source.WritePage(2, new byte[PageSize]);

        Assert.True(cursor.IsStale);
    }

    [Fact]
    public void IsStale_AfterReset_ReturnsFalse()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        source.WritePage(2, new byte[PageSize]);
        Assert.True(cursor.IsStale);

        cursor.Reset();
        Assert.False(cursor.IsStale);
    }

    [Fact]
    public void IsStale_AfterSeek_ReturnsFalse()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        source.WritePage(2, new byte[PageSize]);
        Assert.True(cursor.IsStale);

        cursor.Seek(1);
        Assert.False(cursor.IsStale);
    }

    [Fact]
    public void IsStale_MultipleWrites_ReturnsTrue()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        source.WritePage(2, new byte[PageSize]);
        source.WritePage(2, new byte[PageSize]);
        source.WritePage(2, new byte[PageSize]);

        Assert.True(cursor.IsStale);
    }

    [Fact]
    public void IsStale_WriteAfterReset_ReturnsTrue()
    {
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        source.WritePage(2, new byte[PageSize]);
        cursor.Reset();
        Assert.False(cursor.IsStale);

        source.WritePage(2, new byte[PageSize]);
        Assert.True(cursor.IsStale);
    }

    [Fact]
    public void IsStale_ReadOnlySource_AlwaysFalse()
    {
        var data = CreateDatabaseWithOneRow();
        using var inner = new MemoryPageSource(data);
        var readOnly = new ReadOnlyPageSourceStub(inner);
        using var cursor = new BTreeCursor(readOnly, 1, PageSize);

        // Even though inner has changed, the read-only source reports DataVersion=0
        inner.WritePage(2, new byte[PageSize]);

        Assert.False(cursor.IsStale);
    }

    #endregion

    #region IndexBTreeCursor IsStale

    [Fact]
    public void IsStale_IndexCursor_SameSemantics()
    {
        var data = CreateDatabaseWithIndexPage();
        using var source = new MemoryPageSource(data);
        using var cursor = new IndexBTreeCursor(source, 2, PageSize);

        Assert.False(cursor.IsStale);

        source.WritePage(3, new byte[PageSize]);
        Assert.True(cursor.IsStale);
    }

    #endregion

    #region Cached Leaf Page + IsStale Interaction

    [Fact]
    public void CachedLeafPage_ResetClearsCache_ReadsFreshData()
    {
        // Scenario: Agent A's cursor reads data, Agent B writes to the same page,
        // Agent A detects staleness, resets, and the leaf cache is invalidated
        // so the next MoveNext re-fetches from the page source.
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        // Agent A reads the row
        Assert.True(cursor.MoveNext());
        Assert.Equal(1, cursor.RowId);
        var payloadBefore = cursor.Payload.ToArray();

        // Agent B writes to the leaf page (page 1), changing cell data
        var modifiedPage = data.AsSpan(0, PageSize).ToArray();
        // Modify the payload value (byte at the cell data position)
        int cellStart = PageSize - 20;
        // The cell structure is: payloadSize(varint) + rowid(varint) + header(2 bytes) + value(1 byte)
        // value is at cellStart + 4 (1 byte payloadSize + 1 byte rowid + 2 bytes header)
        modifiedPage[cellStart + 4] = 99; // Change value from 42 to 99
        source.WritePage(1, modifiedPage);

        // Agent A detects staleness
        Assert.True(cursor.IsStale);

        // Reset clears the cached leaf page
        cursor.Reset();
        Assert.False(cursor.IsStale);

        // Re-read gets fresh data from the page source (cache was invalidated)
        Assert.True(cursor.MoveNext());
        var payloadAfter = cursor.Payload.ToArray();

        // The payload should reflect the modified page data
        Assert.NotEqual(payloadBefore, payloadAfter);
    }

    [Fact]
    public void CachedLeafPage_TwoCursors_IndependentStaleness()
    {
        // Two agents each hold a cursor on the same page source.
        // A write makes both stale. Each cursor's cache is independent.
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursorA = new BTreeCursor(source, 1, PageSize);
        using var cursorB = new BTreeCursor(source, 1, PageSize);

        // Both read the same row
        Assert.True(cursorA.MoveNext());
        Assert.True(cursorB.MoveNext());
        Assert.False(cursorA.IsStale);
        Assert.False(cursorB.IsStale);

        // External write
        source.WritePage(2, new byte[PageSize]);

        // Both detect staleness independently
        Assert.True(cursorA.IsStale);
        Assert.True(cursorB.IsStale);

        // Agent A resets — only A's staleness clears
        cursorA.Reset();
        Assert.False(cursorA.IsStale);
        Assert.True(cursorB.IsStale);

        // Agent B resets independently
        cursorB.Reset();
        Assert.False(cursorB.IsStale);
    }

    [Fact]
    public void CachedLeafPage_WriteDoesNotAutoInvalidateCache()
    {
        // The cached leaf page is NOT automatically invalidated by an external write.
        // IsStale is the signal — the caller decides when to reset.
        // This tests that the cursor continues to function (return old data) even when stale,
        // which is the correct passive-detection semantic.
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        // Read initial data
        Assert.True(cursor.MoveNext());
        var originalPayload = cursor.Payload.ToArray();

        // External write to a different page — cursor is stale but leaf cache is unaffected
        source.WritePage(2, new byte[PageSize]);
        Assert.True(cursor.IsStale);

        // Cursor still returns the previously-read cell data (passive detection, no exception)
        var payloadAfterWrite = cursor.Payload.ToArray();
        Assert.Equal(originalPayload, payloadAfterWrite);
    }

    [Fact]
    public void CachedLeafPage_SeekRefreshesBothVersionAndCache()
    {
        // Seek() refreshes the snapshot version AND the cached leaf page,
        // since it navigates to a potentially different leaf.
        var data = CreateDatabaseWithOneRow();
        using var source = new MemoryPageSource(data);
        using var cursor = new BTreeCursor(source, 1, PageSize);

        // Read initial data
        Assert.True(cursor.MoveNext());
        Assert.False(cursor.IsStale);

        // External write
        source.WritePage(2, new byte[PageSize]);
        Assert.True(cursor.IsStale);

        // Seek refreshes both version snapshot and cache
        cursor.Seek(1);
        Assert.False(cursor.IsStale);

        // Payload is accessible (cache was refreshed by seek's leaf navigation)
        Assert.Equal(1, cursor.RowId);
        _ = cursor.Payload; // Should not throw
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Creates a 3-page database with page 1 as a table leaf with one row.
    /// </summary>
    private static byte[] CreateDatabaseWithOneRow()
    {
        var data = new byte[PageSize * 3];
        WriteHeader(data, PageSize, 3);

        // Page 1 is a table-leaf b-tree (type 0x0D) at offset 100 (after db header)
        int pageBase = 0;
        int headerOffset = SQLiteLayout.DatabaseHeaderSize; // 100

        // B-tree page header: type=0x0D (leaf table), freeBlock=0, cellCount=1
        data[pageBase + headerOffset] = 0x0D;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 1), 0); // first free block
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 3), 1); // cell count = 1

        // Cell content area starts near the end of the page
        int cellStart = PageSize - 20;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 5), (ushort)cellStart);

        // Cell pointer array (1 pointer, starts right after 8-byte leaf header)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 8), (ushort)cellStart);

        // Write a simple cell: [payloadSize:varint] [rowid:varint] [payload...]
        int pos = cellStart;
        pos += VarintDecoder.Write(data.AsSpan(pos), 5);  // payload size = 5 bytes
        pos += VarintDecoder.Write(data.AsSpan(pos), 1);  // rowid = 1
        // payload: simple record [header_size=2, type=1(int8)] [value=42]
        data[pos++] = 2; // header size
        data[pos++] = 1; // serial type 1 (int8)
        data[pos++] = 42; // value

        return data;
    }

    /// <summary>
    /// Creates a 3-page database where page 2 is an index leaf.
    /// </summary>
    private static byte[] CreateDatabaseWithIndexPage()
    {
        var data = new byte[PageSize * 3];
        WriteHeader(data, PageSize, 3);

        // Page 2: index-leaf b-tree (type 0x0A)
        int pageBase = PageSize; // page 2
        int headerOffset = 0;

        data[pageBase + headerOffset] = 0x0A; // index leaf
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 1), 0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 3), 1); // cell count = 1

        int cellStart = PageSize - 20;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 5), (ushort)cellStart);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageBase + headerOffset + 8), (ushort)cellStart);

        // Write index leaf cell: [payloadSize:varint] [payload...]
        int pos = pageBase + cellStart;
        int payloadSize = 5;
        pos += VarintDecoder.Write(data.AsSpan(pos), payloadSize);
        // payload: record [header=2, type=1(int8)] [value=10] + trailing rowid byte
        data[pos++] = 3; // header size
        data[pos++] = 1; // serial type 1 (int8) for indexed column
        data[pos++] = 1; // serial type 1 (int8) for rowid
        data[pos++] = 10; // indexed value
        data[pos++] = 1;  // rowid = 1

        return data;
    }

    private static void WriteHeader(byte[] data, int pageSize, int pageCount)
    {
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[21] = 64; data[22] = 32; data[23] = 32;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), pageCount);
        data[47] = 4; // schema format
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(56), 1); // schema cookie
    }

    /// <summary>
    /// IPageSource that does NOT override DataVersion — uses default (0).
    /// </summary>
    private sealed class ReadOnlyPageSourceStub : IPageSource
    {
        private readonly IPageSource _inner;
        public ReadOnlyPageSourceStub(IPageSource inner) => _inner = inner;
        public int PageSize => _inner.PageSize;
        public int PageCount => _inner.PageCount;
        public int ReadPage(uint pageNumber, Span<byte> destination) => _inner.ReadPage(pageNumber, destination);
        public ReadOnlySpan<byte> GetPage(uint pageNumber) => _inner.GetPage(pageNumber);
        public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber) => _inner.GetPageMemory(pageNumber);
        public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);
        public void Dispose() => _inner.Dispose();
        // Deliberately does NOT override DataVersion — uses default (0)
    }

    #endregion
}
