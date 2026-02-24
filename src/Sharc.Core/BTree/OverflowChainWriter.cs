// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;

namespace Sharc.Core.BTree;

/// <summary>
/// Writes overflow chains for large record payloads that exceed inline page capacity.
/// Each overflow page stores a 4-byte next-pointer at offset 0, followed by payload data.
/// </summary>
internal sealed class OverflowChainWriter
{
    private readonly IWritablePageSource _source;
    private readonly int _usablePageSize;
    private readonly Dictionary<uint, byte[]> _pageCache;
    private readonly List<byte[]> _rentedBuffers;
    private readonly Func<uint>? _freePageAllocator;
    private readonly Func<uint> _nextAllocPageProvider;

    public OverflowChainWriter(
        IWritablePageSource source,
        int usablePageSize,
        Dictionary<uint, byte[]> pageCache,
        List<byte[]> rentedBuffers,
        Func<uint>? freePageAllocator,
        Func<uint> nextAllocPageProvider)
    {
        _source = source;
        _usablePageSize = usablePageSize;
        _pageCache = pageCache;
        _rentedBuffers = rentedBuffers;
        _freePageAllocator = freePageAllocator;
        _nextAllocPageProvider = nextAllocPageProvider;
    }

    /// <summary>
    /// Writes the overflow portion of a record payload into a chain of overflow pages.
    /// Patches the cell buffer's overflow pointer (last 4 bytes) with the first overflow page number.
    /// </summary>
    public void WriteOverflowChain(Span<byte> cellBuf, ReadOnlySpan<byte> fullPayload, int inlineSize)
    {
        int overflowDataPerPage = _usablePageSize - 4; // 4 bytes reserved for next-page pointer
        int remaining = fullPayload.Length - inlineSize;
        int srcOffset = inlineSize;

        uint firstOverflowPage = 0;
        uint prevOverflowPage = 0;
        byte[]? prevPageBuf = null;

        while (remaining > 0)
        {
            uint overflowPage = AllocateOverflowPage();
            if (firstOverflowPage == 0)
                firstOverflowPage = overflowPage;

            // Link previous page to this one
            if (prevPageBuf != null)
            {
                BinaryPrimitives.WriteUInt32BigEndian(prevPageBuf, overflowPage);
                WritePageBuffer(prevOverflowPage, prevPageBuf);
            }

            var pageBuf = RentPageBuffer();
            int toCopy = Math.Min(remaining, overflowDataPerPage);
            fullPayload.Slice(srcOffset, toCopy).CopyTo(pageBuf.AsSpan(4));

            // Next-pointer is 0 (end of chain) until the next iteration patches it
            BinaryPrimitives.WriteUInt32BigEndian(pageBuf, 0);

            prevOverflowPage = overflowPage;
            prevPageBuf = pageBuf;
            srcOffset += toCopy;
            remaining -= toCopy;
        }

        // Write the last overflow page (next-pointer stays 0 = end of chain)
        if (prevPageBuf != null)
            WritePageBuffer(prevOverflowPage, prevPageBuf);

        // Patch the cell's overflow pointer (last 4 bytes of the cell)
        BinaryPrimitives.WriteUInt32BigEndian(cellBuf[(cellBuf.Length - 4)..], firstOverflowPage);
    }

    /// <summary>
    /// Allocates a page for overflow storage. Prefers freelist pages, falls back to extending the file.
    /// Does NOT write a B-tree page header (overflow pages are raw).
    /// </summary>
    private uint AllocateOverflowPage()
    {
        uint page = _freePageAllocator?.Invoke() ?? 0;

        if (page == 0)
            page = _nextAllocPageProvider();

        // Track a buffer for this page but don't write a B-tree header
        var buf = RentPageBuffer();
        _pageCache[page] = buf;

        return page;
    }

    private byte[] RentPageBuffer()
    {
        var buf = ArrayPool<byte>.Shared.Rent(_source.PageSize);
        buf.AsSpan(0, _source.PageSize).Clear();
        _rentedBuffers.Add(buf);
        return buf;
    }

    private void WritePageBuffer(uint pageNumber, byte[] buffer)
    {
        _source.WritePage(pageNumber, buffer.AsSpan(0, _source.PageSize));
        _pageCache[pageNumber] = buffer;
    }
}
