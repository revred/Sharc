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

namespace Sharc.Core;

/// <summary>
/// Abstraction for reading database pages from any backing store.
/// Implementations: file-backed, memory-backed, cached, encrypted.
/// </summary>
public interface IPageSource : IDisposable
{
    /// <summary>
    /// Gets the page size in bytes.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Gets the total number of pages in the database.
    /// </summary>
    int PageCount { get; }

    /// <summary>
    /// Reads a page into the provided buffer.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="destination">Buffer to write page data into. Must be at least <see cref="PageSize"/> bytes.</param>
    /// <returns>Number of bytes read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Page number is out of range.</exception>
    int ReadPage(uint pageNumber, Span<byte> destination);

    /// <summary>
    /// Gets a read-only span over a page's data. May return a reference to cached data.
    /// The span is valid only until the next call to this method.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <returns>Read-only span of page bytes.</returns>
    ReadOnlySpan<byte> GetPage(uint pageNumber);
}
