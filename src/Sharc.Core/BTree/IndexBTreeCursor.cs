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
    public void Reset()
    {
        ReturnAssembledPayload();
        _stack.Clear();
        _initialized = false;
        _exhausted = false;
        _currentLeafPage = 0;
        _payloadSize = 0;
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
            int headerOffset = pageNumber == 1 ? SQLiteLayout.DatabaseHeaderSize : 0;
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
                _stack.Push(new CursorStackFrame(pageNumber, 0, headerOffset, header));
                pageNumber = header.RightChildPage;
                continue;
            }

            _stack.Push(new CursorStackFrame(pageNumber, 0, headerOffset, header));

            // For index interior cells: [leftChild:4-BE] [payloadSize:varint] [payload...]
            ushort cellPtr = header.GetCellPointer(page[headerOffset..], 0);
            uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellPtr..]);
            pageNumber = leftChild;
        }
    }

    /// <inheritdoc />
    public bool SeekFirst(long firstColumnKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stack.Clear();
        _exhausted = false;
        _initialized = true;

        bool exactMatch = DescendToLeafByKey(_rootPage, firstColumnKey);

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

    /// <inheritdoc />
    public bool SeekFirst(ReadOnlySpan<byte> utf8Key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stack.Clear();
        _exhausted = false;
        _initialized = true;

        bool exactMatch = DescendToLeafByTextKey(_rootPage, utf8Key);

        if (_currentCellIndex < _currentHeader.CellCount)
        {
            ParseCurrentLeafCell();
            return exactMatch;
        }
        else
        {
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

    /// <summary>
    /// Binary search descent through the index B-tree comparing the first column (text) value
    /// using byte-ordinal UTF-8 comparison (BINARY collation).
    /// </summary>
    private bool DescendToLeafByTextKey(uint pageNumber, ReadOnlySpan<byte> targetKey)
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

                int low = 0;
                int high = header.CellCount - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    int cellOffset = header.GetCellPointer(page[headerOffset..], mid);

                    int headerBytes = IndexCellParser.ParseIndexLeafCell(page[cellOffset..], out int payloadSize);
                    int payloadStart = cellOffset + headerBytes;
                    int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, _usablePageSize);
                    int safeSize = Math.Min(payloadSize, inlineSize);

                    var colBytes = ExtractFirstTextColumn(page.Slice(payloadStart, safeSize));
                    int cmp = colBytes.SequenceCompareTo(targetKey);

                    if (cmp < 0)
                        low = mid + 1;
                    else if (cmp > 0)
                        high = mid - 1;
                    else
                    {
                        while (mid > 0)
                        {
                            int prevOffset = header.GetCellPointer(page[headerOffset..], mid - 1);
                            int prevHdrBytes = IndexCellParser.ParseIndexLeafCell(page[prevOffset..], out int prevPayloadSize);
                            int prevPayloadStart = prevOffset + prevHdrBytes;
                            int prevInlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(prevPayloadSize, _usablePageSize);
                            int prevSafeSize = Math.Min(prevPayloadSize, prevInlineSize);

                            var prevVal = ExtractFirstTextColumn(page.Slice(prevPayloadStart, prevSafeSize));
                            if (!prevVal.SequenceEqual(targetKey)) break;
                            mid--;
                        }

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
                int cellOffset = header.GetCellPointer(page[headerOffset..], mid);

                int cellHdrSize = IndexCellParser.ParseIndexInteriorCell(page[cellOffset..], out _, out int payloadSize);
                int payloadStart = cellOffset + cellHdrSize;
                int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, _usablePageSize);
                int safeSize = Math.Min(payloadSize, inlineSize);

                var colBytes = ExtractFirstTextColumn(page.Slice(payloadStart, safeSize));
                int cmp = colBytes.SequenceCompareTo(targetKey);

                if (cmp >= 0)
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
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellOffset..]);
                pageNumber = leftChild;
            }
            else
            {
                _stack.Push(new CursorStackFrame(pageNumber, header.CellCount - 1, headerOffset, header));
                pageNumber = header.RightChildPage;
            }
        }
    }

    /// <summary>
    /// Extracts the first text column's raw UTF-8 bytes from a SQLite record payload.
    /// For text serial types (odd, >= 13), the byte length is (serialType - 13) / 2.
    /// </summary>
    private static ReadOnlySpan<byte> ExtractFirstTextColumn(ReadOnlySpan<byte> payload)
    {
        int offset = Primitives.VarintDecoder.Read(payload, out long headerSize);
        Primitives.VarintDecoder.Read(payload[offset..], out long serialType);

        int bodyOffset = (int)headerSize;

        if (serialType >= 13 && (serialType & 1) == 1)
        {
            // Text: byte length = (serialType - 13) / 2
            int len = (int)(serialType - 13) / 2;
            return payload.Slice(bodyOffset, len);
        }

        if (serialType >= 12 && (serialType & 1) == 0)
        {
            // Blob: byte length = (serialType - 12) / 2
            int len = (int)(serialType - 12) / 2;
            return payload.Slice(bodyOffset, len);
        }

        // Not a text/blob column — return empty
        return ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// Binary search descent through the index B-tree comparing the first column value.
    /// </summary>
    private bool DescendToLeafByKey(uint pageNumber, long targetKey)
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

                // Binary search leaf cells
                int low = 0;
                int high = header.CellCount - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);
                    int cellOffset = header.GetCellPointer(page[headerOffset..], mid);

                    // Leaf cell: [payloadSize:varint] [payload...]
                    int headerBytes = IndexCellParser.ParseIndexLeafCell(page[cellOffset..], out int payloadSize);
                    int payloadStart = cellOffset + headerBytes;
                    int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, _usablePageSize);
                    int safeSize = Math.Min(payloadSize, inlineSize);

                    long colValue = ExtractFirstIntColumn(page.Slice(payloadStart, safeSize));

                    if (colValue < targetKey)
                        low = mid + 1;
                    else if (colValue > targetKey)
                        high = mid - 1;
                    else
                    {
                        // Found a match — scan backwards to find the first occurrence
                        while (mid > 0)
                        {
                            int prevOffset = header.GetCellPointer(page[headerOffset..], mid - 1);
                            int prevHdrBytes = IndexCellParser.ParseIndexLeafCell(page[prevOffset..], out int prevPayloadSize);
                            int prevPayloadStart = prevOffset + prevHdrBytes;
                            int prevInlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(prevPayloadSize, _usablePageSize);
                            int prevSafeSize = Math.Min(prevPayloadSize, prevInlineSize);

                            long prevVal = ExtractFirstIntColumn(page.Slice(prevPayloadStart, prevSafeSize));
                            if (prevVal != targetKey) break;
                            mid--;
                        }

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
                int cellOffset = header.GetCellPointer(page[headerOffset..], mid);

                // Interior cell: [leftChild:4-BE] [payloadSize:varint] [payload...]
                int cellHdrSize = IndexCellParser.ParseIndexInteriorCell(page[cellOffset..], out _, out int payloadSize);
                int payloadStart = cellOffset + cellHdrSize;
                int inlineSize = IndexCellParser.CalculateIndexInlinePayloadSize(payloadSize, _usablePageSize);
                int safeSize = Math.Min(payloadSize, inlineSize);

                long colValue = ExtractFirstIntColumn(page.Slice(payloadStart, safeSize));

                if (colValue >= targetKey)
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
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(page[cellOffset..]);
                pageNumber = leftChild;
            }
            else
            {
                _stack.Push(new CursorStackFrame(pageNumber, header.CellCount - 1, headerOffset, header));
                pageNumber = header.RightChildPage;
            }
        }
    }

    /// <summary>
    /// Extracts the first integer column from a SQLite record payload without full decode.
    /// Assumes the first column is an integer type (serial types 0-9).
    /// </summary>
    private static long ExtractFirstIntColumn(ReadOnlySpan<byte> payload)
    {
        // Record header: [headerSize:varint] [serialType0:varint] ...
        int offset = Primitives.VarintDecoder.Read(payload, out long headerSize);
        Primitives.VarintDecoder.Read(payload[offset..], out long serialType);

        // Body starts after the header
        int bodyOffset = (int)headerSize;

        return serialType switch
        {
            0 => 0,                   // NULL
            1 => (sbyte)payload[bodyOffset],
            2 => BinaryPrimitives.ReadInt16BigEndian(payload[bodyOffset..]),
            3 => ((payload[bodyOffset] << 16) | (payload[bodyOffset + 1] << 8) | payload[bodyOffset + 2]) | 
                 (((payload[bodyOffset] & 0x80) != 0) ? unchecked((int)0xFF000000) : 0),
            4 => BinaryPrimitives.ReadInt32BigEndian(payload[bodyOffset..]),
            5 => ExtractInt48(payload[bodyOffset..]),
            6 => BinaryPrimitives.ReadInt64BigEndian(payload[bodyOffset..]),
            8 => 0,  // Constant 0
            9 => 1,  // Constant 1
            _ => 0   // Non-integer types default to 0
        };
    }

    private static long ExtractInt48(ReadOnlySpan<byte> data)
    {
        long raw = ((long)data[0] << 40) | ((long)data[1] << 32) |
                   ((long)data[2] << 24) | ((long)data[3] << 16) |
                   ((long)data[4] << 8) | data[5];
        if ((raw & 0x800000000000L) != 0)
            raw |= unchecked((long)0xFFFF000000000000L);
        return raw;
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
            var frame = _stack.Pop();
            int nextCellIndex = frame.CellIndex + 1;

            if (nextCellIndex < frame.Header.CellCount)
            {
                _stack.Push(new CursorStackFrame(frame.PageId, nextCellIndex, frame.HeaderOffset, frame.Header));

                var interiorPage = _pageSource.GetPage(frame.PageId);
                ushort cellPtr = frame.Header.GetCellPointer(interiorPage[frame.HeaderOffset..], nextCellIndex);
                uint leftChild = BinaryPrimitives.ReadUInt32BigEndian(interiorPage[cellPtr..]);

                DescendToLeftmostLeaf(leftChild);
                return true;
            }

            // All cells exhausted — descend to right child
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
