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
        public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);
        public void Dispose() => _inner.Dispose();
        // Deliberately does NOT override DataVersion — uses default (0)
    }

    #endregion
}
