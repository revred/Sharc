// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using Sharc.Core.Format;
using Sharc.Core.Primitives;
using Sharc.Core.Records;

namespace Sharc.Core.BTree;

/// <summary>
/// Populates an index B-tree from an existing table's rows using sorted bulk-insert.
/// Scans all table rows, extracts indexed columns + trailing rowid, sorts by record payload,
/// and builds the index B-tree bottom-up. This avoids modifying <see cref="BTreeMutator"/>'s
/// navigation and split logic, which is hardcoded for table (rowid-based) B-trees.
/// </summary>
internal sealed class IndexBTreePopulator : IDisposable
{
    private readonly BTreeMutator _mutator;
    private readonly int _usablePageSize;
    private readonly int _pageSize;
    private readonly IWritablePageSource _source;
    private readonly List<byte[]> _rentedBuffers = new(4);

    /// <summary>Index leaf page header is 8 bytes (same as table leaf).</summary>
    private const int LeafHeaderSize = SQLiteLayout.TableLeafHeaderSize; // 8
    /// <summary>Index interior page header is 12 bytes (same as table interior).</summary>
    private const int InteriorHeaderSize = SQLiteLayout.TableInteriorHeaderSize; // 12

    public IndexBTreePopulator(BTreeMutator mutator, int usablePageSize, IWritablePageSource source)
    {
        _mutator = mutator;
        _usablePageSize = usablePageSize;
        _pageSize = source.PageSize;
        _source = source;
    }

    /// <summary>
    /// Populates the index B-tree rooted at <paramref name="indexRootPage"/> from the table
    /// rooted at <paramref name="tableRootPage"/>.
    /// </summary>
    /// <param name="indexRootPage">The already-allocated index root page (LeafIndex 0x0A).</param>
    /// <param name="tableRootPage">The root page of the source table.</param>
    /// <param name="columnOrdinals">Ordinals of the indexed columns in the table.</param>
    /// <param name="bTreeReader">B-tree reader for scanning the table.</param>
    /// <param name="recordDecoder">Decoder for reading table records.</param>
    public void PopulateIndex(uint indexRootPage, uint tableRootPage, int[] columnOrdinals,
        IBTreeReader bTreeReader, IRecordDecoder recordDecoder)
    {
        // Step 1: Collect all index entries from the table
        var entries = CollectIndexEntries(tableRootPage, columnOrdinals, bTreeReader, recordDecoder);
        if (entries.Count == 0)
            return; // Empty table — nothing to do

        // Step 2: Sort entries by decoded record values (SQLite type ordering + BINARY collation)
        entries.Sort(static (a, b) => CompareIndexRecords(a.AsSpan(), b.AsSpan()));

        // Step 3: Build index B-tree bottom-up
        BuildIndexBTree(indexRootPage, entries);
    }

    /// <summary>
    /// Scans the table and collects encoded index record payloads.
    /// Each entry is a complete SQLite record: [indexed columns..., rowid].
    /// </summary>
    private static List<byte[]> CollectIndexEntries(uint tableRootPage, int[] columnOrdinals,
        IBTreeReader bTreeReader, IRecordDecoder recordDecoder)
    {
        var entries = new List<byte[]>();
        using var cursor = bTreeReader.CreateCursor(tableRootPage);

        while (cursor.MoveNext())
        {
            var tableRecord = recordDecoder.DecodeRecord(cursor.Payload);
            long rowId = cursor.RowId;

            // Build index record: [indexed columns..., rowid]
            var indexValues = new ColumnValue[columnOrdinals.Length + 1];
            for (int i = 0; i < columnOrdinals.Length; i++)
                indexValues[i] = tableRecord[columnOrdinals[i]];
            indexValues[columnOrdinals.Length] = ColumnValue.FromInt64(
                SerialTypeCodec.GetSerialType(ColumnValue.FromInt64(6, rowId)), rowId);

            // Encode to SQLite record format
            int recordSize = RecordEncoder.ComputeEncodedSize(indexValues);
            var recordBytes = new byte[recordSize];
            RecordEncoder.EncodeRecord(indexValues, recordBytes);
            entries.Add(recordBytes);
        }

        return entries;
    }

    /// <summary>
    /// Builds the index B-tree from sorted entries. Packs cells into leaf pages left-to-right,
    /// then builds interior levels if needed.
    /// </summary>
    private void BuildIndexBTree(uint indexRootPage, List<byte[]> sortedEntries)
    {
        // Build leaf cells from sorted entries
        var leafCells = new List<byte[]>(sortedEntries.Count);
        foreach (var recordPayload in sortedEntries)
        {
            int cellSize = CellBuilder.ComputeIndexLeafCellSize(recordPayload.Length, _usablePageSize);
            var cellBytes = new byte[cellSize];
            CellBuilder.BuildIndexLeafCell(recordPayload, cellBytes, _usablePageSize);
            leafCells.Add(cellBytes);
        }

        // Try to fit everything in the root page
        int totalCellBytes = 0;
        int totalCellPointers = leafCells.Count * 2;
        foreach (var cell in leafCells)
            totalCellBytes += cell.Length;

        int rootHdrOff = indexRootPage == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
        int availableOnRoot = _usablePageSize - rootHdrOff - LeafHeaderSize - totalCellPointers;

        if (totalCellBytes <= availableOnRoot)
        {
            // Everything fits in one leaf page (the root)
            WriteLeafPage(indexRootPage, rootHdrOff, leafCells, 0, leafCells.Count);
            return;
        }

        // Multiple leaves needed — pack into leaf pages and collect (page, firstRecordPayload)
        var leafPages = new List<(uint page, byte[] firstRecordPayload)>();
        PackLeafPages(indexRootPage, rootHdrOff, leafCells, sortedEntries, leafPages);

        // Build interior levels
        BuildInteriorLevels(indexRootPage, rootHdrOff, leafPages, sortedEntries);
    }

    /// <summary>
    /// Packs sorted leaf cells into multiple leaf pages. The root page is NOT used as a leaf
    /// when there are multiple pages — it will become an interior page.
    /// </summary>
    private void PackLeafPages(uint indexRootPage, int rootHdrOff,
        List<byte[]> leafCells, List<byte[]> sortedEntries,
        List<(uint page, byte[] firstRecordPayload)> leafPages)
    {
        int startIdx = 0;

        while (startIdx < leafCells.Count)
        {
            // Allocate a new leaf page (not the root — root will be interior)
            uint leafPage = _mutator.AllocateNewPage(BTreePageType.LeafIndex);

            // Calculate how many cells fit on this page
            int hdrOff = 0; // non-root pages always have hdrOff=0
            int available = _usablePageSize - LeafHeaderSize;
            int runningSize = 0;
            int endIdx = startIdx;

            while (endIdx < leafCells.Count)
            {
                int cellSize = leafCells[endIdx].Length + 2; // +2 for cell pointer
                if (runningSize + cellSize > available)
                    break;
                runningSize += cellSize;
                endIdx++;
            }

            // Must make progress
            if (endIdx == startIdx)
                endIdx = startIdx + 1;

            WriteLeafPage(leafPage, hdrOff, leafCells, startIdx, endIdx);
            leafPages.Add((leafPage, sortedEntries[startIdx]));
            startIdx = endIdx;
        }
    }

    /// <summary>
    /// Builds interior level(s) on top of leaf pages. If all separator keys fit on the root,
    /// writes a single interior root page. Otherwise, builds multiple levels.
    /// </summary>
    private void BuildInteriorLevels(uint indexRootPage, int rootHdrOff,
        List<(uint page, byte[] firstRecordPayload)> childPages,
        List<byte[]> sortedEntries)
    {
        // The interior cells use the first record payload of each child (except the first child,
        // which becomes a left pointer without its own cell). The rightmost child is the right-child
        // pointer in the page header.
        while (childPages.Count > 1)
        {
            // Build interior cells: for each child except the first, create an interior cell
            // with leftChild pointing to the previous child's page.
            var interiorCells = new List<(byte[] cellBytes, byte[] recordPayload, uint leftChild)>();
            for (int i = 1; i < childPages.Count; i++)
            {
                var recordPayload = childPages[i].firstRecordPayload;
                int cellSize = CellBuilder.ComputeIndexInteriorCellSize(recordPayload.Length, _usablePageSize);
                var cellBytes = new byte[cellSize];
                CellBuilder.BuildIndexInteriorCell(childPages[i - 1].page, recordPayload, cellBytes, _usablePageSize);
                interiorCells.Add((cellBytes, recordPayload, childPages[i - 1].page));
            }

            uint rightChild = childPages[^1].page;

            // Check if all interior cells fit on the root page
            int totalInteriorBytes = 0;
            int totalInteriorPointers = interiorCells.Count * 2;
            foreach (var (cellBytes, _, _) in interiorCells)
                totalInteriorBytes += cellBytes.Length;

            int availableOnRoot = _usablePageSize - rootHdrOff - InteriorHeaderSize - totalInteriorPointers;

            if (totalInteriorBytes <= availableOnRoot)
            {
                // Write the root as an interior page
                WriteInteriorPage(indexRootPage, rootHdrOff, interiorCells, rightChild);
                return;
            }

            // Need more interior levels — pack into interior pages
            var nextLevelPages = new List<(uint page, byte[] firstRecordPayload)>();
            int startIdx = 0;

            while (startIdx < interiorCells.Count)
            {
                uint interiorPage = _mutator.AllocateNewPage(BTreePageType.InteriorIndex);
                int hdrOff = 0;
                int available = _usablePageSize - InteriorHeaderSize;
                int runningSize = 0;
                int endIdx = startIdx;

                while (endIdx < interiorCells.Count)
                {
                    int cellSize = interiorCells[endIdx].cellBytes.Length + 2;
                    if (runningSize + cellSize > available)
                        break;
                    runningSize += cellSize;
                    endIdx++;
                }

                if (endIdx == startIdx)
                    endIdx = startIdx + 1;

                // The right-child for this interior page is the page of the last cell's
                // child (or the overall right child if this is the last chunk)
                uint pageRightChild = endIdx < interiorCells.Count
                    ? interiorCells[endIdx].leftChild
                    : rightChild;

                // But wait — the last cell in this chunk points to its left child,
                // and the right child should be the NEXT page in sequence.
                // Actually, for interior cells: cell[i] has leftChild = childPages[i].page
                // and the separator key for childPages[i+1]. The right child of this interior
                // page should be the page that the last cell's separator key leads to.
                // This is the page at endIdx (or rightChild if endIdx == count).
                if (endIdx < interiorCells.Count)
                    pageRightChild = interiorCells[endIdx].leftChild;
                else
                    pageRightChild = rightChild;

                WriteInteriorPage(interiorPage, hdrOff,
                    interiorCells.GetRange(startIdx, endIdx - startIdx), pageRightChild);
                nextLevelPages.Add((interiorPage, interiorCells[startIdx].recordPayload));
                startIdx = endIdx;
            }

            childPages = nextLevelPages;
        }

        // Only one child page remains — the root page should contain the single interior level
        // This case shouldn't happen if we entered the loop, but handle it gracefully:
        // Copy the child page content to the root
        if (childPages.Count == 1)
        {
            var childBuf = RentPageBuffer();
            _source.ReadPage(childPages[0].page, childBuf.AsSpan(0, _pageSize));
            childBuf.AsSpan(0, _pageSize).CopyTo(RentPageBuffer().AsSpan());
            // This path is unexpected — log or handle
        }
    }

    /// <summary>
    /// Writes a set of leaf cells to a single leaf page.
    /// </summary>
    private void WriteLeafPage(uint pageNum, int hdrOff, List<byte[]> cells, int startIdx, int endIdx)
    {
        int cellCount = endIdx - startIdx;

        // Assemble all cells into a contiguous buffer
        int totalCellBytes = 0;
        for (int i = startIdx; i < endIdx; i++)
            totalCellBytes += cells[i].Length;

        var cellBuf = ArrayPool<byte>.Shared.Rent(totalCellBytes);
        _rentedBuffers.Add(cellBuf);
        var cellRefs = new BTreePageRewriter.CellRef[cellCount];

        int writeOff = 0;
        for (int i = startIdx; i < endIdx; i++)
        {
            var cell = cells[i];
            cell.CopyTo(cellBuf.AsSpan(writeOff));
            cellRefs[i - startIdx] = new BTreePageRewriter.CellRef(writeOff, cell.Length);
            writeOff += cell.Length;
        }

        var pageBuf = RentPageBuffer();
        // Preserve database header if writing to page 1
        if (pageNum == 1)
        {
            var existing = RentPageBuffer();
            _source.ReadPage(pageNum, existing.AsSpan(0, _pageSize));
            existing.AsSpan(0, SQLiteLayout.DatabaseHeaderSize).CopyTo(pageBuf);
        }

        var rewriter = new BTreePageRewriter(_source, _usablePageSize, _rentedBuffers);
        rewriter.BuildIndexLeafPage(pageBuf, hdrOff, cellBuf, cellRefs);
        _source.WritePage(pageNum, pageBuf.AsSpan(0, _pageSize));
    }

    /// <summary>
    /// Writes a set of interior cells to a single interior page.
    /// </summary>
    private void WriteInteriorPage(uint pageNum, int hdrOff,
        List<(byte[] cellBytes, byte[] recordPayload, uint leftChild)> cells, uint rightChild)
    {
        int totalCellBytes = 0;
        foreach (var (cellBytes, _, _) in cells)
            totalCellBytes += cellBytes.Length;

        var cellBuf = ArrayPool<byte>.Shared.Rent(totalCellBytes);
        _rentedBuffers.Add(cellBuf);
        var cellRefs = new BTreePageRewriter.CellRef[cells.Count];

        int writeOff = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i].cellBytes;
            cell.CopyTo(cellBuf.AsSpan(writeOff));
            cellRefs[i] = new BTreePageRewriter.CellRef(writeOff, cell.Length);
            writeOff += cell.Length;
        }

        var pageBuf = RentPageBuffer();
        if (pageNum == 1)
        {
            var existing = RentPageBuffer();
            _source.ReadPage(pageNum, existing.AsSpan(0, _pageSize));
            existing.AsSpan(0, SQLiteLayout.DatabaseHeaderSize).CopyTo(pageBuf);
        }

        var rewriter = new BTreePageRewriter(_source, _usablePageSize, _rentedBuffers);
        rewriter.BuildIndexInteriorPage(pageBuf, hdrOff, cellBuf, cellRefs, rightChild);
        _source.WritePage(pageNum, pageBuf.AsSpan(0, _pageSize));
    }

    private byte[] RentPageBuffer()
    {
        var buf = ArrayPool<byte>.Shared.Rent(_pageSize);
        buf.AsSpan(0, _pageSize).Clear();
        _rentedBuffers.Add(buf);
        return buf;
    }

    /// <summary>
    /// Compares two SQLite index record payloads field by field using SQLite type ordering:
    /// NULL &lt; INTEGER/REAL &lt; TEXT &lt; BLOB, with BINARY collation for text/blob.
    /// The trailing rowid column acts as a tiebreaker.
    /// </summary>
    private static int CompareIndexRecords(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        // Parse headers to get serial types
        int aOff = VarintDecoder.Read(a, out long aHeaderSize);
        int bOff = VarintDecoder.Read(b, out long bHeaderSize);

        int aHeaderEnd = (int)aHeaderSize;
        int bHeaderEnd = (int)bHeaderSize;
        int aBodyPos = aHeaderEnd;
        int bBodyPos = bHeaderEnd;

        while (aOff < aHeaderEnd && bOff < bHeaderEnd)
        {
            aOff += VarintDecoder.Read(a[aOff..], out long aSt);
            bOff += VarintDecoder.Read(b[bOff..], out long bSt);

            int aContentSize = SerialTypeCodec.GetContentSize(aSt);
            int bContentSize = SerialTypeCodec.GetContentSize(bSt);

            int cmp = CompareValues(a[aBodyPos..], aSt, b[bBodyPos..], bSt);
            if (cmp != 0) return cmp;

            aBodyPos += aContentSize;
            bBodyPos += bContentSize;
        }

        // If one has more columns, the shorter record sorts first
        return (aHeaderEnd - aOff).CompareTo(bHeaderEnd - bOff);
    }

    /// <summary>
    /// Compares two individual column values by their serial types and body bytes.
    /// SQLite ordering: NULL(0) &lt; INT(1-6,8,9) &lt; REAL(7) &lt; TEXT(odd>=13) &lt; BLOB(even>=12).
    /// </summary>
    private static int CompareValues(ReadOnlySpan<byte> aBody, long aSt, ReadOnlySpan<byte> bBody, long bSt)
    {
        int aClass = GetTypeClass(aSt);
        int bClass = GetTypeClass(bSt);

        if (aClass != bClass)
            return aClass.CompareTo(bClass);

        // Same type class — compare values
        return aClass switch
        {
            0 => 0, // NULL == NULL
            1 => DecodeNumeric(aBody, aSt).CompareTo(DecodeNumeric(bBody, bSt)), // INT/REAL
            2 => CompareBlobs(aBody, aSt, bBody, bSt), // TEXT (BINARY collation = byte compare)
            3 => CompareBlobs(aBody, aSt, bBody, bSt), // BLOB
            _ => 0
        };
    }

    /// <summary>Returns type class: 0=NULL, 1=numeric, 2=text, 3=blob.</summary>
    private static int GetTypeClass(long serialType) => serialType switch
    {
        0 => 0,                                      // NULL
        >= 1 and <= 6 or 8 or 9 => 1,               // INTEGER
        7 => 1,                                       // REAL (same class as integer for ordering)
        >= 12 when (serialType & 1) == 0 => 3,       // BLOB
        >= 13 when (serialType & 1) == 1 => 2,       // TEXT
        _ => 0
    };

    private static double DecodeNumeric(ReadOnlySpan<byte> body, long st) => st switch
    {
        0 => 0,
        1 => (sbyte)body[0],
        2 => System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(body),
        3 => (((body[0] << 16) | (body[1] << 8) | body[2]) | (((body[0] & 0x80) != 0) ? unchecked((int)0xFF000000) : 0)),
        4 => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body),
        5 => ExtractInt48(body),
        6 => System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(body),
        7 => System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(body),
        8 => 0,
        9 => 1,
        _ => 0
    };

    private static long ExtractInt48(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8) | data[5];
        if ((raw & 0x800000000000L) != 0)
            raw |= unchecked((long)0xFFFF000000000000L);
        return raw;
    }

    private static int CompareBlobs(ReadOnlySpan<byte> aBody, long aSt, ReadOnlySpan<byte> bBody, long bSt)
    {
        int aLen = SerialTypeCodec.GetContentSize(aSt);
        int bLen = SerialTypeCodec.GetContentSize(bSt);
        return aBody[..aLen].SequenceCompareTo(bBody[..bLen]);
    }

    public void Dispose()
    {
        foreach (var buf in _rentedBuffers)
            ArrayPool<byte>.Shared.Return(buf);
        _rentedBuffers.Clear();
    }
}
