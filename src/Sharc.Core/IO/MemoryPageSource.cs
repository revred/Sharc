using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Page source backed by an in-memory byte buffer.
/// All page reads are zero-copy span slices — no allocation, no I/O.
/// </summary>
public sealed class MemoryPageSource : IPageSource
{
    private readonly ReadOnlyMemory<byte> _data;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount { get; }

    /// <summary>
    /// Creates a page source over a pre-loaded database buffer.
    /// Parses the database header to determine page size and count.
    /// </summary>
    /// <param name="data">Complete SQLite database bytes.</param>
    /// <exception cref="Sharc.Exceptions.InvalidDatabaseException">Header is invalid.</exception>
    public MemoryPageSource(ReadOnlyMemory<byte> data)
    {
        _data = data;
        var header = DatabaseHeader.Parse(data.Span);
        PageSize = header.PageSize;
        PageCount = header.PageCount;
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ValidatePageNumber(pageNumber);
        int offset = (int)(pageNumber - 1) * PageSize;
        return _data.Span.Slice(offset, PageSize);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        GetPage(pageNumber).CopyTo(destination);
        return PageSize;
    }

    /// <summary>
    /// No-op — in-memory source owns no unmanaged resources.
    /// </summary>
    public void Dispose() { }

    private void ValidatePageNumber(uint pageNumber)
    {
        if (pageNumber < 1 || pageNumber > (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {PageCount}.");
    }
}
