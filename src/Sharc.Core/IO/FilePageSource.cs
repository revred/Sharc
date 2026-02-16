// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Lightweight file-backed page source using <see cref="RandomAccess"/> for on-demand reads.
/// Opens with <see cref="File.OpenHandle"/> Ã¢â‚¬â€ a thin wrapper around the OS file handle with
/// no internal buffering, async state machine, or Stream overhead. Only the 100-byte database
/// header is read on construction; individual pages are read on demand into a reusable buffer.
/// </summary>
/// <remarks>
/// <para>
/// Trade-offs vs other page sources:
/// <list type="bullet">
///   <item><see cref="MemoryPageSource"/>: faster per-read (zero-copy span), but requires entire file in memory.</item>
///   <item><see cref="SafeMemMapdPageSource"/>: faster per-read (zero-copy), but ~98 Ã‚Âµs OS mapping setup.</item>
///   <item><see cref="FilePageSource"/>: fast open (~1-5 Ã‚Âµs), one syscall per page read, small fixed buffer.</item>
/// </list>
/// </para>
/// <para>
/// Best suited for: opening many files briefly, reading a few pages from each, or when memory
/// footprint must stay minimal.
/// </para>
/// </remarks>
public sealed class FilePageSource : IWritablePageSource
{
    private readonly SafeFileHandle _handle;
    private readonly long _fileLength;
    private bool _disposed;

    [ThreadStatic]
    private static byte[]? _threadBuffer;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount => (int)((RandomAccess.GetLength(_handle) + PageSize - 1) / PageSize);

    /// <summary>
    /// Opens a SQLite database file for on-demand page reads and optional writes.
    /// Only reads the 100-byte header on construction.
    /// </summary>
    /// <param name="filePath">Path to the SQLite database file.</param>
    /// <param name="fileShareMode">File sharing mode. Default is <see cref="FileShare.ReadWrite"/>.</param>
    /// <param name="allowWrites">True to open the file with write access.</param>
    public FilePageSource(string filePath, FileShare fileShareMode = FileShare.ReadWrite, bool allowWrites = false)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Database file not found.", filePath);

        var access = allowWrites ? FileAccess.ReadWrite : FileAccess.Read;
        var mode = allowWrites ? FileMode.OpenOrCreate : FileMode.Open;

        _handle = File.OpenHandle(filePath, mode, access, fileShareMode,
            FileOptions.RandomAccess);

        _fileLength = RandomAccess.GetLength(_handle);
        if (_fileLength == 0 && !allowWrites)
        {
            _handle.Dispose();
            throw new ArgumentException("Database file is empty.", nameof(filePath));
        }

        if (_fileLength > 0)
        {
            // Read the database header
            Span<byte> headerBuf = stackalloc byte[SQLiteLayout.DatabaseHeaderSize];
            RandomAccess.Read(_handle, headerBuf, fileOffset: 0);

            try
            {
                var header = DatabaseHeader.Parse(headerBuf);
                PageSize = header.PageSize;
            }
            catch
            {
                _handle.Dispose();
                throw;
            }
        }
        else
        {
            // New database creation - defaults or wait for header write
            PageSize = 4096;
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageNumber(pageNumber);

        if (_threadBuffer == null || _threadBuffer.Length != PageSize)
        {
            _threadBuffer = new byte[PageSize];
        }

        long offset = (long)(pageNumber - 1) * PageSize;
        RandomAccess.Read(_handle, _threadBuffer, fileOffset: offset);
        return _threadBuffer;
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageNumber(pageNumber);

        long offset = (long)(pageNumber - 1) * PageSize;
        RandomAccess.Read(_handle, destination[..PageSize], fileOffset: offset);
        return PageSize;
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // We allow writing to page numbers up to PageCount + 1 to support growth
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1u);

        long offset = (long)(pageNumber - 1) * PageSize;
        RandomAccess.Write(_handle, source[..PageSize], fileOffset: offset);
    }

    /// <inheritdoc />
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // SafeFileHandle doesn't have a direct Flush method in .NET.
        // Durability is currently handled by OS-level file options if specified.
    }

    /// <summary>
    /// Closes the file handle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }

    private void ValidatePageNumber(uint pageNumber)
    {
        // SQLite pages are 1-indexed. Page 1 is the start.
        // We check against the current length of the file/source.
        if (pageNumber < 1 || pageNumber > (uint)((RandomAccess.GetLength(_handle) + PageSize - 1) / PageSize))
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and the current database size.");
    }
}