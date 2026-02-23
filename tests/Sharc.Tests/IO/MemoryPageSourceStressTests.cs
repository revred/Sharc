// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Stress tests for <see cref="MemoryPageSource"/> — verifies page growth,
/// write propagation, DataVersion tracking, and boundary conditions.
/// </summary>
public sealed class MemoryPageSourceStressTests
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
    public void WritePage_GrowsBeyondInitialSize()
    {
        var data = CreateValidHeader(2);
        var source = new MemoryPageSource(data);
        Assert.Equal(2, source.PageCount);

        // Write to page 5 — should grow
        var pageData = new byte[PageSize];
        pageData[0] = 0xAB;
        source.WritePage(5, pageData);

        Assert.Equal(5, source.PageCount);
        Assert.Equal(0xAB, source.GetPage(5)[0]);
    }

    [Fact]
    public void WritePage_DataVersion_IncrementsOnEveryWrite()
    {
        var data = CreateValidHeader(2);
        var source = new MemoryPageSource(data);
        long initial = source.DataVersion;

        var pageData = new byte[PageSize];
        source.WritePage(1, pageData);
        Assert.Equal(initial + 1, source.DataVersion);

        source.WritePage(2, pageData);
        Assert.Equal(initial + 2, source.DataVersion);
    }

    [Fact]
    public void WriteThenRead_DataPersistsInBuffer()
    {
        var data = CreateValidHeader(3);
        var source = new MemoryPageSource(data);

        // Write a known pattern to page 2
        var pattern = new byte[PageSize];
        for (int i = 0; i < PageSize; i++) pattern[i] = (byte)(i & 0xFF);
        source.WritePage(2, pattern);

        // Read it back
        var readBack = source.GetPage(2);
        for (int i = 0; i < PageSize; i++)
        {
            Assert.Equal((byte)(i & 0xFF), readBack[i]);
        }
    }

    [Fact]
    public void MultipleGrows_PagesStillAccessible()
    {
        var data = CreateValidHeader(1);
        var source = new MemoryPageSource(data);

        var pageData = new byte[PageSize];
        // Grow incrementally: 1 → 3 → 5 → 10
        for (uint p = 2; p <= 10; p++)
        {
            pageData[0] = (byte)p;
            source.WritePage(p, pageData);
        }

        Assert.Equal(10, source.PageCount);

        // Verify each page has the correct marker
        for (uint p = 2; p <= 10; p++)
        {
            Assert.Equal((byte)p, source.GetPage(p)[0]);
        }
    }

    [Fact]
    public void ReadPage_CopiesIntoDestination()
    {
        var data = CreateValidHeader(2);
        data[PageSize] = 0x42; // First byte of page 2
        var source = new MemoryPageSource(data);

        var dest = new byte[PageSize];
        int bytesRead = source.ReadPage(2, dest);

        Assert.Equal(PageSize, bytesRead);
        Assert.Equal(0x42, dest[0]);
    }

    [Fact]
    public void GetPage_ReturnsExactPageSize()
    {
        var data = CreateValidHeader(3);
        var source = new MemoryPageSource(data);

        var page = source.GetPage(1);
        Assert.Equal(PageSize, page.Length);

        page = source.GetPage(2);
        Assert.Equal(PageSize, page.Length);
    }

    [Fact]
    public void WritePage_PageOne_UpdatesHeader()
    {
        var data = CreateValidHeader(2);
        var source = new MemoryPageSource(data);

        // Write modified header page
        var newPage1 = source.GetPage(1).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(newPage1.AsSpan(44), 99); // schema cookie
        source.WritePage(1, newPage1);

        // Verify write persisted
        var readBack = source.GetPage(1);
        Assert.Equal(99u, BinaryPrimitives.ReadUInt32BigEndian(readBack.Slice(44, 4)));
    }

    [Fact]
    public void Invalidate_ResetsCache()
    {
        var data = CreateValidHeader(2);
        var source = new MemoryPageSource(data);

        // Invalidate should not throw
        source.Invalidate(1);
        source.Invalidate(2);

        // Pages still readable after invalidation
        var page = source.GetPage(1);
        Assert.Equal(PageSize, page.Length);
    }
}
