using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Sharc.Tests.Write;

public sealed class BTreeMutatorHeaderTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    [Fact]
    public void RootSplit_OnPage1_PreservesDatabaseHeader()
    {
        // Setup: Page 1 with a full database header and some sqlite_master records
        var data = new byte[PageSize * 10];
        
        // 1. Write a unique pattern to the first 100 bytes
        var originalHeader = new byte[100];
        for (int i = 0; i < 100; i++) originalHeader[i] = (byte)(i + 1);
        "SQLite format 3\0"u8.CopyTo(originalHeader);
        BinaryPrimitives.WriteUInt16BigEndian(originalHeader.AsSpan(16), PageSize);
        originalHeader.CopyTo(data);

        // 2. Initialize Page 1 as a Leaf B-Tree page (offset 100)
        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(SQLiteLayout.DatabaseHeaderSize), masterHdr);

        var source = new MemoryPageSource(data);
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // 3. Insert enough records into sqlite_master (Page 1) to force a root split.
        // Each record is ~200 bytes, we need ~20 to fill 4096-100.
        uint root = 1;
        for (int i = 1; i <= 60; i++)
        {
            string sql = $"CREATE TABLE test_{i}(id INTEGER, data TEXT, padding_{i} TEXT)";
            var cols = new ColumnValue[]
            {
                ColumnValue.Text(0, Encoding.UTF8.GetBytes("table")),
                ColumnValue.Text(0, Encoding.UTF8.GetBytes($"test_{i}")),
                ColumnValue.Text(0, Encoding.UTF8.GetBytes($"test_{i}")),
                ColumnValue.FromInt64(0, i + 1),
                ColumnValue.Text(0, Encoding.UTF8.GetBytes(sql))
            };
            int recSize = RecordEncoder.ComputeEncodedSize(cols);
            var recBuf = new byte[recSize];
            RecordEncoder.EncodeRecord(cols, recBuf);
            root = mutator.Insert(root, i, recBuf);
        }

        // 4. Verify root is still Page 1 (root doesn't move in SQLite/Sharc, it becomes interior)
        Assert.Equal(1u, root);

        // 5. Verify Page 1 is now an Interior page
        byte[] page1 = new byte[PageSize];
        shadow.ReadPage(1, page1);
        var header = BTreePageHeader.Parse(page1.AsSpan(SQLiteLayout.DatabaseHeaderSize));
        Assert.Equal(BTreePageType.InteriorTable, header.PageType);

        // 6. CRITICAL: Verify the 100-byte database header is UNTOUCHED
        Assert.True(page1.AsSpan(0, 100).SequenceEqual(originalHeader), "Database header was corrupted during root split!");
    }

    [Fact]
    public void DefragmentPage_OnPage1_PreservesDatabaseHeader()
    {
        var data = new byte[PageSize * 2];
        var originalHeader = new byte[100];
        for (int i = 0; i < 100; i++) originalHeader[i] = (byte)(i + 50);
        "SQLite format 3\0"u8.CopyTo(originalHeader);
        BinaryPrimitives.WriteUInt16BigEndian(originalHeader.AsSpan(16), PageSize);
        originalHeader.CopyTo(data);

        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(SQLiteLayout.DatabaseHeaderSize), masterHdr);

        var source = new MemoryPageSource(data);
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // 1. Insert and delete to create fragmentation
        uint root = 1;
        for (int i = 1; i <= 5; i++)
        {
            var rec = new byte[500]; // Large records
            root = mutator.Insert(root, i, rec);
        }
        mutator.Delete(root, 2);
        mutator.Delete(root, 4);

        // 2. Insert another large record to force defragmentation (but not split)
        root = mutator.Insert(root, 6, new byte[800]);

        // 3. Verify header
        byte[] page1 = new byte[PageSize];
        shadow.ReadPage(1, page1);
        Assert.True(page1.AsSpan(0, 100).SequenceEqual(originalHeader), "Database header was corrupted during defragmentation!");
    }
}
