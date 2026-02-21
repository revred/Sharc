// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Tests for <see cref="IPageSource.GetPageMemory"/>, verifying the default interface
/// method and optimized overrides in concrete page source implementations.
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
        IPageSource iface = source;

        var span = source.GetPage(1);
        var memory = iface.GetPageMemory(1);

        Assert.Equal(span.Length, memory.Length);
        Assert.Equal(0xAB, memory.Span[100]);

        var span2 = source.GetPage(2);
        var memory2 = iface.GetPageMemory(2);
        Assert.Equal(0xCD, memory2.Span[0]);
    }

    [Fact]
    public void MemoryPageSource_GetPageMemory_IsZeroCopy()
    {
        var data = CreateMinimalDatabase();
        data[200] = 0xEF;

        using var source = new MemoryPageSource(data);
        IPageSource iface = source;

        var memory = iface.GetPageMemory(1);

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
        IPageSource iface = cached;

        var span = cached.GetPage(1);
        var memory = iface.GetPageMemory(1);

        Assert.Equal(span.Length, memory.Length);
        Assert.Equal(0xAB, memory.Span[100]);
    }

    [Fact]
    public void GetPageMemory_DefaultImplementation_ReturnsCorrectData()
    {
        // Uses a minimal IPageSource wrapper that doesn't override GetPageMemory,
        // exercising the default interface method
        var data = CreateMinimalDatabase();
        data[300] = 0x99;

        using var inner = new MemoryPageSource(data);
        IPageSource proxy = new NonOverridingPageSource(inner);

        var memory = proxy.GetPageMemory(1);

        Assert.Equal(4096, memory.Length);
        Assert.Equal(0x99, memory.Span[300]);
    }

    /// <summary>
    /// Minimal IPageSource that does NOT override GetPageMemory,
    /// exercising the default interface method (which calls GetPage().ToArray()).
    /// </summary>
    private sealed class NonOverridingPageSource : IPageSource
    {
        private readonly IPageSource _inner;
        public NonOverridingPageSource(IPageSource inner) => _inner = inner;
        public int PageSize => _inner.PageSize;
        public int PageCount => _inner.PageCount;
        public int ReadPage(uint pageNumber, Span<byte> destination) => _inner.ReadPage(pageNumber, destination);
        public ReadOnlySpan<byte> GetPage(uint pageNumber) => _inner.GetPage(pageNumber);
        public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);
        public void Dispose() => _inner.Dispose();
        // Deliberately does NOT override GetPageMemory — uses default
    }
}
