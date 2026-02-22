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
/// Tests for BTreeMutator ArrayPool page caching and IDisposable lifecycle.
/// Validates that page buffers are pooled, cached, and properly returned.
/// </summary>
public sealed class BTreeMutatorPoolingTests
{
    private const int PageSize = 4096;
    private const int UsableSize = PageSize;

    private static MemoryPageSource CreateDatabaseWithEmptyTable()
    {
        var data = new byte[PageSize * 2];

        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1;
        data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 4);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(96), 0);

        string sql = "CREATE TABLE test(id INTEGER, name TEXT)";
        var sqlBytes = Encoding.UTF8.GetBytes(sql);

        var cols = new ColumnValue[5];
        cols[0] = ColumnValue.Text(2 * 5 + 13, Encoding.UTF8.GetBytes("table"));
        cols[1] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));
        cols[2] = ColumnValue.Text(2 * 4 + 13, Encoding.UTF8.GetBytes("test"));
        cols[3] = ColumnValue.FromInt64(1, 2);
        cols[4] = ColumnValue.Text(2 * sqlBytes.Length + 13, sqlBytes);

        int recordSize = RecordEncoder.ComputeEncodedSize(cols);
        Span<byte> recordBuf = stackalloc byte[recordSize];
        RecordEncoder.EncodeRecord(cols, recordBuf);

        int cellSize = CellBuilder.ComputeTableLeafCellSize(1, recordSize, UsableSize);
        Span<byte> cellBuf = stackalloc byte[cellSize];
        CellBuilder.BuildTableLeafCell(1, recordBuf, cellBuf, UsableSize);

        int pageHdrOff = 100;
        ushort cellContentOff = (ushort)(PageSize - cellSize);
        var masterHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 1, cellContentOff, 0, 0);
        BTreePageHeader.Write(data.AsSpan(pageHdrOff), masterHdr);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pageHdrOff + 8), cellContentOff);
        cellBuf.CopyTo(data.AsSpan(cellContentOff));

        int page2Off = PageSize;
        var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)UsableSize, 0, 0);
        BTreePageHeader.Write(data.AsSpan(page2Off), tableHdr);

        return new MemoryPageSource(data);
    }

    private static (byte[] record, int size) BuildSimpleRecord(int value)
    {
        var cols = new ColumnValue[] { ColumnValue.FromInt64(1, value) };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        var recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);
        return (recBuf, recSize);
    }

    private static (byte[] record, int size) BuildNamedRecord(int value, string name)
    {
        var cols = new ColumnValue[]
        {
            ColumnValue.FromInt64(1, value),
            ColumnValue.Text(2 * name.Length + 13, Encoding.UTF8.GetBytes(name)),
        };
        int recSize = RecordEncoder.ComputeEncodedSize(cols);
        var recBuf = new byte[recSize];
        RecordEncoder.EncodeRecord(cols, recBuf);
        return (recBuf, recSize);
    }

    [Fact]
    public void Dispose_ReturnsRentedBuffers_NoLeaks()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        var (rec, _) = BuildSimpleRecord(42);
        mutator.Insert(2, 1, rec);

        // Dispose should return all pooled buffers without error
        mutator.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_Idempotent()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);

        var (rec, _) = BuildSimpleRecord(42);
        mutator.Insert(2, 1, rec);

        mutator.Dispose();
        mutator.Dispose(); // Should not throw
    }

    [Fact]
    public void Insert_SingleRow_CachedPageCountIncreases()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        Assert.Equal(0, mutator.CachedPageCount);

        var (rec, _) = BuildSimpleRecord(42);
        mutator.Insert(2, 1, rec);

        // At least page 2 (the table root) was cached
        Assert.True(mutator.CachedPageCount >= 1);
    }

    [Fact]
    public void Insert_TwoRows_SamePage_CacheCountStaysOne()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        var (rec1, _) = BuildSimpleRecord(10);
        var (rec2, _) = BuildSimpleRecord(20);

        mutator.Insert(2, 1, rec1);
        int countAfterFirst = mutator.CachedPageCount;

        mutator.Insert(2, 2, rec2);
        // Second insert to same page should not increase cache count
        Assert.Equal(countAfterFirst, mutator.CachedPageCount);
    }

    [Fact]
    public void Insert_EnoughToSplit_AllocatedPagesInCache()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        // Use names large enough (~90 chars each → ~100-byte cells) to force page splits
        for (int i = 1; i <= 200; i++)
        {
            var (rec, _) = BuildNamedRecord(i, $"user_{i:D5}_" + new string('x', 80));
            root = mutator.Insert(root, i, rec);
        }

        // After splits, multiple pages should be cached
        Assert.True(mutator.CachedPageCount > 1,
            $"Expected CachedPageCount > 1, but was {mutator.CachedPageCount}");

        // All rows still readable
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(200, count);
    }

    [Fact]
    public void Insert_AfterDispose_ThrowsObjectDisposedException()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        var mutator = new BTreeMutator(shadow, UsableSize);
        mutator.Dispose();

        var (rec, _) = BuildSimpleRecord(42);
        Assert.Throws<ObjectDisposedException>(() => mutator.Insert(2, 1, rec));
    }

    [Fact]
    public void GetMaxRowId_AfterInserts_CorrectWithCache()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 50; i++)
        {
            var (rec, _) = BuildSimpleRecord(i);
            root = mutator.Insert(root, i, rec);
        }

        Assert.Equal(50L, mutator.GetMaxRowId(root));
    }

    [Fact]
    public void Delete_WithPageCache_CorrectBehavior()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 10; i++)
        {
            var (rec, _) = BuildSimpleRecord(i);
            root = mutator.Insert(root, i, rec);
        }

        // Delete row 5
        var (found, newRoot) = mutator.Delete(root, 5);
        Assert.True(found);

        // Verify remaining 9 rows
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, newRoot, UsableSize);
        int count = 0;
        while (cursor.MoveNext())
        {
            Assert.NotEqual(5L, cursor.RowId);
            count++;
        }
        Assert.Equal(9, count);
    }

    [Fact]
    public void Update_WithPageCache_CorrectBehavior()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        uint root = 2;
        for (int i = 1; i <= 5; i++)
        {
            var (rec, _) = BuildSimpleRecord(i * 100);
            root = mutator.Insert(root, i, rec);
        }

        // Update row 3 with new value
        var (newRec, _) = BuildSimpleRecord(999);
        var (found, updatedRoot) = mutator.Update(root, 3, newRec);
        Assert.True(found);

        // Verify row 3 has updated value
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, updatedRoot, UsableSize);
        var decoded = new ColumnValue[1];
        var decoder = new RecordDecoder();
        while (cursor.MoveNext())
        {
            if (cursor.RowId == 3)
            {
                decoder.DecodeRecord(cursor.Payload, decoded);
                Assert.Equal(999L, decoded[0].AsInt64());
            }
        }
    }

    [Fact]
    public void Reset_ReturnsRentedBuffers_ClearsCache()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        var (rec, _) = BuildSimpleRecord(42);
        mutator.Insert(2, 1, rec);
        Assert.True(mutator.CachedPageCount >= 1);

        mutator.Reset();
        Assert.Equal(0, mutator.CachedPageCount);
    }

    [Fact]
    public void Insert_AfterReset_WorksCorrectly()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        using var mutator = new BTreeMutator(shadow, UsableSize);

        // First cycle
        var (rec1, _) = BuildSimpleRecord(42);
        mutator.Insert(2, 1, rec1);
        mutator.Reset();

        // Second cycle — mutator should work normally
        shadow.Reset();
        var source2 = CreateDatabaseWithEmptyTable();
        var shadow2 = new ShadowPageSource(source2);
        using var mutator2 = new BTreeMutator(shadow2, UsableSize);
        var (rec2, _) = BuildSimpleRecord(99);
        mutator2.Insert(2, 1, rec2);

        // Verify the row is readable
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow2, 2, UsableSize);
        Assert.True(cursor.MoveNext());
        Assert.Equal(1L, cursor.RowId);
    }

    [Fact]
    public void Insert_ManyRows_AllReadableAfterDispose()
    {
        var source = CreateDatabaseWithEmptyTable();
        var shadow = new ShadowPageSource(source);
        uint root;

        // Insert rows, then dispose mutator — data should persist in ShadowPageSource
        using (var mutator = new BTreeMutator(shadow, UsableSize))
        {
            root = 2;
            for (int i = 1; i <= 100; i++)
            {
                var (rec, _) = BuildNamedRecord(i, $"row_{i:D3}");
                root = mutator.Insert(root, i, rec);
            }
        } // mutator disposed here, pooled buffers returned

        // Data should still be readable from ShadowPageSource
        using var cursor = new BTreeCursor<ShadowPageSource>(shadow, root, UsableSize);
        int count = 0;
        while (cursor.MoveNext()) count++;
        Assert.Equal(100, count);
    }
}
