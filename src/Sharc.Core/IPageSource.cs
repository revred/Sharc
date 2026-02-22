// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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

    /// <summary>
    /// Evicts the specified page from any internal cache, forcing a re-read from the backing store.
    /// </summary>
    void Invalidate(uint pageNumber);

    /// <summary>
    /// Monotonically increasing version that changes on data mutation via <see cref="IWritablePageSource.WritePage"/>.
    /// Must return 0 for sources that cannot track mutations (read-only, file-backed).
    /// </summary>
    long DataVersion { get; }

    /// <summary>
    /// Gets a <see cref="ReadOnlyMemory{T}"/> over a page's data that can be stored in fields.
    /// Unlike <see cref="GetPage"/>, the returned memory survives across method calls.
    /// Implementations should return zero-copy memory where possible (e.g., <c>AsMemory()</c>
    /// over a backing array). When zero-copy is not possible, copy via <c>GetPage(pageNumber).ToArray()</c>.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <returns>Read-only memory of page bytes.</returns>
    ReadOnlyMemory<byte> GetPageMemory(uint pageNumber);
}