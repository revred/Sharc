using System.Buffers;
using System.Buffers.Binary;
using Sharc.Core.Format;
using Sharc.Exceptions;

namespace Sharc.Core.BTree;

/// <summary>
/// Forward-only cursor that traverses an index b-tree in key order.
/// Index payloads are standard SQLite records where the last column is the table rowid.
/// </summary>
internal sealed class IndexBTreeCursor : IIndexBTreeCursor
{
    private readonly IPageSource _pageSource;
    private readonly int _usablePageSize;
    private readonly Stack<(uint page, int cellIndex, int headerOffset, BTreePageHeader header)> _stack = new();

    // Current leaf page state
    private uint _currentLeafPage;
    private int _currentHeaderOffset;
    private BTreePageHeader _currentHeader;
    private int _currentCellIndex;
    private bool _initialized;
    private bool _exhausted;
    private bool _disposed;

    // Current cell data
    private int _payloadSize;
    private byte[]? _assembledPayload;
    private int _inlinePayloadOffset;
    private uint _inlinePayloadPage;

    // Reusable overflow cycle detection set
    private HashSet<uint>? _visitedOverflowPages;

    private readonly uint _rootPage;

    public IndexBTreeCursor(IPageSource pageSource, uint rootPage, int usablePageSize)
    {
        _pageSource = pageSource;
        _rootPage = rootPage;
        _usablePageSize = usablePageSize;
    }

    /// <inheritdoc />
    public int PayloadSize => _payloadSize;

    /// <inheritdoc />
    public ReadOnlySpan<byte> Payload
    {
        get
        {
            if (_assembledPayload != null)
                return _assembledPayload.AsSpan(0, _payloadSize);

            var page = _pageSource.GetPage(_inlinePayloadPage);
            return page.Slice(_inlinePayloadOffset, _payloadSize);
        }
    }

    /// <inheritdoc />
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

    private void DescendToLeftmostLeaf(uint pageNumber)
    {
        while (true)
        {
            var page = _pageSource.GetPage(pageNumber);
            int headerOffset = pageNumber == 1 ? 100 : 0;
            var header = BTreePageHeader.Parse(page[headerOffset..]);

            if (header.IsLeaf)
            {
                _currentLeafPage = pageNumber;
                _currentHeaderOffset = headerOffset;
                _currentHeader = header;
                _currentCellIndex = -1;
                return;
            }

            // Interior page — push onto stack and descend to leftmost child
            if (header.CellCount == 0)
            {
                _stack.Push((pageNumber, 0, headerOffset, header));
                pageNumber = header.RightChildPage;
                continue;
            }

            _stack.Push((pageNumber, 0, headerOffset, header));

            // For index interior cells: [leftChild:4-BE] [payloadSize:varint] [payload...]
            ushort cellPtr = header.GetCellPointer(page[headerOffset..], 0);
            uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellPtr..]);
            pageNumber = leftChild;
        }
    }

    private bool AdvanceToNextCell()
    {
        _currentCellIndex++;

        while (true)
        {
            if (_currentCellIndex < _currentHeader.CellCount)
            {
                ParseCurrentLeafCell();
                return true;
            }

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
                _stack.Push((page, nextCellIndex, headerOffset, header));

                var interiorPage = _pageSource.GetPage(page);
                ushort cellPtr = header.GetCellPointer(interiorPage[headerOffset..], nextCellIndex);
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(interiorPage[cellPtr..]);

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
        int cellOffset = _currentHeader.GetCellPointer(page[_currentHeaderOffset..], _currentCellIndex);

        int cellHeaderSize = IndexCellParser.ParseIndexLeafCell(
            page[cellOffset..], out _payloadSize);

        int payloadStart = cellOffset + cellHeaderSize;
        int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(_payloadSize, _usablePageSize);

        if (inlineSize >= _payloadSize)
        {
            _inlinePayloadOffset = payloadStart;
            _inlinePayloadPage = _currentLeafPage;
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnAssembledPayload();
    }
}
