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
/// Tests for <see cref="BTreeMutator"/> — insert + page split on raw page sources.
/// </summary>
public sealed class BTreeMutatorTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    /// <summary>
    /// Creates a minimal in-memory SQLite database with a single empty table
    /// rooted at page 2. Page 1 = database header + sqlite_master.
    /// </summary>
    private static MemoryPageSource CreateDatabaseWithEmptyTable()
    {
        // We need 2 pages: page 1 (sqlite_master) and page 2 (empty table leaf)
        var data = new byte[PageSize * 2];

        // ── Page 1: database header (100 bytes) + sqlite_master B-tree ──
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);            // page size
        data[18] = 1; // file format write version
        data[19] = 1; // file format read version
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2);                   // page count = 2
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);                   // schema cookie = 1
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);                   // schema format = 4
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(96), 0);                   // version-valid-for

        // sqlite_master is a leaf table page at offset 100 in page 1
        // We write one row: (type='table', name='test', tbl_name='test', rootpage=2, sql='CREATE TABLE test(id INTEGER, name TEXT)')
        string sql = "CREATE TABLE test(id INTEGER, name TEXT)";
        var sqlBytes = Encoding.UTF8.GetBytes(sql);

        // Build the sqlite_master record
        var cols = new ColumnValue[5];
        cols[0] = ColumnValue.Text(2 * 5 + 13, Encoding.UTF8.GetBytes("table"));          // type
        cols[1] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));            // name
        cols[2] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));            // tbl_name
        cols[3] = ColumnValue.FromInt64(1, 2);                                              // rootpage = 2
        cols[4] = ColumnValue.Text(2 * sqlBytes.Length + 13, sqlBytes);                     // sql

        int recordSize = RecordEncoder.ComputeEncodedSize(cols);
        Span<byte> recordBuf = stackalloc byte[recordSize];
        RecordEncoder.EncodeRecord(cols, recordBuf);

        // Build leaf cell: payload-size varint + rowid varint + payload
        int cellSize = CellBuilder.ComputeTableLeafCellSize(1, recordSize, UsableSize);
        Span<byte> cellBuf = stackalloc byte[cellSize];
        CellBuilder.BuildTableLeafCell(1, recordBuf, cellBuf, UsableSize);

        // Write the sqlite_master page header at offset 100
        int pageHdrOff = 100;
        ushort cellContentOff = (ushort)(PageSize - cellSize);
        var masterHdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0, 1, cellContentOff, 0, 0
        );
        BTreePageHeader.Write(data.AsSpan(pageHdrOff), masterHdr);

        // Cell pointer at offset 108 (100 + 8 = header end)
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageHdrOff + 8), cellContentOff);

        // Cell content
        cellBuf.CopyTo(data.AsSpan(cellContentOff));

        // ── Page 2: empty leaf table page ──
        int page2Off = PageSize;
        var tableHdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0
        );
        BTreePageHeader.Write(data.AsSpan(page2Off), tableHdr);

        return new MemoryPageSource(data);
    }

    [Fact]
    public void Insert_SingleRow_ReadableByBTreeCursor()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // Insert a row: id=42, name='hello'
        var cols = new ColumnValue[]
        {
            ColumnValue.FromInt64(1, 42),
            ColumnValue.Text(2 * 5 + 13, Encoding.UTF8.GetBytes("hello")),
        };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        Span<byte> recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);

        uint newRoot = mutator.Insert(2, 1, recBuf);
        Assert.Equal(2u, newRoot); // No split needed for single row

        // Read back using BTreeCursor
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, newRoot, UsableSize);
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);

        // Decode record
        var decoded = new ColumnValue[2];
        new RecordDecoder().DecodeRecord(cursor.Payload, decoded);
        Assert.Equal(42L, decoded[0].AsInt64());
        Assert.Equal("hello", decoded[1].AsString());

        // No more rows
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Insert_TenRows_AllReadableInOrder()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 10; i++)
        {
            var cols = new ColumnValue[]
            {
                ColumnValue.FromInt64(1, i * 10),
                ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes($"row{i}")),
            };
            int recSize = RecordEncoder.ComputeEncodedSize(cols);
            var recBuf = new byte[recSize];
            RecordEncoder.EncodeRecord(cols, recBuf);
            root = mutator.Insert(root, i, recBuf);
        }

        // Read back
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        var decoded = new ColumnValue[2];
        var decoder = new RecordDecoder();

        for (int i = 1; i <= 10; i++)
        {
            Assert.True(cursor.MoveNext());
            Assert.Equal((long)i, cursor.RowId);
            decoder.DecodeRecord(cursor.Payload, decoded);
            Assert.Equal((long)(i * 10), decoded[0].AsInt64());
        }
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Insert_EnoughToTriggerSplit_AllReadable()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // Insert enough rows to trigger at least one page split.
        // Each row is ~50 bytes, page usable is ~4088 bytes → ~80 rows per page.
        // Insert 200 rows to guarantee at least 2 splits.
        int rowCount = 200;
        uint root = 2;

        for (int i = 1; i <= rowCount; i++)
        {
            string name = $"user_{i:D5}"; // consistent size
            var cols = new ColumnValue[]
            {
                ColumnValue.FromInt64(1, i),
                ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
            };
            int recSize = RecordEncoder.ComputeEncodedSize(cols);
            var recBuf = new byte[recSize];
            RecordEncoder.EncodeRecord(cols, recBuf);
            root = mutator.Insert(root, i, recBuf);
        }

        // Read back all rows in order
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        var decoded = new ColumnValue[2];
        var decoder = new RecordDecoder();

        for (int i = 1; i <= rowCount; i++)
        {
            Assert.True(cursor.MoveNext(), $"Expected row {i} but cursor exhausted");
            Assert.Equal((long)i, cursor.RowId);
            decoder.DecodeRecord(cursor.Payload, decoded);
            Assert.Equal((long)i, decoded[0].AsInt64());
        }
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void GetMaxRowId_EmptyTree_ReturnsZero()
    {
        var source = CreateDatabaseWithEmptyTable();
        using var mutator = new BTreeMutator(source, UsableSize);
        Assert.Equal(0L, mutator.GetMaxRowId(2));
    }

    [Fact]
    public void GetMaxRowId_AfterInserts_ReturnsHighest()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 50; i++)
        {
            var cols = new ColumnValue[] { ColumnValue.FromInt64(1, i) };
            int recSize = RecordEncoder.ComputeEncodedSize(cols);
            var recBuf = new byte[recSize];
            RecordEncoder.EncodeRecord(cols, recBuf);
            root = mutator.Insert(root, i, recBuf);
        }

        Assert.Equal(50L, mutator.GetMaxRowId(root));
    }
}
