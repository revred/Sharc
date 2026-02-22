// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Exceptions;

namespace Sharc.Core.BTree;

/// <summary>
/// Scan-optimized cursor that pre-collects all leaf page numbers via a single DFS
/// through interior pages, then iterates cells in a flat loop without B-tree stack
/// navigation. Faster than <see cref="BTreeCursor{TPageSource}"/> for full table scans because
/// leaf-to-leaf transitions are a simple array index bump instead of stack-based
/// interior page re-navigation.
/// <para>
/// Does not support <see cref="Seek"/> or <see cref="MoveLast"/> (scan-only by design).
/// </para>
/// </summary>
internal sealed class LeafPageScanner<TPageSource> : IBTreeCursor
    where TPageSource : class, IPageSource
{
    private readonly TPageSource _pageSource;
    private readonly int _usablePageSize;
    private readonly uint[] _leafPages;

    // Current leaf state
    private int _leafIndex;
    private int _leafHeaderOffset;
    private BTreePageHeader _leafHeader;
    private int _cellIndex;
    private bool _exhausted;
    private bool _disposed;

    // Current cell data
    private long _rowId;
    private int _payloadSize;
    private byte[]? _assembledPayload;
    private int _inlinePayloadOffset;

    // Leaf page cache — avoids redundant GetPage() calls for cells on the same leaf
    private ReadOnlyMemory<byte> _cachedLeafMemory;
    private uint _cachedLeafPageNum;

    // Reusable overflow cycle detection set
    private HashSet<uint>? _visitedOverflowPages;

    // Staleness tracking
    private readonly IWritablePageSource? _writableSource;
    private long _snapshotVersion;

    public LeafPageScanner(TPageSource pageSource, uint rootPage, int usablePageSize)
    {
        _pageSource = pageSource;
        _usablePageSize = usablePageSize;
        _writableSource = pageSource as IWritablePageSource;
        _leafPages = CollectLeafPages(pageSource, rootPage);
        _leafIndex = 0;
        _cellIndex = -1;

        if (_leafPages.Length > 0)
            LoadLeafPage(0);
        else
            _exhausted = true;
    }

    /// <inheritdoc />
    public long RowId => _rowId;

    /// <inheritdoc />
    public int PayloadSize => _payloadSize;

    /// <inheritdoc />
    public bool IsStale
    {
        get
        {
            if (_writableSource is null) return false;
            long current = _writableSource.DataVersion;
            if (_snapshotVersion == 0)
            {
                _snapshotVersion = current;
                return false;
            }
            return current != _snapshotVersion;
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> Payload
    {
        get
        {
            if (_assembledPayload != null)
                return _assembledPayload.AsSpan(0, _payloadSize);

            // Return inline payload from cached leaf page
            return GetCachedLeafPage().Slice(_inlinePayloadOffset, _payloadSize);
        }
    }

    /// <inheritdoc />
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReturnAssembledPayload();

        if (_snapshotVersion == 0)
            _snapshotVersion = _writableSource?.DataVersion ?? 0;

        if (_exhausted)
            return false;

        _cellIndex++;

        // Fast path: next cell in current leaf
        if (_cellIndex < _leafHeader.CellCount)
        {
            ParseCurrentLeafCell();
            return true;
        }

        // Move to next leaf page
        _leafIndex++;
        if (_leafIndex >= _leafPages.Length)
        {
            _exhausted = true;
            return false;
        }

        LoadLeafPage(_leafIndex);
        _cellIndex = 0;

        if (_leafHeader.CellCount == 0)
        {
            // Empty leaf — advance further (shouldn't happen in well-formed DBs)
            return MoveNext();
        }

        ParseCurrentLeafCell();
        return true;
    }

    /// <inheritdoc />
    public void Reset()
    {
        ReturnAssembledPayload();
        _snapshotVersion = _writableSource?.DataVersion ?? 0;
        _leafIndex = 0;
        _cellIndex = -1;
        _exhausted = _leafPages.Length == 0;
        _cachedLeafPageNum = 0;
        _cachedLeafMemory = default;

        if (_leafPages.Length > 0)
            LoadLeafPage(0);
    }

    /// <inheritdoc />
    public bool MoveLast() =>
        throw new NotSupportedException("LeafPageScanner is scan-only. Use BTreeCursor for seek/MoveLast operations.");

    /// <inheritdoc />
    public bool Seek(long rowId) =>
        throw new NotSupportedException("LeafPageScanner is scan-only. Use BTreeCursor for seek/MoveLast operations.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnAssembledPayload();
    }

    /// <summary>
    /// Loads the leaf page at the given index in the pre-collected leaf pages array.
    /// </summary>
    private void LoadLeafPage(int leafIndex)
    {
        uint pageNum = _leafPages[leafIndex];
        var page = GetCachedLeafPage(pageNum);
        _leafHeaderOffset = pageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
        _leafHeader = BTreePageHeader.Parse(page[_leafHeaderOffset..]);
    }

    /// <summary>
    /// Returns the cached leaf page data, fetching it only on cache miss.
    /// </summary>
    private ReadOnlySpan<byte> GetCachedLeafPage()
    {
        uint pageNum = _leafPages[_leafIndex];
        return GetCachedLeafPage(pageNum);
    }

    /// <summary>
    /// Returns the specified leaf page data, caching it to avoid redundant GetPageMemory() calls
    /// when iterating multiple cells on the same leaf.
    /// </summary>
    private ReadOnlySpan<byte> GetCachedLeafPage(uint pageNum)
    {
        if (pageNum != _cachedLeafPageNum)
        {
            _cachedLeafMemory = _pageSource.GetPageMemory(pageNum);
            _cachedLeafPageNum = pageNum;
        }
        return _cachedLeafMemory.Span;
    }

    /// <summary>
    /// Parses the cell at the current cell index within the current leaf page.
    /// </summary>
    private void ParseCurrentLeafCell()
    {
        var page = GetCachedLeafPage();
        int cellOffset = _leafHeader.GetCellPointer(page[_leafHeaderOffset..], _cellIndex);

        int cellHeaderSize = CellParser.ParseTableLeafCell(
            page[cellOffset..], out _payloadSize, out _rowId);

        int payloadStart = cellOffset + cellHeaderSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(_payloadSize, _usablePageSize);

        if (inlineSize >= _payloadSize)
        {
            _inlinePayloadOffset = payloadStart;
            _assembledPayload = null;
        }
        else
        {
            AssembleOverflowPayload(page, payloadStart, inlineSize);
        }
    }

    private void AssembleOverflowPayload(ReadOnlySpan<byte> page, int payloadStart, int inlineSize)
    {
        _assembledPayload = ArrayPool<byte>.Shared.Rent(_payloadSize);

        page.Slice(payloadStart, inlineSize).CopyTo(_assembledPayload);

        uint overflowPage = BinaryPrimitives.ReadUInt32BigEndian(
            page[(payloadStart + inlineSize)..]);

        int remaining = _payloadSize - inlineSize;
        int destOffset = inlineSize;
        int overflowDataSize = _usablePageSize - 4;

        _visitedOverflowPages ??= new HashSet<uint>();
        _visitedOverflowPages.Clear();

        while (overflowPage != 0 && remaining > 0)
        {
            if (!_visitedOverflowPages.Add(overflowPage))
                throw new CorruptPageException(overflowPage,
                    "Overflow page chain cycle detected.");

            var ovfPage = _pageSource.GetPage(overflowPage);
            int toCopy = Math.Min(remaining, overflowDataSize);
            ovfPage.Slice(4, toCopy).CopyTo(_assembledPayload.AsSpan(destOffset));

            destOffset += toCopy;
            remaining -= toCopy;

            overflowPage = BinaryPrimitives.ReadUInt32BigEndian(ovfPage);
        }
    }

    private void ReturnAssembledPayload()
    {
        if (_assembledPayload != null)
        {
            ArrayPool<byte>.Shared.Return(_assembledPayload);
            _assembledPayload = null;
        }
    }

    /// <summary>
    /// Collects all leaf page numbers in left-to-right order via DFS through interior pages.
    /// This is a one-time O(I) operation where I is the number of interior pages.
    /// </summary>
    private static uint[] CollectLeafPages(TPageSource pageSource, uint rootPage)
    {
        var page = pageSource.GetPage(rootPage);
        int headerOffset = rootPage == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
        var header = BTreePageHeader.Parse(page[headerOffset..]);

        // Fast path: root is a leaf page
        if (header.IsLeaf)
            return [rootPage];

        // DFS through interior pages
        var leaves = new List<uint>();
        CollectLeavesRecursive(pageSource, rootPage, header, headerOffset, leaves);
        return leaves.ToArray();
    }

    private static void CollectLeavesRecursive(
        TPageSource pageSource, uint pageNum, BTreePageHeader header, int headerOffset,
        List<uint> leaves)
    {
        var page = pageSource.GetPage(pageNum);

        // Process each cell's left child
        for (int i = 0; i < header.CellCount; i++)
        {
            int cellOffset = header.GetCellPointer(page[headerOffset..], i);
            uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellOffset..]);

            var childPage = pageSource.GetPage(leftChild);
            int childHeaderOffset = leftChild == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var childHeader = BTreePageHeader.Parse(childPage[childHeaderOffset..]);

            if (childHeader.IsLeaf)
                leaves.Add(leftChild);
            else
                CollectLeavesRecursive(pageSource, leftChild, childHeader, childHeaderOffset, leaves);
        }

        // Process right child
        if (header.RightChildPage != 0)
        {
            uint rightChild = header.RightChildPage;
            var childPage = pageSource.GetPage(rightChild);
            int childHeaderOffset = rightChild == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var childHeader = BTreePageHeader.Parse(childPage[childHeaderOffset..]);

            if (childHeader.IsLeaf)
                leaves.Add(rightChild);
            else
                CollectLeavesRecursive(pageSource, rightChild, childHeader, childHeaderOffset, leaves);
        }
    }
}
