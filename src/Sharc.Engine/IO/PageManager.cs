/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// Manages dirty page buffers and page allocation for write operations.
/// Wraps a read-only <see cref="IPageSource"/> and maintains copy-on-write buffers.
/// </summary>
internal sealed class PageManager : IDisposable
{
    private readonly IPageSource _source;
    private readonly Dictionary<uint, byte[]> _dirtyPages = new();
    private uint _nextPage;
    private bool _disposed;

    /// <summary>
    /// Creates a new page manager wrapping the given page source.
    /// </summary>
    /// <param name="source">The read-only page source.</param>
    public PageManager(IPageSource source)
    {
        _source = source;
        _nextPage = (uint)source.PageCount + 1;
    }

    /// <summary>
    /// Gets a writable span for the given page. On first access, copies from the source (COW).
    /// Subsequent accesses return the same dirty buffer.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <returns>A writable span over the page data.</returns>
    public Span<byte> GetPageForWrite(uint pageNumber)
    {
        if (_dirtyPages.TryGetValue(pageNumber, out byte[]? buffer))
            return buffer;

        buffer = ArrayPool<byte>.Shared.Rent(_source.PageSize);
        // Zero out the full rented buffer then copy source data
        buffer.AsSpan(0, _source.PageSize).Clear();

        if (pageNumber <= (uint)_source.PageCount)
            _source.ReadPage(pageNumber, buffer);

        _dirtyPages[pageNumber] = buffer;
        return buffer.AsSpan(0, _source.PageSize);
    }

    /// <summary>
    /// Allocates a new page by extending the file. Returns the new page number.
    /// </summary>
    /// <returns>1-based page number of the new page.</returns>
    public uint AllocatePage()
    {
        return _nextPage++;
    }

    /// <summary>
    /// Returns all dirty pages as (pageNumber, data) pairs.
    /// </summary>
    public IEnumerable<(uint PageNumber, ReadOnlyMemory<byte> Data)> GetDirtyPages()
    {
        foreach (var kvp in _dirtyPages)
            yield return (kvp.Key, kvp.Value.AsMemory(0, _source.PageSize));
    }

    /// <summary>
    /// Clears all dirty pages and returns buffers to the pool.
    /// Call after a successful commit.
    /// </summary>
    public void Reset()
    {
        foreach (var buffer in _dirtyPages.Values)
            ArrayPool<byte>.Shared.Return(buffer);
        _dirtyPages.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
