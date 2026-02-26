/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Core;

/// <summary>
/// Extends <see cref="IPageSource"/> with write operations for database mutation.
/// </summary>
public interface IPageStore : IPageSource
{
    /// <summary>
    /// Writes a full page of data to the store.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="data">Page data to write. Must be exactly <see cref="IPageSource.PageSize"/> bytes.</param>
    void WritePage(uint pageNumber, ReadOnlySpan<byte> data);

    /// <summary>
    /// Allocates a new page and returns its page number.
    /// </summary>
    /// <returns>1-based page number of the newly allocated page.</returns>
    uint AllocatePage();

    /// <summary>
    /// Frees a page, returning it to the freelist.
    /// </summary>
    /// <param name="pageNumber">1-based page number to free.</param>
    void FreePage(uint pageNumber);

    /// <summary>
    /// Flushes all pending writes to the underlying storage.
    /// </summary>
    void Sync();
}
