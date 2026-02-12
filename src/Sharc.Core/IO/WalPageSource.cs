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
    public void Dispose()
    {
        _inner.Dispose();
    }
}
