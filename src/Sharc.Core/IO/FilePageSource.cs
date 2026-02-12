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

using System.IO;
using Microsoft.Win32.SafeHandles;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Lightweight file-backed page source using <see cref="RandomAccess"/> for on-demand reads.
/// Opens with <see cref="File.OpenHandle"/> — a thin wrapper around the OS file handle with
/// no internal buffering, async state machine, or Stream overhead. Only the 100-byte database
/// header is read on construction; individual pages are read on demand into a reusable buffer.
/// </summary>
/// <remarks>
/// <para>
/// Trade-offs vs other page sources:
/// <list type="bullet">
///   <item><see cref="MemoryPageSource"/>: faster per-read (zero-copy span), but requires entire file in memory.</item>
///   <item><see cref="MemoryMappedPageSource"/>: faster per-read (zero-copy), but ~98 µs OS mapping setup.</item>
///   <item><see cref="FilePageSource"/>: fast open (~1-5 µs), one syscall per page read, small fixed buffer.</item>
/// </list>
/// </para>
/// <para>
/// Best suited for: opening many files briefly, reading a few pages from each, or when memory
/// footprint must stay minimal.
/// </para>
/// </remarks>
public sealed class FilePageSource : IPageSource
{
    private readonly SafeFileHandle _handle;
    private readonly byte[] _pageBuffer;
    private readonly long _fileLength;
    private bool _disposed;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount { get; }

    /// <summary>
    /// Opens a SQLite database file for on-demand page reads.
    /// Only reads the 100-byte header on construction.
    /// </summary>
    /// <param name="filePath">Path to the SQLite database file.</param>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="ArgumentException">File is empty.</exception>
    /// <exception cref="Sharc.Exceptions.InvalidDatabaseException">Database header is invalid.</exception>
    public FilePageSource(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Database file not found.", filePath);

        _handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            FileOptions.RandomAccess);

        _fileLength = RandomAccess.GetLength(_handle);
        if (_fileLength == 0)
        {
            _handle.Dispose();
            throw new ArgumentException("Database file is empty.", nameof(filePath));
        }

        // Read only the 100-byte database header — one syscall, stackalloc, zero heap pressure
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

        _pageBuffer = new byte[PageSize];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a span over an internal buffer that is reused across calls.
    /// The returned span is valid only until the next call to <see cref="GetPage"/>.
    /// </remarks>
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidatePageNumber(pageNumber);

        long offset = (long)(pageNumber - 1) * PageSize;
        RandomAccess.Read(_handle, _pageBuffer.AsSpan(), fileOffset: offset);
        return _pageBuffer;
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
        if (pageNumber < 1 || pageNumber > (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {PageCount}.");
    }
}
