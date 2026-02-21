// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Tests for <see cref="IPageSource.GetPageMemory"/>, verifying zero-copy behavior
/// and correctness in concrete page source implementations.
/// </summary>
public class GetPageMemoryTests
{
    private static byte[] CreateMinimalDatabase(int pageSize = 4096, int pageCount = 3)
    {
        var data = new byte[pageSize * pageCount];
        "SQLite format 3\0"u8.CopyTo(data);
        data[16] = (byte)(pageSize >> 8);
        data[17] = (byte)(pageSize & 0xFF);
        data[18] = 1; data[19] = 1;
        data[21] = 64; data[22] = 32; data[23] = 32;
        data[28] = (byte)(pageCount >> 24);
        data[29] = (byte)(pageCount >> 16);
        data[30] = (byte)(pageCount >> 8);
        data[31] = (byte)(pageCount & 0xFF);
        data[47] = 4;
        data[56] = 0; data[57] = 0; data[58] = 0; data[59] = 1;
        return data;
    }

    [Fact]
    public void MemoryPageSource_GetPageMemory_MatchesGetPage()
    {
        var data = CreateMinimalDatabase();
        data[100] = 0xAB;
        data[4096] = 0xCD; // page 2

        using var source = new MemoryPageSource(data);

        var span = source.GetPage(1);
        var memory = source.GetPageMemory(1);

        Assert.Equal(span.Length, memory.Length);
        Assert.Equal(0xAB, memory.Span[100]);

        var span2 = source.GetPage(2);
        var memory2 = source.GetPageMemory(2);
        Assert.Equal(0xCD, memory2.Span[0]);
    }

    [Fact]
    public void MemoryPageSource_GetPageMemory_IsZeroCopy()
    {
        var data = CreateMinimalDatabase();
        data[200] = 0xEF;

        using var source = new MemoryPageSource(data);

        var memory = source.GetPageMemory(1);

        // Mutate the backing array — the memory should reflect the change
        // (proving it's zero-copy, not a snapshot)
        data[200] = 0x42;
        Assert.Equal(0x42, memory.Span[200]);
    }

    [Fact]
    public void CachedPageSource_GetPageMemory_MatchesGetPage()
    {
        var data = CreateMinimalDatabase();
        data[100] = 0xAB;

        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, 10);

        var span = cached.GetPage(1);
        var memory = cached.GetPageMemory(1);

        Assert.Equal(span.Length, memory.Length);
        Assert.Equal(0xAB, memory.Span[100]);
    }

    [Fact]
    public void CachedPageSource_GetPageMemory_IsZeroCopy()
    {
        var data = CreateMinimalDatabase();
        data[100] = 0xAB;

        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, 10);

        // First call populates cache
        var memory1 = cached.GetPageMemory(1);
        Assert.Equal(0xAB, memory1.Span[100]);

        // Second call should return the same cached buffer (zero-copy)
        var memory2 = cached.GetPageMemory(1);
        Assert.True(memory1.Span == memory2.Span);
    }

    [Fact]
    public void CachedPageSource_GetPageMemory_AfterGetPage_IsZeroCopy()
    {
        // Verifies that GetPageMemory returns the same cached buffer after GetPage
        var data = CreateMinimalDatabase();
        data[100] = 0xAB;

        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, 10);

        // Populate cache via GetPage
        _ = cached.GetPage(1);

        // GetPageMemory should return the same underlying buffer (zero-copy from cache)
        var memory1 = cached.GetPageMemory(1);
        var memory2 = cached.GetPageMemory(1);

        Assert.True(memory1.Span == memory2.Span);
        Assert.Equal(0xAB, memory1.Span[100]);
    }

}
