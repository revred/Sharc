// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Stress tests for <see cref="ShadowPageSource"/> — verifies dirty page tracking,
/// shadow reads, DataVersion monotonicity, ClearShadow/Reset behavior, and
/// WriteDirtyPagesTo fidelity under heavy write loads.
/// </summary>
public sealed class ShadowPageSourceStressTests
{
    private const int PageSize = 4096;

    private static byte[] CreateValidHeader(int pageCount)
    {
        var data = new byte[PageSize * pageCount];
        "SQLite format 3\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), PageSize);
        data[18] = 1; data[19] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), (uint)pageCount);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 1);
        return data;
    }

    [Fact]
    public void WritePage_ShadowedRead_ReturnsDirtyData()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var pageData = new byte[PageSize];
        pageData[0] = 0xAB;
        pageData[PageSize - 1] = 0xCD;
        shadow.WritePage(2, pageData);

        var read = shadow.GetPage(2);
        Assert.Equal(0xAB, read[0]);
        Assert.Equal(0xCD, read[PageSize - 1]);
    }

    [Fact]
    public void UnshadowedRead_FallsThroughToBase()
    {
        var data = CreateValidHeader(2);
        data[PageSize + 10] = 0x42; // page 2, offset 10
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        // Write to page 1 only — page 2 should come from base
        shadow.WritePage(1, new byte[PageSize]);

        var page2 = shadow.GetPage(2);
        Assert.Equal(0x42, page2[10]);
    }

    [Fact]
    public void DataVersion_IncrementsOnEveryWrite()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        long v0 = shadow.DataVersion;
        shadow.WritePage(1, new byte[PageSize]);
        long v1 = shadow.DataVersion;
        shadow.WritePage(2, new byte[PageSize]);
        long v2 = shadow.DataVersion;

        Assert.True(v1 > v0);
        Assert.True(v2 > v1);
    }

    [Fact]
    public void OverwriteSamePage_KeepsLatestData()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var first = new byte[PageSize];
        first[0] = 0x11;
        shadow.WritePage(2, first);

        var second = new byte[PageSize];
        second[0] = 0x22;
        shadow.WritePage(2, second);

        Assert.Equal(0x22, shadow.GetPage(2)[0]);
    }

    [Fact]
    public void DirtyPageCount_TracksDistinctPages()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        Assert.Equal(0, shadow.DirtyPageCount);

        shadow.WritePage(1, new byte[PageSize]);
        Assert.Equal(1, shadow.DirtyPageCount);

        shadow.WritePage(2, new byte[PageSize]);
        Assert.Equal(2, shadow.DirtyPageCount);

        // Overwrite page 1 — count stays at 2
        shadow.WritePage(1, new byte[PageSize]);
        Assert.Equal(2, shadow.DirtyPageCount);
    }

    [Fact]
    public void ClearShadow_DiscardsAllDirtyPages()
    {
        var data = CreateValidHeader(2);
        data[PageSize + 5] = 0xFF; // original page 2 data
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var dirty = new byte[PageSize];
        dirty[5] = 0x00;
        shadow.WritePage(2, dirty);
        Assert.Equal(0x00, shadow.GetPage(2)[5]);

        shadow.ClearShadow();

        // After clear, reads should fall through to base
        Assert.Equal(0xFF, shadow.GetPage(2)[5]);
        Assert.Equal(0, shadow.DirtyPageCount);
    }

    [Fact]
    public void Reset_AllowsReuse()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        var shadow = new ShadowPageSource(baseSource);

        shadow.WritePage(1, new byte[PageSize]);
        shadow.Dispose();

        // Reset re-enables the object
        shadow.Reset();
        shadow.WritePage(2, new byte[PageSize]);
        Assert.Equal(1, shadow.DirtyPageCount);

        shadow.Dispose();
    }

    [Fact]
    public void PageCount_ReflectsMaxDirtyPage()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        Assert.Equal(2, shadow.PageCount);

        // Writing beyond base range should grow PageCount
        shadow.WritePage(5, new byte[PageSize]);
        Assert.Equal(5, shadow.PageCount);

        // Writing within range shouldn't shrink
        shadow.WritePage(3, new byte[PageSize]);
        Assert.Equal(5, shadow.PageCount);
    }

    [Fact]
    public void WriteDirtyPagesTo_CopiesAllDirtyPages()
    {
        var data = CreateValidHeader(3);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        // Write distinct patterns to pages 1, 2, 3
        for (uint p = 1; p <= 3; p++)
        {
            var page = new byte[PageSize];
            page[0] = (byte)(p * 10);
            page[PageSize - 1] = (byte)(p * 20);
            shadow.WritePage(p, page);
        }

        // Flush to a separate target
        var targetData = CreateValidHeader(3);
        var target = new MemoryPageSource(targetData);
        shadow.WriteDirtyPagesTo(target);

        for (uint p = 1; p <= 3; p++)
        {
            Assert.Equal((byte)(p * 10), target.GetPage(p)[0]);
            Assert.Equal((byte)(p * 20), target.GetPage(p)[PageSize - 1]);
        }
    }

    [Fact]
    public void HeavyWrite_50Pages_AllAccessible()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        for (uint p = 1; p <= 50; p++)
        {
            var page = new byte[PageSize];
            page[0] = (byte)(p & 0xFF);
            BinaryPrimitives.WriteUInt32BigEndian(page.AsSpan(4), p);
            shadow.WritePage(p, page);
        }

        Assert.Equal(50, shadow.DirtyPageCount);
        Assert.Equal(50, shadow.PageCount);

        for (uint p = 1; p <= 50; p++)
        {
            var read = shadow.GetPage(p);
            Assert.Equal((byte)(p & 0xFF), read[0]);
            Assert.Equal(p, BinaryPrimitives.ReadUInt32BigEndian(read.Slice(4, 4)));
        }
    }

    [Fact]
    public void ReadPage_CopiesFromShadow()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var dirty = new byte[PageSize];
        dirty[100] = 0xBE;
        shadow.WritePage(2, dirty);

        var dest = new byte[PageSize];
        int bytesRead = shadow.ReadPage(2, dest);

        Assert.Equal(PageSize, bytesRead);
        Assert.Equal(0xBE, dest[100]);
    }

    [Fact]
    public void Invalidate_RemovesDirtyPage()
    {
        var data = CreateValidHeader(2);
        data[PageSize + 0] = 0x99; // base page 2 marker
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var dirty = new byte[PageSize];
        dirty[0] = 0x11;
        shadow.WritePage(2, dirty);
        Assert.Equal(0x11, shadow.GetPage(2)[0]);

        shadow.Invalidate(2);

        // After invalidation, should fall through to base (or throw)
        // MemoryPageSource invalidation is a no-op so base data returns
        var page = shadow.GetPage(2);
        Assert.Equal(0x99, page[0]);
    }

    [Fact]
    public void Dispose_ThenAnyOperation_Throws()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        var shadow = new ShadowPageSource(baseSource);
        shadow.Dispose();

        Assert.Throws<ObjectDisposedException>(() => shadow.GetPage(1));
        Assert.Throws<ObjectDisposedException>(() => shadow.WritePage(1, new byte[PageSize]));
        Assert.Throws<ObjectDisposedException>(() => shadow.ReadPage(1, new byte[PageSize]));
    }

    [Fact]
    public void GetPageMemory_ReturnsCopyForDirtyPages()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        var dirty = new byte[PageSize];
        dirty[0] = 0x77;
        shadow.WritePage(2, dirty);

        var memory = shadow.GetPageMemory(2);
        Assert.Equal(0x77, memory.Span[0]);
    }

    [Fact]
    public void DataVersion_ResetsOnClear()
    {
        var data = CreateValidHeader(2);
        var baseSource = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(baseSource);

        long v0 = shadow.DataVersion;
        shadow.WritePage(1, new byte[PageSize]);
        long v1 = shadow.DataVersion;
        Assert.True(v1 > v0);

        shadow.ClearShadow();
        long v2 = shadow.DataVersion;
        // After clear, shadow version resets to 0, so DataVersion = base version
        Assert.True(v2 <= v1);
    }
}
