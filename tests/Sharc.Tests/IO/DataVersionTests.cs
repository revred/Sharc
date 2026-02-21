// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

/// <summary>
/// Tests for <see cref="IPageSource.DataVersion"/> — monotonic version counter
/// that tracks data mutations across page source implementations.
/// </summary>
public class DataVersionTests
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
    public void DataVersion_NewMemoryPageSource_ReturnsOne()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        Assert.Equal(1L, source.DataVersion);
    }

    [Fact]
    public void DataVersion_AfterWritePage_Increments()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);
        long before = source.DataVersion;

        source.WritePage(1, new byte[4096]);

        Assert.Equal(before + 1, source.DataVersion);
    }

    [Fact]
    public void DataVersion_MultipleWrites_IncrementsEachTime()
    {
        var data = CreateMinimalDatabase();
        using var source = new MemoryPageSource(data);

        source.WritePage(1, new byte[4096]);
        source.WritePage(2, new byte[4096]);
        source.WritePage(3, new byte[4096]);

        Assert.Equal(4L, source.DataVersion);
    }

    [Fact]
    public void DataVersion_ReadOnlySource_ReturnsZero()
    {
        // A source that doesn't override DataVersion should return the default (0)
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        IPageSource readOnly = new ReadOnlyPageSourceStub(inner);

        Assert.Equal(0L, readOnly.DataVersion);
    }

    [Fact]
    public void DataVersion_CachedPageSource_DelegatesToInner()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, 10);

        Assert.Equal(inner.DataVersion, cached.DataVersion);
    }

    [Fact]
    public void DataVersion_CachedPageSource_WriteIncreases()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var cached = new CachedPageSource(inner, 10);
        long before = cached.DataVersion;

        cached.WritePage(1, new byte[4096]);

        Assert.True(cached.DataVersion > before);
        Assert.Equal(inner.DataVersion, cached.DataVersion);
    }

    [Fact]
    public void DataVersion_ProxyPageSource_DelegatesToTarget()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        var proxy = new ProxyPageSource(inner);

        Assert.Equal(inner.DataVersion, proxy.DataVersion);
    }

    [Fact]
    public void DataVersion_ProxyPageSource_AfterSetTarget_ReflectsNewTarget()
    {
        var data1 = CreateMinimalDatabase();
        var data2 = CreateMinimalDatabase();
        using var source1 = new MemoryPageSource(data1);
        using var source2 = new MemoryPageSource(data2);

        // Write to source2 so its version differs
        source2.WritePage(1, new byte[4096]);

        var proxy = new ProxyPageSource(source1);
        long v1 = proxy.DataVersion;

        proxy.SetTarget(source2);
        long v2 = proxy.DataVersion;

        Assert.Equal(source1.DataVersion, v1);
        Assert.Equal(source2.DataVersion, v2);
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void DataVersion_ShadowPageSource_IncrementsOnWrite()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(inner);
        long before = shadow.DataVersion;

        shadow.WritePage(1, new byte[4096]);

        Assert.True(shadow.DataVersion > before);
    }

    [Fact]
    public void DataVersion_ShadowPageSource_ResetClearsShadowVersion()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(inner);

        shadow.WritePage(1, new byte[4096]);
        long afterWrite = shadow.DataVersion;

        shadow.Reset();
        long afterReset = shadow.DataVersion;

        Assert.True(afterWrite > afterReset);
        Assert.Equal(inner.DataVersion, afterReset);
    }

    [Fact]
    public void DataVersion_ShadowPageSource_CombinesBaseAndShadow()
    {
        var data = CreateMinimalDatabase();
        using var inner = new MemoryPageSource(data);
        using var shadow = new ShadowPageSource(inner);

        long baseVersion = inner.DataVersion;
        shadow.WritePage(1, new byte[4096]);
        shadow.WritePage(2, new byte[4096]);

        // Shadow version = base + 2 shadow writes
        Assert.Equal(baseVersion + 2, shadow.DataVersion);
    }

    /// <summary>
    /// Minimal IPageSource that does NOT override DataVersion,
    /// exercising the default interface method (returns 0).
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
}
