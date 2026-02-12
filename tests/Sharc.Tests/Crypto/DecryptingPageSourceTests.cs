using System.Security.Cryptography;
using Sharc.Core;
using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Crypto;

public class DecryptingPageSourceTests
{
    private const int PageSize = 4096;
    private const int PageCount = 4;

    private static SharcKeyHandle CreateTestKey()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        return SharcKeyHandle.FromRawKey(keyBytes);
    }

    /// <summary>
    /// Builds a FakeEncryptedPageSource whose pages are pre-encrypted with the given transform.
    /// Each plaintext page is filled with a distinctive byte pattern.
    /// </summary>
    private static (FakePageSource inner, byte[][] plaintextPages) BuildEncryptedSource(
        AesGcmPageTransform transform)
    {
        int encPageSize = transform.TransformedPageSize(PageSize);
        var encryptedPages = new byte[PageCount][];
        var plaintextPages = new byte[PageCount][];

        for (int i = 0; i < PageCount; i++)
        {
            uint pageNum = (uint)(i + 1);
            var plain = new byte[PageSize];
            // Fill with distinctive pattern: page number repeated
            Array.Fill(plain, (byte)(pageNum & 0xFF));
            plaintextPages[i] = plain;

            var enc = new byte[encPageSize];
            transform.TransformWrite(plain, enc, pageNum);
            encryptedPages[i] = enc;
        }

        return (new FakePageSource(encPageSize, PageCount, encryptedPages), plaintextPages);
    }

    [Fact]
    public void GetPage_DecryptsInnerPage()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, plaintext) = BuildEncryptedSource(transform);

        using var source = new DecryptingPageSource(inner, transform, PageSize);

        for (uint p = 1; p <= PageCount; p++)
        {
            var page = source.GetPage(p);
            Assert.Equal(PageSize, page.Length);
            Assert.Equal(plaintext[p - 1], page.ToArray());
        }
    }

    [Fact]
    public void ReadPage_DecryptsIntoDest()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, plaintext) = BuildEncryptedSource(transform);

        using var source = new DecryptingPageSource(inner, transform, PageSize);
        var dest = new byte[PageSize];

        for (uint p = 1; p <= PageCount; p++)
        {
            int read = source.ReadPage(p, dest);
            Assert.Equal(PageSize, read);
            Assert.Equal(plaintext[p - 1], dest);
        }
    }

    [Fact]
    public void PageSize_ReturnsLogicalSize()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, _) = BuildEncryptedSource(transform);

        using var source = new DecryptingPageSource(inner, transform, PageSize);

        Assert.Equal(PageSize, source.PageSize);
        Assert.NotEqual(inner.PageSize, source.PageSize); // inner has encrypted size
    }

    [Fact]
    public void PageCount_DelegatesToInner()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, _) = BuildEncryptedSource(transform);

        using var source = new DecryptingPageSource(inner, transform, PageSize);

        Assert.Equal(PageCount, source.PageCount);
    }

    [Fact]
    public void Dispose_DisposesInner()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, _) = BuildEncryptedSource(transform);

        var source = new DecryptingPageSource(inner, transform, PageSize);
        source.Dispose();

        Assert.True(inner.IsDisposed);
    }

    [Fact]
    public void GetPage_AfterDispose_Throws()
    {
        using var key = CreateTestKey();
        using var transform = new AesGcmPageTransform(key);
        var (inner, _) = BuildEncryptedSource(transform);

        var source = new DecryptingPageSource(inner, transform, PageSize);
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.GetPage(1).ToArray());
    }

    #region Test Doubles

    private sealed class FakePageSource : IPageSource
    {
        private readonly byte[][] _pages;
        public bool IsDisposed { get; private set; }
        public int PageSize { get; }
        public int PageCount { get; }

        public FakePageSource(int pageSize, int pageCount, byte[][] pages)
        {
            PageSize = pageSize;
            PageCount = pageCount;
            _pages = pages;
        }

        public ReadOnlySpan<byte> GetPage(uint pageNumber) => _pages[pageNumber - 1];

        public int ReadPage(uint pageNumber, Span<byte> destination)
        {
            _pages[pageNumber - 1].CopyTo(destination);
            return PageSize;
        }

        public void Dispose() => IsDisposed = true;
    }

    #endregion
}
