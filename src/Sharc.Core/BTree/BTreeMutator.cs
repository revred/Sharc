/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sharc.Core.Format;
using Sharc.Core.Primitives;

namespace Sharc.Core.BTree;

/// <summary>
/// Insert/update/delete operations on a table B-tree.
/// Mirrors <see cref="BTreeCursor"/> for navigation,
/// but modifies pages via <see cref="IWritablePageSource"/>.
/// </summary>
internal sealed class BTreeMutator
{
    private readonly IWritablePageSource _source;
    private readonly int _usablePageSize;

    /// <summary>Header size for a leaf table page.</summary>
    private const int LeafHeaderSize = SQLiteLayout.TableLeafHeaderSize;   // 8
    /// <summary>Header size for an interior table page.</summary>
    private const int InteriorHeaderSize = SQLiteLayout.TableInteriorHeaderSize; // 12

    public BTreeMutator(IWritablePageSource source, int usablePageSize)
    {
        _source = source;
        _usablePageSize = usablePageSize;
    }

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>
    /// Inserts a record into the table B-tree rooted at <paramref name="rootPage"/>.
    /// Returns the (possibly new) root page number — the root changes when the root page splits.
    /// </summary>
    public uint Insert(uint rootPage, long rowId, ReadOnlySpan<byte> recordPayload)
    {
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
            int hdrOff = currentPage == 1 ? 100 : 0;
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
    /// Returns the maximum rowid in the table B-tree, or 0 if the tree is empty.
    /// </summary>
    public long GetMaxRowId(uint rootPage)
    {
        uint pageNum = rootPage;
        while (true)
        {
            var page = ReadPageBuffer(pageNum);
            int hdrOff = pageNum == 1 ? 100 : 0;
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

    // ── Navigation helpers ─────────────────────────────────────────

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
        int hdrOff = pageNum == 1 ? 100 : 0;
        var hdr = BTreePageHeader.Parse(pageBuf.AsSpan(hdrOff));

        // Try to insert into existing page
        if (TryInsertCell(pageBuf, hdrOff, hdr, insertIdx, cellBytes))
        {
            WritePageBuffer(pageNum, pageBuf);
            return currentRoot;
        }

        // ── Page is full — split ──────────────────────────────────

        // Gather all existing cells + the new cell into a sorted list
        var allCells = GatherCellsWithInsertion(pageBuf, hdrOff, hdr, insertIdx, cellBytes);

        // Split point: divide roughly in half by total byte count
        int totalBytes = 0;
        foreach (var c in allCells) totalBytes += c.Length;
        int halfTarget = totalBytes / 2;

        int splitIdx = 0;
        int runningBytes = 0;
        for (int i = 0; i < allCells.Count; i++)
        {
            runningBytes += allCells[i].Length;
            if (runningBytes >= halfTarget) { splitIdx = i; break; }
        }

        // Ensure split leaves at least 1 cell on each side
        if (splitIdx == 0) splitIdx = 1;
        if (splitIdx >= allCells.Count - 1) splitIdx = allCells.Count - 2;

        // For leaf pages: left gets [0..splitIdx], right gets [splitIdx+1..end]
        // The median (splitIdx) is promoted to parent
        bool isLeaf = hdr.IsLeaf;

        // Parse the median cell to get the promoted rowId
        long medianRowId;
        if (isLeaf)
        {
            CellParser.ParseTableLeafCell(allCells[splitIdx], out _, out medianRowId);
        }
        else
        {
            CellParser.ParseTableInteriorCell(allCells[splitIdx], out _, out medianRowId);
        }

        // Allocate a new page for the right sibling
        uint newPageNum = AllocateNewPage();

        // Build left page (reuse existing page buffer)
        if (isLeaf)
        {
            // Left gets [0..splitIdx] (inclusive — leaf keeps the median too)
            BuildLeafPage(pageBuf, hdrOff, allCells, 0, splitIdx);
            WritePageBuffer(pageNum, pageBuf);

            // Right gets [splitIdx+1..end]
            var rightBuf = new byte[_source.PageSize];
            BuildLeafPage(rightBuf, 0, allCells, splitIdx + 1, allCells.Count - 1);
            WritePageBuffer(newPageNum, rightBuf);
        }
        else
        {
            // Interior split: left gets [0..splitIdx-1], median promoted, right gets [splitIdx+1..end]
            // The median's left-child becomes the new right page's leftmost child... complex.
            // For simplicity, left keeps cells [0..splitIdx-1], right gets [splitIdx+1..end]
            // The right-child-page of left page = left-child of median cell
            // The right-child-page of right page = original right-child or from last cell

            CellParser.ParseTableInteriorCell(allCells[splitIdx], out uint medianLeftChild, out _);

            // Left page: cells [0..splitIdx-1], rightChild = medianLeftChild
            BuildInteriorPage(pageBuf, hdrOff, allCells, 0, splitIdx - 1, medianLeftChild);
            WritePageBuffer(pageNum, pageBuf);

            // Right page: cells [splitIdx+1..end], rightChild = original rightChild
            var rightBuf = new byte[_source.PageSize];
            uint originalRightChild = hdr.RightChildPage;
            BuildInteriorPage(rightBuf, 0, allCells, splitIdx + 1, allCells.Count - 1, originalRightChild);
            WritePageBuffer(newPageNum, rightBuf);
        }



        if (pathIndex == 0)
        {
            // Root split with retention:
            // The root (pageNum) is full. instead of allocating a new root and returning it (moving the root),
            // we allocate a NEW left child (newLeftPage) and move the left-half content there.
            // The right-half content goes to newNewPage (allocated above at line 220, let's call it newRightPage).
            // The root page is then CLEARED and rewritten as an Interior Node containing the median and pointers to newLeft and newRight.

            // 1. We already have 'newPageNum' (allocated at line 220) which is currently holding the "Right" half.
            //    BUT, the earlier logic (lines 222-253) overwrote 'pageNum' (the root) with the LEFT half.
            //    We need to move that left half to a new page.

            // Allocate a new page for the Left Half
            uint newLeftPage = AllocateNewPage();

            // Copy the content of 'pageNum' (which currently holds TableLeaf[Left]) to 'newLeftPage'
            var leftContent = ReadPageBuffer(pageNum);
            WritePageBuffer(newLeftPage, leftContent);

            // 2. Now 'pageNum' (Root) can be overwritten as the new Interior Root.
            //    It needs to point to newLeftPage (Left Child) and newPageNum (Right Child).
            //    And contain the Median Key.

            var rootBuf = new byte[_source.PageSize];

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

            int rootHdrOff = pageNum == 1 ? 100 : 0;
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

            // The parent must update: the child pointer that used to point to pageNum 
            // now the new cell's left-child = pageNum, and the right side should point to newPageNum.
            // We need to update the parent's right-child or the next cell's left-child.
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
        int hdrOff = parentPageNum == 1 ? 100 : 0;
        var hdr = BTreePageHeader.Parse(parentBuf.AsSpan(hdrOff));

        // If the descent was through the right-child pointer (cellIdx == cellCount),
        // update the right-child to point to newRightChild
        if (parentCellIdx >= hdr.CellCount)
        {
            // The right-child of parent should now be newRightChild
            BinaryPrimitives.WriteUInt32BigEndian(
                parentBuf.AsSpan(hdrOff + SQLiteLayout.RightChildPageOffset), newRightChild);
            WritePageBuffer(parentPageNum, parentBuf);
        }
        else
        {
            // The promoted cell's left-child already points to the old (left) page.
            // We need to make the cell at parentCellIdx+1 (or right-child) point to newRightChild.
            // This is handled by inserting the promoted cell at parentCellIdx position, 
            // which naturally pushes other cells right. The old cell at parentCellIdx 
            // already has the correct left-child for the keys beyond the median.
            // Actually, we need to update the path so the promoted cell gets inserted
            // at the right position with the new right child as the next pointer.

            // Update the cell at parentCellIdx: its left-child should be newRightChild
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
        int cellPtrArrayEnd = hdrOff + headerSize + (hdr.CellCount + 1) * 2; // need room for one more pointer
        int cellContentStart = hdr.CellContentOffset == 0 ? _usablePageSize : hdr.CellContentOffset;

        // Check if there's enough space: cell bytes + 2-byte pointer
        int requiredSpace = cellBytes.Length + 2;
        int availableSpace = cellContentStart - cellPtrArrayEnd;

        if (availableSpace < requiredSpace)
            return false;

        // Write cell content at the bottom of the free area
        int newCellOffset = cellContentStart - cellBytes.Length;
        cellBytes.CopyTo(pageBuf.AsSpan(newCellOffset));

        // Shift cell pointers to make room at insertIdx
        var pageSpan = pageBuf.AsSpan();
        int ptrBase = hdrOff + headerSize;
        // Shift pointers [insertIdx..cellCount-1] right by 2 bytes
        for (int i = hdr.CellCount - 1; i >= insertIdx; i--)
        {
            ushort ptr = BinaryPrimitives.ReadUInt16BigEndian(pageSpan[(ptrBase + i * 2)..]);
            BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + (i + 1) * 2)..], ptr);
        }

        // Write new cell pointer
        BinaryPrimitives.WriteUInt16BigEndian(pageSpan[(ptrBase + insertIdx * 2)..], (ushort)newCellOffset);

        // Update header
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
    /// </summary>
    private List<byte[]> GatherCellsWithInsertion(byte[] pageBuf, int hdrOff,
        BTreePageHeader hdr, int insertIdx, ReadOnlySpan<byte> newCell)
    {
        var cells = new List<byte[]>(hdr.CellCount + 1);
        var pageSpan = pageBuf.AsSpan();

        // 1. Existing items before insertion point
        for (int i = 0; i < insertIdx; i++)
        {
            int cellPtr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            int cellLen = MeasureCell(pageSpan[cellPtr..], hdr.IsLeaf);
            cells.Add(pageSpan.Slice(cellPtr, cellLen).ToArray());
        }

        // 2. The new item
        cells.Add(newCell.ToArray());

        // 3. Existing items after insertion point
        for (int i = insertIdx; i < hdr.CellCount; i++)
        {
            int cellPtr = hdr.GetCellPointer(pageSpan[hdrOff..], i);
            int cellLen = MeasureCell(pageSpan[cellPtr..], hdr.IsLeaf);
            cells.Add(pageSpan.Slice(cellPtr, cellLen).ToArray());
        }

        return cells;
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

    /// <summary>Builds a leaf table page from a slice of cells.</summary>
    private void BuildLeafPage(byte[] pageBuf, int hdrOff, List<byte[]> cells, int from, int to)
    {
        var span = pageBuf.AsSpan();
        span.Clear();

        int cellCount = to - from + 1;
        if (cellCount <= 0) cellCount = 0;

        int contentEnd = _usablePageSize;
        int ptrBase = hdrOff + LeafHeaderSize;

        for (int i = from; i <= to && i < cells.Count; i++)
        {
            var cell = cells[i];
            contentEnd -= cell.Length;
            cell.CopyTo(span[contentEnd..]);

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

    /// <summary>Builds an interior table page from a slice of cells.</summary>
    private void BuildInteriorPage(byte[] pageBuf, int hdrOff, List<byte[]> cells,
        int from, int to, uint rightChildPage)
    {
        var span = pageBuf.AsSpan();
        span.Clear();

        int cellCount = to - from + 1;
        if (cellCount <= 0) cellCount = 0;

        int contentEnd = _usablePageSize;
        int ptrBase = hdrOff + InteriorHeaderSize;

        for (int i = from; i <= to && i < cells.Count; i++)
        {
            var cell = cells[i];
            contentEnd -= cell.Length;
            cell.CopyTo(span[contentEnd..]);

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

    // ── I/O helpers ────────────────────────────────────────────────

    private byte[] ReadPageBuffer(uint pageNumber)
    {
        var buf = new byte[_source.PageSize];
        _source.ReadPage(pageNumber, buf);
        return buf;
    }

    private void WritePageBuffer(uint pageNumber, byte[] buffer)
    {
        _source.WritePage(pageNumber, buffer);
    }

    private uint _nextAllocPage;

    private uint AllocateNewPage()
    {
        if (_nextAllocPage == 0)
        {
            _nextAllocPage = (uint)_source.PageCount + 1;
        }
        uint page = _nextAllocPage++;

        // Initialize the new page as an empty leaf
        var buf = new byte[_source.PageSize];
        var hdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0, 0,
            (ushort)_usablePageSize, 0, 0
        );
        BTreePageHeader.Write(buf, hdr);
        WritePageBuffer(page, buf);

        return page;
    }
}
