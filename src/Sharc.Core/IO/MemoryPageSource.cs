/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Page source backed by an in-memory byte buffer.
/// All page reads are zero-copy span slices â€” no allocation, no I/O.
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

    /// <summary>
    /// Creates a page source over raw memory with explicit page size and count.
    /// Used internally for encrypted data where the header is not a valid SQLite header.
    /// </summary>
    internal MemoryPageSource(ReadOnlyMemory<byte> data, int pageSize, int pageCount)
    {
        _data = data;
        PageSize = pageSize;
        PageCount = pageCount;
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
    /// No-op â€” in-memory source owns no unmanaged resources.
    /// </summary>
    public void Dispose() { }

    private void ValidatePageNumber(uint pageNumber)
    {
        if (pageNumber < 1 || pageNumber > (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {PageCount}.");
    }
}
