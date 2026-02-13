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

using System.IO;
using Microsoft.Win32.SafeHandles;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Lightweight file-backed page source using <see cref="RandomAccess"/> for on-demand reads.
/// Opens with <see cref="File.OpenHandle"/> â€” a thin wrapper around the OS file handle with
/// no internal buffering, async state machine, or Stream overhead. Only the 100-byte database
/// header is read on construction; individual pages are read on demand into a reusable buffer.
/// </summary>
/// <remarks>
/// <para>
/// Trade-offs vs other page sources:
/// <list type="bullet">
///   <item><see cref="MemoryPageSource"/>: faster per-read (zero-copy span), but requires entire file in memory.</item>
///   <item><see cref="SafeMemMapdPageSource"/>: faster per-read (zero-copy), but ~98 Âµs OS mapping setup.</item>
///   <item><see cref="FilePageSource"/>: fast open (~1-5 Âµs), one syscall per page read, small fixed buffer.</item>
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
    public int PageCount { get; }

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
            // Read only the 100-byte database header
            Span<byte> headerBuf = stackalloc byte[100];
            RandomAccess.Read(_handle, headerBuf, fileOffset: 0);

            try
            {
                var header = DatabaseHeader.Parse(headerBuf);
                PageSize = header.PageSize;
                PageCount = header.PageCount;
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
            PageCount = 0;
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
