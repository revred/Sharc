/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.IO.MemoryMappedFiles;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Page source backed by a memory-mapped file.
/// The OS handles paging — only accessed pages are loaded into physical memory.
/// Opening is near-instant (no upfront file read), and all page access is zero-copy.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="UnmanagedMemoryManager"/> (the BCL <c>MemoryManager&lt;byte&gt;</c> pattern)
/// to expose the mapped region as safe <see cref="ReadOnlyMemory{T}"/> / <see cref="ReadOnlySpan{T}"/>.
/// The single <c>unsafe</c> block is confined to the constructor's pointer acquisition.
/// All subsequent page reads are fully safe span slices.
/// </para>
/// <para>
/// Maximum supported file size is <see cref="int.MaxValue"/> (~2 GiB) due to
/// <see cref="Span{T}"/> length limits. For larger files, use a streaming page source.
/// </para>
/// </remarks>
public sealed class MemoryMappedPageSource : IPageSource
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly UnmanagedMemoryManager _memoryManager;
    private readonly ReadOnlyMemory<byte> _memory;
    private bool _disposed;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount { get; }

    /// <summary>
    /// Memory-maps a SQLite database file for zero-copy page access.
    /// </summary>
    /// <param name="filePath">Path to the SQLite database file.</param>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="ArgumentException">File is empty or exceeds 2 GiB.</exception>
    /// <exception cref="Sharc.Exceptions.InvalidDatabaseException">Database header is invalid.</exception>
    public MemoryMappedPageSource(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Database file not found.", filePath);
        if (fileInfo.Length == 0)
            throw new ArgumentException("Database file is empty.", nameof(filePath));
        if (fileInfo.Length > int.MaxValue)
            throw new ArgumentException(
                $"File size ({fileInfo.Length:N0} bytes) exceeds the 2 GiB limit for memory-mapped access.",
                nameof(filePath));

        var fileLength = (int)fileInfo.Length;

        _mmf = MemoryMappedFile.CreateFromFile(
            filePath, FileMode.Open, mapName: null, capacity: 0,
            MemoryMappedFileAccess.Read);

        _accessor = _mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);

        // SAFETY: The single unsafe block — acquires the OS-pinned pointer and wraps it
        // in UnmanagedMemoryManager (BCL MemoryManager<byte> pattern) so all downstream
        // code uses safe Memory<byte> / Span<byte>.
        unsafe
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            ptr += _accessor.PointerOffset;
            _memoryManager = new UnmanagedMemoryManager(ptr, fileLength);
        }

        _memory = _memoryManager.Memory;

        var header = DatabaseHeader.Parse(_memory.Span);
        PageSize = header.PageSize;
        PageCount = header.PageCount;
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageNumber(pageNumber);
        int offset = (int)(pageNumber - 1) * PageSize;
        return _memory.Span.Slice(offset, PageSize);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        GetPage(pageNumber).CopyTo(destination);
        return PageSize;
    }

    /// <summary>
    /// Releases the memory-mapped view, file mapping, and pointer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        ((IDisposable)_memoryManager).Dispose();
        _accessor.Dispose();
        _mmf.Dispose();
    }

    private void ValidatePageNumber(uint pageNumber)
    {
        if (pageNumber < 1 || pageNumber > (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {PageCount}.");
    }
}
