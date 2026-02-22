/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.Write;

/// <summary>
/// TDD tests for PageManager — dirty page buffer + page allocation.
/// </summary>
public class PageManagerTests
{
    private const int PageSize = 4096;

    // ── GetPageForWrite: COW on first access ──

    [Fact]
    public void GetPageForWrite_FirstAccess_CopiesFromSource()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        source.SetPageByte(1, 0, 0xAA); // write a marker byte

        using var mgr = new PageManager(source);
        var page = mgr.GetPageForWrite(1);

        Assert.Equal(0xAA, page[0]);
    }

    [Fact]
    public void GetPageForWrite_SecondAccess_ReturnsSameBuffer()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        using var mgr = new PageManager(source);

        var page1 = mgr.GetPageForWrite(1);
        page1[0] = 0xBB;

        var page2 = mgr.GetPageForWrite(1);
        Assert.Equal(0xBB, page2[0]); // same buffer, mutation visible
    }

    [Fact]
    public void GetPageForWrite_DoesNotMutateSource()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        using var mgr = new PageManager(source);

        var page = mgr.GetPageForWrite(1);
        page[0] = 0xFF;

        // Source page should still be 0
        var originalPage = source.GetPage(1);
        Assert.Equal(0, originalPage[0]);
    }

    // ── AllocatePage: extend file when freelist empty ──

    [Fact]
    public void AllocatePage_EmptyFreelist_ReturnsNextPage()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        using var mgr = new PageManager(source);

        uint newPage = mgr.AllocatePage();
        Assert.Equal(4u, newPage); // 3 existing pages → next is 4
    }

    [Fact]
    public void AllocatePage_Twice_ReturnsConsecutivePages()
    {
        using var source = new FakePageSource(PageSize, pageCount: 5);
        using var mgr = new PageManager(source);

        uint p1 = mgr.AllocatePage();
        uint p2 = mgr.AllocatePage();
        Assert.Equal(6u, p1);
        Assert.Equal(7u, p2);
    }

    // ── GetDirtyPages: returns modified pages ──

    [Fact]
    public void GetDirtyPages_AfterWrite_ReturnsModifiedPages()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        using var mgr = new PageManager(source);

        var page1 = mgr.GetPageForWrite(1);
        page1[0] = 0x11;
        var page2 = mgr.GetPageForWrite(2);
        page2[0] = 0x22;

        var dirty = mgr.GetDirtyPages().ToList();
        Assert.Equal(2, dirty.Count);
        Assert.Contains(dirty, d => d.PageNumber == 1);
        Assert.Contains(dirty, d => d.PageNumber == 2);
    }

    [Fact]
    public void GetDirtyPages_AllocatedPage_Included()
    {
        using var source = new FakePageSource(PageSize, pageCount: 2);
        using var mgr = new PageManager(source);

        uint newPage = mgr.AllocatePage();
        var page = mgr.GetPageForWrite(newPage);
        page[0] = 0xFF;

        var dirty = mgr.GetDirtyPages().ToList();
        Assert.Contains(dirty, d => d.PageNumber == newPage);
    }

    // ── Reset: clears dirty state ──

    [Fact]
    public void Reset_ClearsDirtyPages()
    {
        using var source = new FakePageSource(PageSize, pageCount: 3);
        using var mgr = new PageManager(source);

        mgr.GetPageForWrite(1);
        mgr.GetPageForWrite(2);
        mgr.Reset();

        var dirty = mgr.GetDirtyPages().ToList();
        Assert.Empty(dirty);
    }

    // ── Multiple independent pages ──

    [Fact]
    public void GetPageForWrite_MultiplePagesIndependent()
    {
        using var source = new FakePageSource(PageSize, pageCount: 5);
        using var mgr = new PageManager(source);

        for (uint i = 1; i <= 5; i++)
        {
            var page = mgr.GetPageForWrite(i);
            page[0] = (byte)i;
        }

        // Verify each page has its own marker
        for (uint i = 1; i <= 5; i++)
        {
            var page = mgr.GetPageForWrite(i);
            Assert.Equal((byte)i, page[0]);
        }

        Assert.Equal(5, mgr.GetDirtyPages().Count());
    }

    // ── Fake page source ──

    private sealed class FakePageSource : IPageSource
    {
        private readonly byte[][] _pages;
        public int PageSize { get; }
        public int PageCount { get; }

        public FakePageSource(int pageSize, int pageCount)
        {
            PageSize = pageSize;
            PageCount = pageCount;
            _pages = new byte[pageCount][];
            for (int i = 0; i < pageCount; i++)
                _pages[i] = new byte[pageSize];
        }

        public void SetPageByte(uint pageNumber, int offset, byte value)
        {
            _pages[pageNumber - 1][offset] = value;
        }

        public int ReadPage(uint pageNumber, Span<byte> destination)
        {
            _pages[pageNumber - 1].CopyTo(destination);
            return PageSize;
        }

        public ReadOnlySpan<byte> GetPage(uint pageNumber)
        {
            return _pages[pageNumber - 1];
        }

        public long DataVersion => 0;
        public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber) => GetPage(pageNumber).ToArray();
        public void Invalidate(uint pageNumber) { }
        public void Dispose() { }
    }
}
