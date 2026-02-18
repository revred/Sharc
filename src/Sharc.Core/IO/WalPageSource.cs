// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Core.IO;

/// <summary>
/// Page source that overlays WAL (Write-Ahead Log) pages on top of the main database file.
/// When a page is present in the WAL, the WAL version is returned instead of the main file version.
/// This provides snapshot isolation for reading WAL-mode databases.
/// </summary>
public sealed class WalPageSource : IPageSource
{
    private readonly IPageSource _inner;
    private readonly ReadOnlyMemory<byte> _walData;
    private readonly Dictionary<uint, long> _walFrameMap;

    /// <inheritdoc />
    public int PageSize => _inner.PageSize;

    /// <inheritdoc />
    public int PageCount => _inner.PageCount;

    /// <summary>
    /// Creates a WAL page source wrapping an inner page source.
    /// </summary>
    /// <param name="inner">The underlying main database page source.</param>
    /// <param name="walData">The complete WAL file contents.</param>
    /// <param name="walFrameMap">
    /// Map from page numbers to byte offsets in <paramref name="walData"/>
    /// where each page's data begins.
    /// </param>
    public WalPageSource(IPageSource inner, ReadOnlyMemory<byte> walData,
        Dictionary<uint, long> walFrameMap)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _walData = walData;
        _walFrameMap = walFrameMap ?? throw new ArgumentNullException(nameof(walFrameMap));
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        if (_walFrameMap.TryGetValue(pageNumber, out long walOffset))
        {
            return _walData.Span.Slice((int)walOffset, PageSize);
        }

        return _inner.GetPage(pageNumber);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        if (_walFrameMap.TryGetValue(pageNumber, out long walOffset))
        {
            _walData.Span.Slice((int)walOffset, PageSize).CopyTo(destination);
            return PageSize;
        }

        return _inner.ReadPage(pageNumber, destination);
    }

    /// <inheritdoc />
    public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);

    /// <inheritdoc />
    public void Dispose()
    {
        _inner.Dispose();
    }
}