using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// A page source that shadows an underlying source.
/// All writes are stored in memory until committed.
/// Reads check the shadow (dirty) pages first.
/// Dirty page buffers are rented from <see cref="ArrayPool{T}"/> and returned on clear/dispose.
/// </summary>
public sealed class ShadowPageSource : IWritablePageSource
{
    private readonly IPageSource _baseSource;
    private readonly Dictionary<uint, byte[]> _dirtyPages = new();
    private uint _maxDirtyPage;
    private bool _disposed;

    /// <inheritdoc />
    public ShadowPageSource(IPageSource baseSource)
    {
        _baseSource = baseSource ?? throw new ArgumentNullException(nameof(baseSource));
    }

    /// <inheritdoc />
    public int PageSize => _baseSource.PageSize;

    /// <inheritdoc />
    public int PageCount
    {
        get
        {
            int baseCount = _baseSource.PageCount;
            if (_dirtyPages.Count == 0) return baseCount;
            return Math.Max(baseCount, (int)_maxDirtyPage);
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirtyPages.TryGetValue(pageNumber, out var dirty))
            return dirty.AsSpan(0, PageSize);
        return _baseSource.GetPage(pageNumber);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dirtyPages.TryGetValue(pageNumber, out var dirty))
        {
            dirty.AsSpan(0, PageSize).CopyTo(destination);
            return PageSize;
        }
        return _baseSource.ReadPage(pageNumber, destination);
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dirtyPages.TryGetValue(pageNumber, out var dirty))
        {
            dirty = ArrayPool<byte>.Shared.Rent(PageSize);
            dirty.AsSpan(0, PageSize).Clear();
            _dirtyPages[pageNumber] = dirty;
        }
        source.CopyTo(dirty);
        if (pageNumber > _maxDirtyPage) _maxDirtyPage = pageNumber;
    }

    /// <inheritdoc />
    public void Flush()
    {
        // Shadow pages are in memory, nothing to flush to persistence yet.
    }

    /// <summary>
    /// Returns the collection of dirty pages to be written back to the base source.
    /// </summary>
    internal IReadOnlyDictionary<uint, byte[]> GetDirtyPages() => _dirtyPages;

    /// <summary>
    /// Clears all dirty pages (Rollback equivalent).
    /// </summary>
    internal void ClearShadow()
    {
        ReturnAllBuffers();
        _dirtyPages.Clear();
        _maxDirtyPage = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnAllBuffers();
        _dirtyPages.Clear();
        _maxDirtyPage = 0;
    }

    private void ReturnAllBuffers()
    {
        foreach (var buf in _dirtyPages.Values)
            ArrayPool<byte>.Shared.Return(buf);
    }
}
