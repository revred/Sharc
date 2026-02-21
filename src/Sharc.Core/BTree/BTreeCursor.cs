// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers;
using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Exceptions;

namespace Sharc.Core.BTree;

/// <summary>
/// Forward-only cursor that traverses a table b-tree in rowid order.
/// Uses a stack to track position through interior pages and descends to leaf pages.
/// </summary>
internal sealed class BTreeCursor : IBTreeCursor
{
    private readonly IPageSource _pageSource;
    private readonly int _usablePageSize;
    private CursorStackFrame[] _stack = new CursorStackFrame[8];
    private int _stackTop;

    // Current leaf page state
    private uint _currentLeafPage;
    private int _currentHeaderOffset;
    private BTreePageHeader _currentHeader;
    private int _currentCellIndex;
    private bool _initialized;
    private bool _exhausted;
    private bool _disposed;

    // Leaf page cache — avoids redundant GetPage() calls for cells on the same leaf
    private ReadOnlyMemory<byte> _cachedLeafMemory;
    private uint _cachedLeafPageNum;

    // Current cell data
    private long _rowId;
    private int _payloadSize;
    private byte[]? _assembledPayload;
    private int _inlinePayloadOffset;

    // Reusable overflow cycle detection set - cleared between overflow assemblies
    private HashSet<uint>? _visitedOverflowPages;

    private readonly uint _rootPage;

    public BTreeCursor(IPageSource pageSource, uint rootPage, int usablePageSize)
    {
        _pageSource = pageSource;
        _rootPage = rootPage;
        _usablePageSize = usablePageSize;
    }

    /// <inheritdoc />
    public long RowId => _rowId;

    /// <inheritdoc />
    public int PayloadSize => _payloadSize;

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
    public void Reset()
    {
        ReturnAssembledPayload();
        _stackTop = 0;
        _initialized = false;
        _exhausted = false;
        _currentLeafPage = 0;
        _cachedLeafPageNum = 0;
        _cachedLeafMemory = default;
    }

    /// <inheritdoc />
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Return any previously assembled overflow buffer
        ReturnAssembledPayload();

        if (_exhausted)
            return false;

        if (!_initialized)
        {
            _initialized = true;
            DescendToLeftmostLeaf(_rootPage);
        }

        return AdvanceToNextCell();
    }

    /// <inheritdoc />
    public bool MoveLast()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReturnAssembledPayload();
        _stackTop = 0;
        _exhausted = false;
        _initialized = true;

        uint pageNum = _rootPage;
        while (true)
        {
             var page = _pageSource.GetPage(pageNum);
             int headerOffset = pageNum == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
             var header = BTreePageHeader.Parse(page[headerOffset..]);

             if (header.IsLeaf)
             {
                 _currentLeafPage = pageNum;
                 _currentHeaderOffset = headerOffset;
                 _currentHeader = header;
                 
                 if (header.CellCount == 0)
                 {
                     _exhausted = true;
                     return false;
                 }
                 
                 _currentCellIndex = header.CellCount - 1;
                 ParseCurrentLeafCell();
                 return true;
             }
             
             // Interior page - follow right pointer
             pageNum = header.RightChildPage;
        }
    }

    /// <inheritdoc />
    public bool Seek(long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReturnAssembledPayload();
        _stackTop = 0;
        _exhausted = false;
        _initialized = true;

        bool exactMatch = DescendToLeaf(_rootPage, rowId);

        if (_currentCellIndex < _currentHeader.CellCount)
        {
            ParseCurrentLeafCell();
            return exactMatch;
        }
        else
        {
            // Moved past end of leaf, try next leaf
            if (MoveToNextLeaf())
            {
                _currentCellIndex = 0;
                ParseCurrentLeafCell();
                return false;
            }
            else
            {
                _exhausted = true;
                return false;
            }
        }
    }

    private void DescendToLeftmostLeaf(uint pageNumber)
    {
        while (true)
        {
            var page = _pageSource.GetPage(pageNumber);
            int headerOffset = pageNumber == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var header = BTreePageHeader.Parse(page[headerOffset..]);

            if (header.IsLeaf)
            {
                _currentLeafPage = pageNumber;
                _currentHeaderOffset = headerOffset;
                _currentHeader = header;
                _currentCellIndex = -1; // Will be incremented by AdvanceToNextCell
                return;
            }

            // Interior page - push onto stack and descend to leftmost child
            if (header.CellCount == 0)
            {
                // Interior page with no cells - go to right child
                StackPush(new CursorStackFrame(pageNumber, 0, headerOffset, header));
                pageNumber = header.RightChildPage;
                continue;
            }

            // Push this interior page (starting before first cell)
            StackPush(new CursorStackFrame(pageNumber, 0, headerOffset, header));

            // Descend to the left child of the first cell
            // Read the single cell pointer on-demand (no array allocation)
            ushort cellPtr = header.GetCellPointer(page[headerOffset..], 0);
            uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellPtr..]);
            pageNumber = leftChild;
        }
    }

    private bool DescendToLeaf(uint pageNumber, long targetRowId)
    {
        while (true)
        {
            var page = _pageSource.GetPage(pageNumber);
            int headerOffset = pageNumber == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
            var header = BTreePageHeader.Parse(page[headerOffset..]);

            if (header.IsLeaf)
            {
                _currentLeafPage = pageNumber;
                _currentHeaderOffset = headerOffset;
                _currentHeader = header;

                // Binary search leaf cells using on-demand pointer reads
                int low = 0;
                int high = header.CellCount - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    int cellOffset = header.GetCellPointer(page[headerOffset..], mid);

                    CellParser.ParseTableLeafCell(page[cellOffset..], out int _, out long rowId);

                    if (rowId < targetRowId)
                        low = mid + 1;
                    else if (rowId > targetRowId)
                        high = mid - 1;
                    else
                    {
                        _currentCellIndex = mid;
                        return true;
                    }
                }

                _currentCellIndex = low;
                return false;
            }

            // Interior page - binary search for the correct child
            int idx = -1;
            int l = 0;
            int r = header.CellCount - 1;

            while (l <= r)
            {
                int mid = l + ((r - l) >> 1);
                int cellOffset = header.GetCellPointer(page[headerOffset..], mid);
                CellParser.ParseTableInteriorCell(page[cellOffset..], out _, out long key);

                if (key >= targetRowId)
                {
                    idx = mid;
                    r = mid - 1;
                }
                else
                {
                    l = mid + 1;
                }
            }

            if (idx != -1)
            {
                StackPush(new CursorStackFrame(pageNumber, idx, headerOffset, header));
                int cellOffset = header.GetCellPointer(page[headerOffset..], idx);
                CellParser.ParseTableInteriorCell(page[cellOffset..], out uint leftChild, out _);
                pageNumber = leftChild;
            }
            else
            {
                pageNumber = header.RightChildPage;
            }
        }
    }

    private bool AdvanceToNextCell()
    {
        _currentCellIndex++;

        while (true)
        {
            if (_currentCellIndex < _currentHeader.CellCount)
            {
                // Parse the current leaf cell
                ParseCurrentLeafCell();
                return true;
            }

            // Current leaf exhausted - try to move to next leaf via stack
            if (!MoveToNextLeaf())
            {
                _exhausted = true;
                return false;
            }

            _currentCellIndex = 0;
        }
    }

    private bool MoveToNextLeaf()
    {
        while (_stackTop > 0)
        {
            var frame = StackPop();
            int nextCellIndex = frame.CellIndex + 1;

            if (nextCellIndex < frame.Header.CellCount)
            {
                // More cells in this interior page — push updated state and descend
                StackPush(new CursorStackFrame(frame.PageId, nextCellIndex, frame.HeaderOffset, frame.Header));

                // Read the single cell pointer on-demand (no array allocation)
                var interiorPage = _pageSource.GetPage(frame.PageId);
                ushort cellPtr = frame.Header.GetCellPointer(interiorPage[frame.HeaderOffset..], nextCellIndex);
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(interiorPage[cellPtr..]);

                DescendToLeftmostLeaf(leftChild);
                return true;
            }

            // All cells exhausted - descend to right child
            if (frame.Header.RightChildPage != 0)
            {
                DescendToLeftmostLeaf(frame.Header.RightChildPage);
                return true;
            }
        }

        return false;
    }

    private void ParseCurrentLeafCell()
    {
        var page = GetCachedLeafPage();
        // Read cell pointer on-demand - zero allocation
        int cellOffset = _currentHeader.GetCellPointer(page[_currentHeaderOffset..], _currentCellIndex);

        int cellHeaderSize = CellParser.ParseTableLeafCell(
            page[cellOffset..], out _payloadSize, out _rowId);

        int payloadStart = cellOffset + cellHeaderSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(_payloadSize, _usablePageSize);

        if (inlineSize >= _payloadSize)
        {
            // All payload is inline — offset into cached leaf memory
            _inlinePayloadOffset = payloadStart;
            _assembledPayload = null;
        }
        else
        {
            // Overflow - assemble the full payload
            AssembleOverflowPayload(page, payloadStart, inlineSize);
        }
    }

    /// <summary>
    /// Returns the current leaf page data, caching it to avoid redundant GetPage()/GetPageMemory() calls
    /// when iterating multiple cells on the same leaf.
    /// </summary>
    private ReadOnlySpan<byte> GetCachedLeafPage()
    {
        if (_currentLeafPage != _cachedLeafPageNum)
        {
            _cachedLeafMemory = _pageSource.GetPageMemory(_currentLeafPage);
            _cachedLeafPageNum = _currentLeafPage;
        }
        return _cachedLeafMemory.Span;
    }

    private void AssembleOverflowPayload(ReadOnlySpan<byte> page, int payloadStart, int inlineSize)
    {
        _assembledPayload = ArrayPool<byte>.Shared.Rent(_payloadSize);

        // Copy inline portion
        page.Slice(payloadStart, inlineSize).CopyTo(_assembledPayload);

        // Read overflow page pointer (4 bytes after inline payload)
        uint overflowPage = BinaryPrimitives.ReadUInt32BigEndian(
            page[(payloadStart + inlineSize)..]);

        int remaining = _payloadSize - inlineSize;
        int destOffset = inlineSize;
        int overflowDataSize = _usablePageSize - 4;

        // Reuse the HashSet instead of allocating a new one per overflow cell
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

    private void StackPush(CursorStackFrame frame)
    {
        if (_stackTop == _stack.Length)
            Array.Resize(ref _stack, _stack.Length * 2);
        _stack[_stackTop++] = frame;
    }

    private CursorStackFrame StackPop() => _stack[--_stackTop];

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnAssembledPayload();
    }
}
