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
    private readonly Stack<CursorStackFrame> _stack = new();

    // Current leaf page state
    private uint _currentLeafPage;
    private int _currentHeaderOffset;
    private BTreePageHeader _currentHeader;
    private int _currentCellIndex;
    private bool _initialized;
    private bool _exhausted;
    private bool _disposed;

    // Current cell data
    private long _rowId;
    private int _payloadSize;
    private byte[]? _assembledPayload;
    private int _inlinePayloadOffset;
    private uint _inlinePayloadPage;

    // Reusable overflow cycle detection set Ã¢â‚¬â€ cleared between overflow assemblies
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

            // Return inline payload from the page
            var page = _pageSource.GetPage(_inlinePayloadPage);
            return page.Slice(_inlinePayloadOffset, _payloadSize);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        ReturnAssembledPayload();
        _stack.Clear();
        _initialized = false;
        _exhausted = false;
        _currentLeafPage = 0;
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
        _stack.Clear();
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
        _stack.Clear();
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

            // Interior page Ã¢â‚¬â€ push onto stack and descend to leftmost child
            if (header.CellCount == 0)
            {
                // Interior page with no cells Ã¢â‚¬â€ go to right child
                _stack.Push(new CursorStackFrame(pageNumber, 0, headerOffset, header));
                pageNumber = header.RightChildPage;
                continue;
            }

            // Push this interior page (starting before first cell)
            _stack.Push(new CursorStackFrame(pageNumber, 0, headerOffset, header));

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

            // Interior page Ã¢â‚¬â€ binary search for the correct child
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
                _stack.Push(new CursorStackFrame(pageNumber, idx, headerOffset, header));
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

            // Current leaf exhausted Ã¢â‚¬â€ try to move to next leaf via stack
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
        while (_stack.Count > 0)
        {
            var (page, cellIndex, headerOffset, header) = _stack.Pop();

            int nextCellIndex = cellIndex + 1;

            if (nextCellIndex < header.CellCount)
            {
                // More cells in this interior page Ã¢â‚¬â€œ push updated state and descend
                _stack.Push(new CursorStackFrame(page, nextCellIndex, headerOffset, header));

                // Read the single cell pointer on-demand (no array allocation)
                var interiorPage = _pageSource.GetPage(page);
                ushort cellPtr = header.GetCellPointer(interiorPage[headerOffset..], nextCellIndex);
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(interiorPage[cellPtr..]);

                DescendToLeftmostLeaf(leftChild);
                return true;
            }

            // All cells exhausted Ã¢â‚¬â€ descend to right child
            if (header.RightChildPage != 0)
            {
                DescendToLeftmostLeaf(header.RightChildPage);
                return true;
            }
        }

        return false;
    }

    private void ParseCurrentLeafCell()
    {
        var page = _pageSource.GetPage(_currentLeafPage);
        // Read cell pointer on-demand Ã¢â‚¬â€ zero allocation
        int cellOffset = _currentHeader.GetCellPointer(page[_currentHeaderOffset..], _currentCellIndex);

        int cellHeaderSize = CellParser.ParseTableLeafCell(
            page[cellOffset..], out _payloadSize, out _rowId);

        int payloadStart = cellOffset + cellHeaderSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(_payloadSize, _usablePageSize);

        if (inlineSize >= _payloadSize)
        {
            // All payload is inline Ã¢â‚¬â€ point directly at page data
            _inlinePayloadOffset = payloadStart;
            _inlinePayloadPage = _currentLeafPage;
            _assembledPayload = null;
        }
        else
        {
            // Overflow Ã¢â‚¬â€ assemble the full payload
            AssembleOverflowPayload(page, payloadStart, inlineSize);
        }
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnAssembledPayload();
    }
}