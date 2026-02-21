// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.IO.MemoryMappedFiles;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Page source backed by a memory-mapped file.
/// The OS handles paging Ã¢â‚¬â€ only accessed pages are loaded into physical memory.
/// Opening is near-instant (no upfront file read), and all page access is zero-copy.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="UnsafeMemoryManager"/> (the BCL <c>MemoryManager&lt;byte&gt;</c> pattern)
/// to expose the mapped region as safe <see cref="ReadOnlyMemory{T}"/> / <see cref="ReadOnlySpan{T}"/>.
/// The single <c>unsafe</c> block is confined to the constructor's pointer acquisition.
/// All subsequent page reads are fully safe span slices.
/// </para>
/// <para>
/// Maximum supported file size is <see cref="int.MaxValue"/> (~2 GiB) due to
/// <see cref="Span{T}"/> length limits. For larger files, use a streaming page source.
/// </para>
/// </remarks>
public sealed class SafeMemMapdPageSource : IPageSource
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly UnsafeMemoryManager _memoryManager;
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
    public SafeMemMapdPageSource(string filePath)
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

        // SAFETY: The single unsafe block acquires the OS-pinned pointer and wraps it
        // in UnsafeMemoryManager (BCL MemoryManager<byte> pattern) so all downstream
        // code uses safe Memory<byte> / Span<byte>.
        unsafe
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            ptr += _accessor.PointerOffset;
            _memoryManager = new UnsafeMemoryManager(ptr, fileLength);
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
    public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageNumber(pageNumber);
        int offset = (int)(pageNumber - 1) * PageSize;
        return _memory.Slice(offset, PageSize);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        GetPage(pageNumber).CopyTo(destination);
        return PageSize;
    }

    /// <inheritdoc />
    public void Invalidate(uint pageNumber)
    {
        // OS-managed mapping, no internal cache to invalidate.
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