// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Xunit;

namespace Sharc.Tests.BTree;

/// <summary>
/// Stress tests for <see cref="BTreeCursor{TPageSource}"/> — tests traversal
/// correctness with large trees, seek operations, and edge cases.
/// </summary>
public sealed class BTreeCursorStressTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    /// <summary>
    /// Builds an in-memory B-tree with the specified row count.
    /// Returns (source, rootPage).
    /// </summary>
    private static (MemoryPageSource source, uint root) BuildTree(int rowCount, Func<int, string>? valueFactory = null)
    {
        valueFactory ??= i => $"val_{i:D6}";
        var data = new byte[PageSize * 2];
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1; data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);

        // Page 1 header: empty leaf table
        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)(UsableSize - 100), 0, 0);
        BTreePageHeader.Write(data.AsSpan(100), masterHdr);

        // Page 2: empty leaf table for "test"
        var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(PageSize), tableHdr);

        var source = new MemoryPageSource(data);
        using var mutator = new BTreeMutator(source, UsableSize);

        uint root = 2;
        for (int i = 1; i <= rowCount; i++)
        {
            var cols = new ColumnValue[]
            {
                ColumnValue.FromInt64(1, i),
                ColumnValue.Text(2 * Encoding.UTF8.GetByteCount(valueFactory(i)) + 13,
                    Encoding.UTF8.GetBytes(valueFactory(i))),
            };
            int size = RecordEncoder.ComputeEncodedSize(cols);
            var buf = new byte[size];
            RecordEncoder.EncodeRecord(cols, buf);
            root = mutator.Insert(root, i, buf);
        }

        return (source, root);
    }

    [Fact]
    public void FullScan_1000Rows_AllRowidsInOrder()
    {
        var (source, root) = BuildTree(1000);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        long expected = 1;
        while (cursor.MoveNext())
        {
            Assert.Equal(expected, cursor.RowId);
            expected++;
        }
        Assert.Equal(1001, expected);
    }

    [Fact]
    public void Seek_EveryRowid_AllFound()
    {
        var (source, root) = BuildTree(500);

        for (int i = 1; i <= 500; i++)
        {
            using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
            bool found = cursor.Seek(i);
            Assert.True(found, $"Seek({i}) should find the row");
            Assert.Equal(i, cursor.RowId);
        }
    }

    [Fact]
    public void Seek_NonExistentRowid_ReturnsFalse()
    {
        var (source, root) = BuildTree(100);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        bool found = cursor.Seek(999);
        Assert.False(found);
    }

    [Fact]
    public void Seek_ThenMoveNext_ContinuesFromSeekPosition()
    {
        var (source, root) = BuildTree(200);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        Assert.True(cursor.Seek(100));
        Assert.Equal(100, cursor.RowId);

        // MoveNext should continue from 101
        Assert.True(cursor.MoveNext());
        Assert.Equal(101, cursor.RowId);
    }

    [Fact]
    public void Reset_AfterFullScan_RestartsFromBeginning()
    {
        var (source, root) = BuildTree(50);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);

        // First pass
        int count1 = 0;
        while (cursor.MoveNext()) count1++;
        Assert.Equal(50, count1);

        // Reset and second pass
        cursor.Reset();
        int count2 = 0;
        while (cursor.MoveNext()) count2++;
        Assert.Equal(50, count2);
    }

    [Fact]
    public void PayloadDecode_AllRowsDecodable()
    {
        var (source, root) = BuildTree(300, i => $"test_value_{i:D6}");
        var decoder = new RecordDecoder();
        var colBuf = new ColumnValue[2];

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext())
        {
            decoder.DecodeRecord(cursor.Payload, colBuf);
            long id = colBuf[0].AsInt64();
            string val = colBuf[1].AsString();
            Assert.Equal(cursor.RowId, id);
            Assert.Equal($"test_value_{cursor.RowId:D6}", val);
            count++;
        }
        Assert.Equal(300, count);
    }

    [Fact]
    public void LargePayloads_OverflowAssembly_Correct()
    {
        // Values >~2KB will spill to overflow pages
        var (source, root) = BuildTree(10, i => new string((char)('A' + (i % 26)), 3000));
        var decoder = new RecordDecoder();
        var colBuf = new ColumnValue[2];

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext())
        {
            decoder.DecodeRecord(cursor.Payload, colBuf);
            string val = colBuf[1].AsString();
            Assert.Equal(3000, val.Length);
            count++;
        }
        Assert.Equal(10, count);
    }

    [Fact]
    public void EmptyTree_MoveNext_ReturnsFalse()
    {
        var (source, root) = BuildTree(0);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void SingleRow_SeekAndScan_BothWork()
    {
        var (source, root) = BuildTree(1);

        using var cursor = new BTreeCursor<MemoryPageSource>(source, root, UsableSize);
        Assert.True(cursor.Seek(1));
        Assert.Equal(1, cursor.RowId);
        Assert.False(cursor.MoveNext()); // No more rows after the single row
    }
}
