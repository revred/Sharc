using Sharc.Core;
using Sharc.Core.IO;
using Xunit;

namespace Sharc.Tests.IO;

public class WalPageSourceTests
{
    private const int PageSize = 4096;
    private const int PageCount = 10;

    [Fact]
    public void GetPage_NotInWal_DelegatesToInner()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: p => (byte)(p & 0xFF));
        var walMap = new Dictionary<uint, long>();
        var walData = Array.Empty<byte>();

        var source = new WalPageSource(inner, walData, walMap);

        var page = source.GetPage(3);
        Assert.Equal(PageSize, page.Length);
        Assert.Equal(3, page[0]); // FakePageSource fills byte 0 with page number
    }

    [Fact]
    public void GetPage_InWal_ReturnsWalData()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: p => 0);
        var walData = new byte[PageSize + 100]; // some WAL data
        long walOffset = 50;

        // Write distinctive bytes at the WAL offset
        walData[walOffset] = 0xDE;
        walData[walOffset + 1] = 0xAD;
        walData[walOffset + 2] = 0xBE;
        walData[walOffset + 3] = 0xEF;

        var walMap = new Dictionary<uint, long> { [3] = walOffset };
        var source = new WalPageSource(inner, walData, walMap);

        var page = source.GetPage(3);
        Assert.Equal(PageSize, page.Length);
        Assert.Equal(0xDE, page[0]);
        Assert.Equal(0xAD, page[1]);
        Assert.Equal(0xBE, page[2]);
        Assert.Equal(0xEF, page[3]);
    }

    [Fact]
    public void ReadPage_InWal_CopiesWalData()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: p => 0);
        var walData = new byte[PageSize + 100];
        long walOffset = 50;
        walData[walOffset] = 0x42;

        var walMap = new Dictionary<uint, long> { [5] = walOffset };
        var source = new WalPageSource(inner, walData, walMap);

        var dest = new byte[PageSize];
        int read = source.ReadPage(5, dest);

        Assert.Equal(PageSize, read);
        Assert.Equal(0x42, dest[0]);
    }

    [Fact]
    public void ReadPage_NotInWal_DelegatesToInner()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: p => (byte)(p & 0xFF));
        var walMap = new Dictionary<uint, long>();
        var walData = Array.Empty<byte>();

        var source = new WalPageSource(inner, walData, walMap);

        var dest = new byte[PageSize];
        int read = source.ReadPage(7, dest);

        Assert.Equal(PageSize, read);
        Assert.Equal(7, dest[0]);
    }

    [Fact]
    public void PageSize_DelegatesToInner()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: _ => 0);
        var source = new WalPageSource(inner, Array.Empty<byte>(), new Dictionary<uint, long>());

        Assert.Equal(PageSize, source.PageSize);
    }

    [Fact]
    public void PageCount_DelegatesToInner()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: _ => 0);
        var source = new WalPageSource(inner, Array.Empty<byte>(), new Dictionary<uint, long>());

        Assert.Equal(PageCount, source.PageCount);
    }

    [Fact]
    public void Dispose_DisposesInner()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: _ => 0);
        var source = new WalPageSource(inner, Array.Empty<byte>(), new Dictionary<uint, long>());

        source.Dispose();

        Assert.True(inner.IsDisposed);
    }

    [Fact]
    public void MultipleWalPages_EachResolvedCorrectly()
    {
        var inner = new FakePageSource(PageSize, PageCount, pageFiller: p => 0);
        var walData = new byte[PageSize * 3];

        // Page 2 at offset 0, page 5 at offset PageSize, page 8 at offset 2*PageSize
        walData[0] = 0xAA;
        walData[PageSize] = 0xBB;
        walData[PageSize * 2] = 0xCC;

        var walMap = new Dictionary<uint, long>
        {
            [2] = 0,
            [5] = PageSize,
            [8] = PageSize * 2
        };
        var source = new WalPageSource(inner, walData, walMap);

        Assert.Equal(0xAA, source.GetPage(2)[0]);
        Assert.Equal(0xBB, source.GetPage(5)[0]);
        Assert.Equal(0xCC, source.GetPage(8)[0]);
        Assert.Equal(0, source.GetPage(1)[0]); // not in WAL — from inner
    }

    #region Test Doubles

    private sealed class FakePageSource : IPageSource
    {
        private readonly Func<uint, byte> _pageFiller;
        private readonly byte[][] _pages;
        public bool IsDisposed { get; private set; }

        public int PageSize { get; }
        public int PageCount { get; }

        public FakePageSource(int pageSize, int pageCount, Func<uint, byte> pageFiller)
        {
            PageSize = pageSize;
            PageCount = pageCount;
            _pageFiller = pageFiller;
            _pages = new byte[pageCount][];
            for (int i = 0; i < pageCount; i++)
            {
                _pages[i] = new byte[pageSize];
                byte fill = pageFiller((uint)(i + 1));
                _pages[i][0] = fill; // Put distinctive byte at position 0
            }
        }

        public ReadOnlySpan<byte> GetPage(uint pageNumber) => _pages[pageNumber - 1];
        public int ReadPage(uint pageNumber, Span<byte> destination)
        {
            _pages[pageNumber - 1].CopyTo(destination);
            return PageSize;
        }
        public void Invalidate(uint pageNumber) { }
        public void Dispose() => IsDisposed = true;
    }

    #endregion
}
