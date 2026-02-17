// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sharc.Core.Format;

namespace Sharc.Core.BTree;

/// <summary>
/// Insert/update/delete operations on a table B-tree.
/// Mirrors <see cref="BTreeCursor"/> for navigation,
/// but modifies pages via <see cref="IWritablePageSource"/>.
/// Page buffers are rented from <see cref="ArrayPool{T}"/> and cached
/// for the lifetime of this instance. Call <see cref="Dispose"/> to return them.
/// </summary>
internal sealed class BTreeMutator : IDisposable
{
    private readonly IWritablePageSource _source;
    private readonly int _usablePageSize;
    private readonly Dictionary<uint, byte[]> _pageCache = new();
    private readonly List<byte[]> _rentedBuffers = new();
    private bool _disposed;

    /// <summary>Header size for a leaf table page.</summary>
    private const int LeafHeaderSize = SQLiteLayout.TableLeafHeaderSize;   // 8
    /// <summary>Header size for an interior table page.</summary>
    private const int InteriorHeaderSize = SQLiteLayout.TableInteriorHeaderSize; // 12

    /// <summary>Describes a cell's location within a contiguous assembly buffer.</summary>
    private readonly struct CellRef
    {
        public readonly int Offset;
        public readonly int Length;
        public CellRef(int offset, int length) { Offset = offset; Length = length; }
    }

    private readonly Func<uint>? _freePageAllocator;
    private readonly Action<uint>? _freePageCallback;

    public BTreeMutator(IWritablePageSource source, int usablePageSize,
        Func<uint>? freePageAllocator = null, Action<uint>? freePageCallback = null)
    {
        _source = source;
        _usablePageSize = usablePageSize;
        _freePageAllocator = freePageAllocator;
        _freePageCallback = freePageCallback;
    }

    /// <summary>Number of pages currently held in the internal cache. Exposed for testing.</summary>
    internal int CachedPageCount => _pageCache.Count;

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>
    /// Inserts a record into the table B-tree rooted at <paramref name="rootPage"/>.
    /// Returns the (possibly new) root page number — the root changes when the root page splits.
    /// </summary>
    public uint Insert(uint rootPage, long rowId, ReadOnlySpan<byte> recordPayload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Build the cell bytes (stored in a rented/stack buffer)
        int cellSize = CellBuilder.ComputeTableLeafCellSize(rowId, recordPayload.Length, _usablePageSize);
        Span<byte> cellBuf = cellSize <= 512 ? stackalloc byte[cellSize] : new byte[cellSize];
        CellBuilder.BuildTableLeafCell(rowId, recordPayload, cellBuf, _usablePageSize);
        ReadOnlySpan<byte> cellBytes = cellBuf[..cellSize];

        // Navigate from root to the correct leaf, collecting the ancestor path.
        var path = new List<(uint PageNum, int CellIndex)>();
        uint currentPage = rootPage;

        while (true)
        {
            var page = ReadPageBuffer(currentPage);
            int hdrOff = currentPage == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var hdr = BTreePageHeader.Parse(page.AsSpan(hdrOff));

            if (hdr.IsLeaf)
            {
                // Binary search for insertion point
                int insertIdx = FindLeafInsertionPoint(page, hdrOff, hdr, rowId);
                path.Add((currentPage, insertIdx));
                break;
            }

            // Interior page — binary search for child
            int childIdx = FindInteriorChild(page, hdrOff, hdr, rowId, out uint childPage);
            path.Add((currentPage, childIdx));
            currentPage = childPage;
        }

        // Insert into the leaf (last element in path). Splits propagate upward.
        return InsertCellAndSplit(path, path.Count - 1, cellBytes, rowId, rootPage);
    }

    /// <summary>
    /// Deletes a record from the table B-tree by rowid.
    /// Returns whether the row was found and the (unchanged) root page number.
    /// Interior page keys are NOT modified — they remain as valid routing hints per SQLite spec.
    /// </summary>
    public (bool Found, uint RootPage) Delete(uint rootPage, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Navigate from root to the correct leaf
        uint currentPage = rootPage;
        while (true)
        {
            var page = ReadPageBuffer(currentPage);
            int hdrOff = currentPage == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var hdr = BTreePageHeader.Parse(page.AsSpan(hdrOff));

            if (hdr.IsLeaf)
            {
                var (cellIdx, found) = FindLeafCellByRowId(page, hdrOff, hdr, rowId);
                if (!found)
                    return (false, rootPage);

                RemoveCellFromPage(page, hdrOff, hdr, cellIdx);
                WritePageBuffer(currentPage, page);
                return (true, rootPage);
            }

            // Interior page — descend to child
            FindInteriorChild(page, hdrOff, hdr, rowId, out uint childPage);
            currentPage = childPage;
        }
    }

    /// <summary>
    /// Updates a record in the table B-tree by deleting the old record and inserting a new one
    /// with the same rowid. Returns whether the row was found and the (possibly new) root page.
    /// </summary>
    public (bool Found, uint RootPage) Update(uint rootPage, long rowId, ReadOnlySpan<byte> newRecordPayload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var (found, root) = Delete(rootPage, rowId);
        if (!found) return (false, root);
        root = Insert(root, rowId, newRecordPayload);
        return (true, root);
    }

    /// <summary>
    /// Returns the maximum rowid in the table B-tree, or 0 if the tree is empty.
    /// </summary>
    public long GetMaxRowId(uint rootPage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint pageNum = rootPage;
        while (true)
        {
            var page = ReadPageBuffer(pageNum);
            int hdrOff = pageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var hdr = BTreePageHeader.Parse(page.AsSpan(hdrOff));

            if (hdr.IsLeaf)
            {
                if (hdr.CellCount == 0) return 0;
                // Last cell has the max rowid
                int cellPtr = hdr.GetCellPointer(page.AsSpan(hdrOff), hdr.CellCount - 1);
                CellParser.ParseTableLeafCell(page.AsSpan(cellPtr), out _, out long maxRowId);
                return maxRowId;
            }

            // Interior — descend to right-most child
            pageNum = hdr.RightChildPage;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pageCache.Clear();
        foreach (var buf in _rentedBuffers)
            ArrayPool<byte>.Shared.Return(buf);
        _rentedBuffers.Clear();
    }

    // ── Navigation helpers ─────────────────────────────────────────

    /// <summary>
    /// Binary-search a leaf page for an exact rowid match.
    /// Returns the cell index and whether it was an exact match.
    /// </summary>
    private static (int Index, bool Found) FindLeafCellByRowId(byte[] page, int hdrOff, BTreePageHeader hdr, long rowId)
    {
        int lo = 0, hi = hdr.CellCount - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int cellPtr = hdr.GetCellPointer(page.AsSpan(hdrOff), mid);
            CellParser.ParseTableLeafCell(page.AsSpan(cellPtr), out _, out long midRowId);

            if (midRowId < rowId) lo = mid + 1;
            else if (midRowId > rowId) hi = mid - 1;
            else return (mid, true);
        }
        return (lo, false);
    }

    /// <summary>Binary-search a leaf page for the insertion index of <paramref name="rowId"/>.</summary>
    private static int FindLeafInsertionPoint(byte[] page, int hdrOff, BTreePageHeader hdr, long rowId)
    {
        int lo = 0, hi = hdr.CellCount - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int cellPtr = hdr.GetCellPointer(page.AsSpan(hdrOff), mid);
            CellParser.ParseTableLeafCell(page.AsSpan(cellPtr), out _, out long midRowId);

            if (midRowId < rowId) lo = mid + 1;
            else if (midRowId > rowId) hi = mid - 1;
            else return mid; // exact match — overwrite position
        }
        return lo; // insertion point
    }

    /// <summary>
    /// Binary-search an interior page to find the child to descend into.
    /// Also returns the cell index for the ancestor path.
    /// </summary>
    private static int FindInteriorChild(byte[] page, int hdrOff, BTreePageHeader hdr,
        long rowId, out uint childPage)
    {
        int lo = 0, hi = hdr.CellCount - 1;
        int idx = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int cellPtr = hdr.GetCellPointer(page.AsSpan(hdrOff), mid);
            CellParser.ParseTableInteriorCell(page.AsSpan(cellPtr), out _, out long key);

            if (key >= rowId) { idx = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        if (idx != -1)
        {
            int cellPtr = hdr.GetCellPointer(page.AsSpan(hdrOff), idx);
            CellParser.ParseTableInteriorCell(page.AsSpan(cellPtr), out uint leftChild, out _);
            childPage = leftChild;
            return idx;
        }

        // rowId > all keys → descend to rightmost child
        childPage = hdr.RightChildPage;
        return hdr.CellCount; // index beyond last cell (signals right-child descent)
    }

    // ── Insert + split engine ──────────────────────────────────────

    /// <summary>
    /// Inserts a cell into the page at <paramref name="pathIndex"/> in the ancestor path.
    /// If the page is full, splits it and promotes the median into the parent.
    /// Returns the (possibly new) root page number.
    /// </summary>
    private uint InsertCellAndSplit(
        List<(uint PageNum, int CellIndex)> path,
        int pathIndex,
        ReadOnlySpan<byte> cellBytes,
        long promotedRowId,
        uint currentRoot)
    {
        var (pageNum, insertIdx) = path[pathIndex];
        var pageBuf = ReadPageBuffer(pageNum);
        int hdrOff = pageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
        var hdr = BTreePageHeader.Parse(pageBuf.AsSpan(hdrOff));

        // Try to insert into existing page
        if (TryInsertCell(pageBuf, hdrOff, hdr, insertIdx, cellBytes))
        {
            WritePageBuffer(pageNum, pageBuf);
            return currentRoot;
        }

        // ── Page is full — split ──────────────────────────────────

        // Gather all existing cells + the new cell into a contiguous buffer
        var (cellBuf, cellRefs) = GatherCellsWithInsertion(pageBuf, hdrOff, hdr, insertIdx, cellBytes);

        // Split point: divide roughly in half by total byte count
        int totalBytes = 0;
        foreach (var c in cellRefs) totalBytes += c.Length;
        int halfTarget = totalBytes / 2;

        int splitIdx = 0;
        int runningBytes = 0;
        for (int i = 0; i < cellRefs.Count; i++)
        {
            runningBytes += cellRefs[i].Length;
            if (runningBytes >= halfTarget) { splitIdx = i; break; }
        }

        // Ensure split leaves at least 1 cell on each side
        if (splitIdx == 0) splitIdx = 1;
        if (splitIdx >= cellRefs.Count - 1) splitIdx = cellRefs.Count - 2;

        // For leaf pages: left gets [0..splitIdx], right gets [splitIdx+1..end]
        // The median (splitIdx) is promoted to parent
        bool isLeaf = hdr.IsLeaf;

        // Parse the median cell to get the promoted rowId
        var medianRef = cellRefs[splitIdx];
        var medianSpan = cellBuf.AsSpan(medianRef.Offset, medianRef.Length);
        long medianRowId;
        if (isLeaf)
        {
            CellParser.ParseTableLeafCell(medianSpan, out _, out medianRowId);
        }
        else
        {
            CellParser.ParseTableInteriorCell(medianSpan, out _, out medianRowId);
        }

        // Allocate a new page for the right sibling
        uint newPageNum = AllocateNewPage();

        // Build left page (reuse existing page buffer)
        if (isLeaf)
        {
            // Left gets [0..splitIdx] (inclusive — leaf keeps the median too)
            BuildLeafPage(pageBuf, hdrOff, cellBuf, cellRefs, 0, splitIdx);
            WritePageBuffer(pageNum, pageBuf);

            // Right gets [splitIdx+1..end]
            var rightBuf = RentPageBuffer();
            BuildLeafPage(rightBuf, 0, cellBuf, cellRefs, splitIdx + 1, cellRefs.Count - 1);
            WritePageBuffer(newPageNum, rightBuf);
        }
        else
        {
            CellParser.ParseTableInteriorCell(medianSpan, out uint medianLeftChild, out _);

            // Left page: cells [0..splitIdx-1], rightChild = medianLeftChild
            BuildInteriorPage(pageBuf, hdrOff, cellBuf, cellRefs, 0, splitIdx - 1, medianLeftChild);
            WritePageBuffer(pageNum, pageBuf);

            // Right page: cells [splitIdx+1..end], rightChild = original rightChild
            var rightBuf = RentPageBuffer();
            uint originalRightChild = hdr.RightChildPage;
            BuildInteriorPage(rightBuf, 0, cellBuf, cellRefs, splitIdx + 1, cellRefs.Count - 1, originalRightChild);
            WritePageBuffer(newPageNum, rightBuf);
        }

        if (pathIndex == 0)
        {
            // Root split with retention:
            // Allocate a new page for the Left Half
            uint newLeftPage = AllocateNewPage();

            // Copy the content of 'pageNum' (which currently holds TableLeaf[Left]) to 'newLeftPage'
            var leftContent = ReadPageBuffer(pageNum);
            WritePageBuffer(newLeftPage, leftContent);

            // Now 'pageNum' (Root) can be overwritten as the new Interior Root.
            var rootBuf = RentPageBuffer();

            // CRITICAL: If we are splitting the root on Page 1, we MUST preserve the 100-byte database header.
            if (pageNum == 1)
            {
                pageBuf.AsSpan(0, SQLiteLayout.DatabaseHeaderSize).CopyTo(rootBuf);
            }

            // Build interior cell: leftChild=newLeftPage, rowId=medianRowId
            Span<byte> interiorCell = stackalloc byte[16]; // max interior cell size
            int interiorSize = CellBuilder.BuildTableInteriorCell(newLeftPage, medianRowId, interiorCell);

            var newRootHdr = new BTreePageHeader(
                BTreePageType.InteriorTable,
                0, 1, // 1 cell
                (ushort)(_usablePageSize - interiorSize),
                0,
                newPageNum // right child = newRightPage
            );

            int rootHdrOff = pageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            BTreePageHeader.Write(rootBuf.AsSpan(rootHdrOff), newRootHdr);

            // Write cell pointer
            int cellPtrOff = rootHdrOff + InteriorHeaderSize;
            ushort cellContentOff = (ushort)(_usablePageSize - interiorSize);
            BinaryPrimitives.WriteUInt16BigEndian(rootBuf.AsSpan(cellPtrOff), cellContentOff);

            // Write cell content
            interiorCell[..interiorSize].CopyTo(rootBuf.AsSpan(cellContentOff));

            // Write the new Root Page
            WritePageBuffer(pageNum, rootBuf);

            // Return the SAME root page number
            return currentRoot;
        }
        else
        {
            // Build interior cell to insert into parent
            Span<byte> interiorCell = stackalloc byte[16];
            int interiorSize = CellBuilder.BuildTableInteriorCell(pageNum, medianRowId, interiorCell);

            UpdateParentAfterSplit(path, pathIndex - 1, interiorCell[..interiorSize], newPageNum);

            return InsertCellAndSplit(path, pathIndex - 1, interiorCell[..interiorSize], medianRowId, currentRoot);
        }
    }

    /// <summary>
    /// After splitting, update the parent page so that the pointer that previously led to
    /// the old child now properly references both the old page (via the promoted cell's left-child)
    /// and the new page (via the right reference).
    /// </summary>
    private void UpdateParentAfterSplit(
        List<(uint PageNum, int CellIndex)> path,
        int parentPathIndex,
        ReadOnlySpan<byte> promotedCell,
        uint newRightChild)
    {
        var (parentPageNum, parentCellIdx) = path[parentPathIndex];
        var parentBuf = ReadPageBuffer(parentPageNum);
        int hdrOff = parentPageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
        var hdr = BTreePageHeader.Parse(parentBuf.AsSpan(hdrOff));

        if (parentCellIdx >= hdr.CellCount)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                parentBuf.AsSpan(hdrOff + SQLiteLayout.RightChildPageOffset), newRightChild);
            WritePageBuffer(parentPageNum, parentBuf);
        }
        else
        {
            int cellPtr = hdr.GetCellPointer(parentBuf.AsSpan(hdrOff), parentCellIdx);
            BinaryPrimitives.WriteUInt32BigEndian(parentBuf.AsSpan(cellPtr), newRightChild);
            WritePageBuffer(parentPageNum, parentBuf);
        }
    }

    // ── Page building helpers ──────────────────────────────────────

    /// <summary>
    /// Tries to insert a cell into a page. Returns false if there isn't enough free space.
    /// </summary>
    private bool TryInsertCell(byte[] pageBuf, int hdrOff, BTreePageHeader hdr,
        int insertIdx, ReadOnlySpan<byte> cellBytes)
    {
        int headerSize = hdr.HeaderSize;
        int cellPtrArrayEnd = hdrOff + headerSize + (hdr.CellCount + 1) * 2;
        int cellContentStart = hdr.CellContentOffset == 0 ? _usablePageSize : hdr.CellContentOffset;

        int requiredSpace = cellBytes.Length + 2;
        int availableSpace = cellContentStart - cellPtrArrayEnd;

        if (availableSpace < requiredSpace)
        {
            int totalFree = availableSpace + hdr.FragmentedFreeBytes;
            if (totalFree >= requiredSpace)
            {
                DefragmentPage(pageBuf, hdrOff, hdr);
                hdr = BTreePageHeader.Parse(pageBuf.AsSpan(hdrOff));
                cellContentStart = hdr.CellContentOffset == 0 ? _usablePageSize : hdr.CellContentOffset;
                cellPtrArrayEnd = hdrOff + headerSize + (hdr.CellCount + 1) * 2;
                availableSpace = cellContentStart - cellPtrArrayEnd;
            }

            if (availableSpace < requiredSpace)
                return false;
        }

        int newCellOffset = cellContentStart - cellBytes.Length;
        cellBytes.CopyTo(pageBuf.AsSpan(newCellOffset));

        var pageSpan = pageBuf.AsSpan();
        int ptrBase = hdrOff + headerSize;
        for (int i = hdr.CellCount - 1; i >= insertIdx; i--)
        {
            ushort ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + i * 2)..]);
            BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + (i + 1) * 2)..], ptr);
        }

        BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + insertIdx * 2)..], (ushort)newCellOffset);

        var newHdr = new BTreePageHeader(
            hdr.PageType,
            hdr.FirstFreeblockOffset,
            (ushort)(hdr.CellCount + 1),
            (ushort)newCellOffset,
            hdr.FragmentedFreeBytes,
            hdr.RightChildPage
        );
        BTreePageHeader.Write(pageSpan[hdrOff..], newHdr);

        return true;
    }

    /// <summary>
    /// Collects all cells from a page plus the new cell at the insertion point.
    /// Packs cells into a single contiguous rented buffer instead of per-cell allocations.
    /// </summary>
    private (byte[] Buffer, List<CellRef> Refs) GatherCellsWithInsertion(byte[] pageBuf, int hdrOff,
        BTreePageHeader hdr, int insertIdx, ReadOnlySpan<byte> newCell)
    {
        var pageSpan = pageBuf.AsSpan();

        // Calculate total bytes needed
        int totalBytes = newCell.Length;
        for (int i = 0; i < hdr.CellCount; i++)
        {
            int cellPtr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            totalBytes += MeasureCell(pageSpan[cellPtr..], hdr.IsLeaf);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        _rentedBuffers.Add(buffer);
        var refs = new List<CellRef>(hdr.CellCount + 1);
        int writeOff = 0;

        for (int i = 0; i < insertIdx; i++)
        {
            int cellPtr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            int cellLen = MeasureCell(pageSpan[cellPtr..], hdr.IsLeaf);
            pageSpan.Slice(cellPtr, cellLen).CopyTo(buffer.AsSpan(writeOff));
            refs.Add(new CellRef(writeOff, cellLen));
            writeOff += cellLen;
        }

        newCell.CopyTo(buffer.AsSpan(writeOff));
        refs.Add(new CellRef(writeOff, newCell.Length));
        writeOff += newCell.Length;

        for (int i = insertIdx; i < hdr.CellCount; i++)
        {
            int cellPtr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            int cellLen = MeasureCell(pageSpan[cellPtr..], hdr.IsLeaf);
            pageSpan.Slice(cellPtr, cellLen).CopyTo(buffer.AsSpan(writeOff));
            refs.Add(new CellRef(writeOff, cellLen));
            writeOff += cellLen;
        }

        return (buffer, refs);
    }

    /// <summary>
    /// Measures the byte length of a cell starting at the given position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MeasureCell(ReadOnlySpan<byte> cellData, bool isLeaf)
    {
        if (isLeaf)
        {
            int off = CellParser.ParseTableLeafCell(cellData, out int payloadSize, out _);
            int inlineSize = CellParser.CalculateInlinePayloadSize(payloadSize, _usablePageSize);
            int total = off + inlineSize;
            if (inlineSize < payloadSize) total += 4; // overflow pointer
            return total;
        }
        else
        {
            return CellParser.ParseTableInteriorCell(cellData, out _, out _);
        }
    }

    /// <summary>Builds a leaf table page from a slice of cells in a contiguous buffer.</summary>
    private void BuildLeafPage(byte[] pageBuf, int hdrOff, byte[] cellBuf, List<CellRef> refs, int from, int to)
    {
        var span = pageBuf.AsSpan(0, _source.PageSize);
        // Clear only the B-Tree portion of the page to preserve any headers (e.g. on Page 1)
        span[hdrOff..].Clear();

        int cellCount = to - from + 1;
        if (cellCount <= 0) cellCount = 0;

        int contentEnd = _usablePageSize;
        int ptrBase = hdrOff + LeafHeaderSize;

        for (int i = from; i <= to && i < refs.Count; i++)
        {
            var cell = refs[i];
            contentEnd -= cell.Length;
            cellBuf.AsSpan(cell.Offset, cell.Length).CopyTo(span[contentEnd..]);

            int ptrIdx = i - from;
            BinaryPrimitives.WriteUInt16BigEndian(span[(ptrBase + ptrIdx * 2)..], (ushort)contentEnd);
        }

        var hdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0,
            (ushort)cellCount,
            (ushort)contentEnd,
            0, 0
        );
        BTreePageHeader.Write(span[hdrOff..], hdr);
    }

    /// <summary>Builds an interior table page from a slice of cells in a contiguous buffer.</summary>
    private void BuildInteriorPage(byte[] pageBuf, int hdrOff, byte[] cellBuf, List<CellRef> refs,
        int from, int to, uint rightChildPage)
    {
        var span = pageBuf.AsSpan(0, _source.PageSize);
        // Clear only the B-Tree portion of the page to preserve any headers (e.g. on Page 1)
        span[hdrOff..].Clear();

        int cellCount = to - from + 1;
        if (cellCount <= 0) cellCount = 0;

        int contentEnd = _usablePageSize;
        int ptrBase = hdrOff + InteriorHeaderSize;

        for (int i = from; i <= to && i < refs.Count; i++)
        {
            var cell = refs[i];
            contentEnd -= cell.Length;
            cellBuf.AsSpan(cell.Offset, cell.Length).CopyTo(span[contentEnd..]);

            int ptrIdx = i - from;
            BinaryPrimitives.WriteUInt16BigEndian(span[(ptrBase + ptrIdx * 2)..], (ushort)contentEnd);
        }

        var hdr = new BTreePageHeader(
            BTreePageType.InteriorTable, 0,
            (ushort)cellCount,
            (ushort)contentEnd,
            0,
            rightChildPage
        );
        BTreePageHeader.Write(span[hdrOff..], hdr);
    }

    // ── Delete helpers ──────────────────────────────────────────────

    /// <summary>
    /// Removes the cell at <paramref name="cellIndex"/> from a leaf page.
    /// Shifts the cell pointer array left and recomputes FragmentedFreeBytes accurately.
    /// </summary>
    private void RemoveCellFromPage(byte[] pageBuf, int hdrOff, BTreePageHeader hdr, int cellIndex)
    {
        var pageSpan = pageBuf.AsSpan();
        int headerSize = hdr.HeaderSize;
        int ptrBase = hdrOff + headerSize;

        for (int i = cellIndex; i < hdr.CellCount - 1; i++)
        {
            ushort ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + (i + 1) * 2)..]);
            BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + i * 2)..], ptr);
        }

        BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + (hdr.CellCount - 1) * 2)..], 0);

        int newCellCount = hdr.CellCount - 1;

        if (newCellCount == 0)
        {
            var emptyHdr = new BTreePageHeader(
                hdr.PageType, 0, 0, (ushort)_usablePageSize, 0, hdr.RightChildPage);
            BTreePageHeader.Write(pageSpan[hdrOff..], emptyHdr);
            return;
        }

        ushort newCellContentOffset = ushort.MaxValue;
        int totalCellBytes = 0;
        for (int i = 0; i < newCellCount; i++)
        {
            ushort ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + i * 2)..]);
            if (ptr < newCellContentOffset) newCellContentOffset = ptr;
            totalCellBytes += MeasureCell(pageSpan[ptr..], hdr.IsLeaf);
        }

        int cellContentAreaSize = _usablePageSize - newCellContentOffset;
        int newFragmented = cellContentAreaSize - totalCellBytes;

        if (newFragmented > 255)
        {
            int totalCellBytesForDefrag = 0;
            for (int i = 0; i < newCellCount; i++)
            {
                int ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + i * 2)..]);
                totalCellBytesForDefrag += MeasureCell(pageSpan[ptr..], hdr.IsLeaf);
            }

            var defragBuf = ArrayPool<byte>.Shared.Rent(totalCellBytesForDefrag);
            _rentedBuffers.Add(defragBuf);
            var defragRefs = new List<CellRef>(newCellCount);
            int writeOff = 0;

            for (int i = 0; i < newCellCount; i++)
            {
                int ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + i * 2)..]);
                int len = MeasureCell(pageSpan[ptr..], hdr.IsLeaf);
                pageSpan.Slice(ptr, len).CopyTo(defragBuf.AsSpan(writeOff));
                defragRefs.Add(new CellRef(writeOff, len));
                writeOff += len;
            }
            BuildLeafPage(pageBuf, hdrOff, defragBuf, defragRefs, 0, defragRefs.Count - 1);
            return;
        }

        var newHdr = new BTreePageHeader(
            hdr.PageType,
            hdr.FirstFreeblockOffset,
            (ushort)newCellCount,
            newCellContentOffset,
            (byte)newFragmented,
            hdr.RightChildPage
        );
        BTreePageHeader.Write(pageSpan[hdrOff..], newHdr);
    }

    /// <summary>
    /// Compacts all cells on a page to recover fragmented free space.
    /// </summary>
    private void DefragmentPage(byte[] pageBuf, int hdrOff, BTreePageHeader hdr)
    {
        var pageSpan = pageBuf.AsSpan();
        int headerSize = hdr.HeaderSize;
        int ptrBase = hdrOff + headerSize;

        // Measure total cell bytes and collect descriptors
        int totalCellBytes = 0;
        for (int i = 0; i < hdr.CellCount; i++)
        {
            int ptr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            totalCellBytes += MeasureCell(pageSpan[ptr..], hdr.IsLeaf);
        }

        var defragBuf = ArrayPool<byte>.Shared.Rent(totalCellBytes);
        _rentedBuffers.Add(defragBuf);
        int writeOff = 0;
        var offsets = new (int Offset, int Length)[hdr.CellCount];

        for (int i = 0; i < hdr.CellCount; i++)
        {
            int ptr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            int len = MeasureCell(pageSpan[ptr..], hdr.IsLeaf);
            pageSpan.Slice(ptr, len).CopyTo(defragBuf.AsSpan(writeOff));
            offsets[i] = (writeOff, len);
            writeOff += len;
        }

        int contentEnd = _usablePageSize;
        for (int i = 0; i < offsets.Length; i++)
        {
            var (off, len) = offsets[i];
            contentEnd -= len;
            defragBuf.AsSpan(off, len).CopyTo(pageSpan[contentEnd..]);
            BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + i * 2)..], (ushort)contentEnd);
        }

        var newHdr = new BTreePageHeader(
            hdr.PageType,
            0,
            hdr.CellCount,
            (ushort)contentEnd,
            0,
            hdr.RightChildPage
        );
        BTreePageHeader.Write(pageSpan[hdrOff..], newHdr);
    }

    // ── I/O helpers ────────────────────────────────────────────────

    private byte[] ReadPageBuffer(uint pageNumber)
    {
        if (_pageCache.TryGetValue(pageNumber, out var cached))
            return cached;

        var buf = RentPageBuffer();
        _source.ReadPage(pageNumber, buf.AsSpan(0, _source.PageSize));
        _pageCache[pageNumber] = buf;
        return buf;
    }

    private void WritePageBuffer(uint pageNumber, byte[] buffer)
    {
        _source.WritePage(pageNumber, buffer.AsSpan(0, _source.PageSize));
        _pageCache[pageNumber] = buffer;
    }

    /// <summary>Rents a page-sized buffer from the pool, clears it, and tracks it for return on Dispose.</summary>
    private byte[] RentPageBuffer()
    {
        var buf = ArrayPool<byte>.Shared.Rent(_source.PageSize);
        buf.AsSpan(0, _source.PageSize).Clear();
        _rentedBuffers.Add(buf);
        return buf;
    }

    private uint _nextAllocPage;

    internal uint AllocateNewPage()
    {
        // Try reusing a free page from the freelist first
        uint page = _freePageAllocator?.Invoke() ?? 0;

        if (page == 0)
        {
            // No free page available — extend the file
            if (_nextAllocPage == 0)
            {
                _nextAllocPage = (uint)_source.PageCount + 1;
            }
            page = _nextAllocPage++;
        }

        var buf = RentPageBuffer();
        var hdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0, 0,
            (ushort)_usablePageSize, 0, 0
        );
        BTreePageHeader.Write(buf, hdr);
        _pageCache[page] = buf;
        WritePageBuffer(page, buf);

        return page;
    }
}
