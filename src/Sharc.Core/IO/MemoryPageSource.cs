// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Runtime.InteropServices;
using Sharc.Core.Format;

namespace Sharc.Core.IO;

/// <summary>
/// Page source backed by an in-memory byte buffer.
/// All page reads are zero-copy span slices — no allocation, no I/O.
/// When constructed from a byte[], writes propagate back to the original buffer.
/// </summary>
public sealed class MemoryPageSource : IWritablePageSource
{
    private byte[] _data;
    private int _pageCount;

    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int PageCount => _pageCount;

    /// <summary>
    /// Creates a page source over a pre-loaded database buffer.
    /// Parses the database header to determine page size and count.
    /// If the memory is backed by a byte[], writes propagate to the original array.
    /// </summary>
    /// <param name="data">Complete SQLite database bytes.</param>
    /// <exception cref="Sharc.Exceptions.InvalidDatabaseException">Header is invalid.</exception>
    public MemoryPageSource(ReadOnlyMemory<byte> data)
    {
        _data = ExtractOrCopy(data);
        var header = DatabaseHeader.Parse(_data);
        PageSize = header.PageSize;
        _pageCount = header.PageCount;
    }

    /// <summary>
    /// Creates a page source over raw memory with explicit page size and count.
    /// Used internally for encrypted data where the header is not a valid SQLite header.
    /// </summary>
    internal MemoryPageSource(ReadOnlyMemory<byte> data, int pageSize, int pageCount)
    {
        _data = ExtractOrCopy(data);
        PageSize = pageSize;
        _pageCount = pageCount;
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ValidatePageNumber(pageNumber);
        int offset = (int)(pageNumber - 1) * PageSize;
        return _data.AsSpan(offset, PageSize);
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        GetPage(pageNumber).CopyTo(destination);
        return PageSize;
    }

    /// <inheritdoc />
    public void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1u, nameof(pageNumber));

        if (pageNumber > (uint)_pageCount)
        {
            // Grow the buffer to accommodate the new page
            int requiredSize = (int)pageNumber * PageSize;
            if (_data.Length < requiredSize)
            {
                Array.Resize(ref _data, requiredSize);
            }
            _pageCount = (int)pageNumber;
        }

        int offset = (int)(pageNumber - 1) * PageSize;
        source.CopyTo(_data.AsSpan(offset, PageSize));
    }

    /// <inheritdoc />
    public void Flush()
    {
        // No-op for in-memory source.
    }

    /// <summary>
    /// No-op — in-memory source owns no unmanaged resources.
    /// </summary>
    public void Dispose() { }

    private static byte[] ExtractOrCopy(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
            && segment.Array is not null
            && segment.Offset == 0
            && segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }
        return data.ToArray();
    }

    private void ValidatePageNumber(uint pageNumber)
    {
        if (pageNumber < 1 || pageNumber > (uint)_pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber,
                $"Page number must be between 1 and {_pageCount}.");
    }
}
