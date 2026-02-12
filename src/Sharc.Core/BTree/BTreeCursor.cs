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
    private readonly Stack<(uint page, int cellIndex, ushort[] pointers, BTreePageHeader header)> _stack = new();

    // Current leaf page state
    private uint _currentLeafPage;
    private ushort[] _currentPointers = [];
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
            int headerOffset = pageNumber == 1 ? 100 : 0;
            var header = BTreePageHeader.Parse(page[headerOffset..]);
            var pointers = header.ReadCellPointers(page[headerOffset..]);

            if (header.IsLeaf)
            {
                _currentLeafPage = pageNumber;
                _currentHeader = header;
                _currentPointers = pointers;
                _currentCellIndex = -1; // Will be incremented by AdvanceToNextCell
                return;
            }

            // Interior page — push onto stack and descend to leftmost child
            if (header.CellCount == 0)
            {
                // Interior page with no cells — go to right child
                _stack.Push((pageNumber, 0, pointers, header));
                pageNumber = header.RightChildPage;
                continue;
            }

            // Push this interior page (starting before first cell)
            _stack.Push((pageNumber, 0, pointers, header));

            // Descend to the left child of the first cell
            var interiorPage = _pageSource.GetPage(pageNumber);
            uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(
                interiorPage[pointers[0]..]);
            pageNumber = leftChild;
        }
    }

    private bool DescendToLeaf(uint pageNumber, long targetRowId)
    {
        while (true)
        {
            var page = _pageSource.GetPage(pageNumber);
            int headerOffset = pageNumber == 1 ? 100 : 0;
            var header = BTreePageHeader.Parse(page[headerOffset..]);
            var pointers = header.ReadCellPointers(page[headerOffset..]);

            if (header.IsLeaf)
            {
                _currentLeafPage = pageNumber;
                _currentHeader = header;
                _currentPointers = pointers;

                // Binary search leaf cells
                int low = 0;
                int high = header.CellCount - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    int cellOffset = pointers[mid];

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

            // Interior page — binary search for the correct child
            int idx = -1;
            int l = 0;
            int r = header.CellCount - 1;

            while (l <= r)
            {
                int mid = l + ((r - l) >> 1);
                int cellOffset = pointers[mid];
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
                _stack.Push((pageNumber, idx, pointers, header));
                int cellOffset = pointers[idx];
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

            // Current leaf exhausted — try to move to next leaf via stack
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
            var (page, cellIndex, pointers, header) = _stack.Pop();

            int nextCellIndex = cellIndex + 1;

            if (nextCellIndex < header.CellCount)
            {
                // More cells in this interior page — push updated state and descend
                _stack.Push((page, nextCellIndex, pointers, header));

                // Cell pointers are page-absolute offsets
                var interiorPage = _pageSource.GetPage(page);
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(
                    interiorPage[pointers[nextCellIndex]..]);

                DescendToLeftmostLeaf(leftChild);
                return true;
            }

            // All cells exhausted — descend to right child
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
        // Cell pointers are page-absolute offsets (from start of page)
        int cellOffset = _currentPointers[_currentCellIndex];

        int cellHeaderSize = CellParser.ParseTableLeafCell(
            page[cellOffset..], out _payloadSize, out _rowId);

        int payloadStart = cellOffset + cellHeaderSize;
        int inlineSize = CellParser.CalculateInlinePayloadSize(_payloadSize, _usablePageSize);

        if (inlineSize >= _payloadSize)
        {
            // All payload is inline — point directly at page data
            _inlinePayloadOffset = payloadStart;
            _inlinePayloadPage = _currentLeafPage;
            _assembledPayload = null;
        }
        else
        {
            // Overflow — assemble the full payload
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

        var visited = new HashSet<uint>();

        while (overflowPage != 0 && remaining > 0)
        {
            if (!visited.Add(overflowPage))
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
