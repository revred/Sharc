// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Buffers.Binary;
using Sharc.Exceptions;

namespace Sharc.Core.Format;

/// <summary>
/// SQLite b-tree page types.
/// </summary>
public enum BTreePageType : byte
{
    /// <summary>Interior index b-tree page.</summary>
    InteriorIndex = 0x02,

    /// <summary>Interior table b-tree page.</summary>
    InteriorTable = 0x05,

    /// <summary>Leaf index b-tree page.</summary>
    LeafIndex = 0x0A,

    /// <summary>Leaf table b-tree page.</summary>
    LeafTable = 0x0D,
}

/// <summary>
/// Parsed b-tree page header. 8 bytes for leaf pages, 12 bytes for interior pages.
/// </summary>
public readonly struct BTreePageHeader
{
    /// <summary>Page type.</summary>
    public BTreePageType PageType { get; }

    /// <summary>Offset of the first freeblock (0 if none).</summary>
    public ushort FirstFreeblockOffset { get; }

    /// <summary>Number of cells on this page.</summary>
    public ushort CellCount { get; }

    /// <summary>Offset to the start of the cell content area.</summary>
    public ushort CellContentOffset { get; }

    /// <summary>Number of fragmented free bytes in the cell content area.</summary>
    public byte FragmentedFreeBytes { get; }

    /// <summary>Right-most child page pointer (interior pages only; 0 for leaves).</summary>
    public uint RightChildPage { get; }

    /// <summary>True if this is a leaf page (no child pointers).</summary>
    public bool IsLeaf => PageType == BTreePageType.LeafTable || PageType == BTreePageType.LeafIndex;

    /// <summary>True if this is a table b-tree page (vs index b-tree).</summary>
    public bool IsTable => PageType == BTreePageType.LeafTable || PageType == BTreePageType.InteriorTable;

    /// <summary>Header size in bytes (8 for leaf, 12 for interior).</summary>
    public int HeaderSize => IsLeaf ? SQLiteLayout.TableLeafHeaderSize : SQLiteLayout.TableInteriorHeaderSize;

    /// <summary>
    /// Initializes a new b-tree page header.
    /// </summary>
    public BTreePageHeader(BTreePageType pageType, ushort firstFreeblockOffset,
        ushort cellCount, ushort cellContentOffset, byte fragmentedFreeBytes,
        uint rightChildPage)
    {
        PageType = pageType;
        FirstFreeblockOffset = firstFreeblockOffset;
        CellCount = cellCount;
        CellContentOffset = cellContentOffset;
        FragmentedFreeBytes = fragmentedFreeBytes;
        RightChildPage = rightChildPage;
    }

    /// <summary>
    /// Parses a b-tree page header from the given bytes.
    /// </summary>
    /// <param name="data">Page bytes starting at the b-tree header offset.</param>
    /// <returns>Parsed header.</returns>
    /// <exception cref="CorruptPageException">Invalid page type flag.</exception>
    public static BTreePageHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < SQLiteLayout.TableInteriorHeaderSize)
            throw new CorruptPageException(0,
                $"Page data too small for b-tree header: {data.Length} bytes (minimum 12 required).");

        byte typeFlag = data[0];
        if (typeFlag is not (0x02 or 0x05 or 0x0A or 0x0D))
            throw new CorruptPageException(0, $"Invalid b-tree page type flag: 0x{typeFlag:X2}");

        var pageType = (BTreePageType)typeFlag;
        ushort firstFreeblock = BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
        ushort cellCount = BinaryPrimitives.ReadUInt16BigEndian(data[3..]);
        ushort cellContentOffset = BinaryPrimitives.ReadUInt16BigEndian(data[5..]);
        byte fragmentedFreeBytes = data[7];

        uint rightChild = 0;
        bool isLeaf = pageType is BTreePageType.LeafTable or BTreePageType.LeafIndex;
        if (!isLeaf)
        {
            rightChild = BinaryPrimitives.ReadUInt32BigEndian(data[SQLiteLayout.RightChildPageOffset..]);
        }

        return new BTreePageHeader(pageType, firstFreeblock, cellCount,
            cellContentOffset, fragmentedFreeBytes, rightChild);
    }

    /// <summary>
    /// Reads a single cell pointer by index from the cell pointer array.
    /// Zero-allocation alternative to <see cref="ReadCellPointers"/>.
    /// </summary>
    /// <param name="pageData">The full page bytes (starting at b-tree header offset).</param>
    /// <param name="cellIndex">0-based index into the cell pointer array.</param>
    /// <returns>The cell offset within the page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellIndex"/> is negative or >= <see cref="CellCount"/>.</exception>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public ushort GetCellPointer(ReadOnlySpan<byte> pageData, int cellIndex)
    {
        if ((uint)cellIndex >= (uint)CellCount)
            throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex,
                $"Cell index must be between 0 and {CellCount - 1}.");

        int offset = HeaderSize + cellIndex * 2;
        return BinaryPrimitives.ReadUInt16BigEndian(pageData[offset..]);
    }

    /// <summary>
    /// Reads the cell pointer array following this header.
    /// </summary>
    /// <param name="pageData">The full page bytes (starting at b-tree header offset).</param>
    /// <returns>Array of cell offsets within the page.</returns>
    public ushort[] ReadCellPointers(ReadOnlySpan<byte> pageData)
    {
        var pointers = new ushort[CellCount];
        int offset = HeaderSize;
        for (int i = 0; i < CellCount; i++)
        {
            pointers[i] = BinaryPrimitives.ReadUInt16BigEndian(pageData[offset..]);
            offset += 2;
        }
        return pointers;
    }

    /// <summary>
    /// Writes the b-tree page header to the destination span.
    /// </summary>
    /// <param name="destination">At least 12 bytes.</param>
    /// <param name="header">The header values to write.</param>
    /// <returns>Number of bytes written (8 for leaf, 12 for interior).</returns>
    public static int Write(Span<byte> destination, BTreePageHeader header)
    {
        destination[0] = (byte)header.PageType;
        BinaryPrimitives.WriteUInt16BigEndian(destination[1..], header.FirstFreeblockOffset);
        BinaryPrimitives.WriteUInt16BigEndian(destination[3..], header.CellCount);
        BinaryPrimitives.WriteUInt16BigEndian(destination[5..], header.CellContentOffset);
        destination[7] = header.FragmentedFreeBytes;

        if (!header.IsLeaf)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[SQLiteLayout.RightChildPageOffset..], header.RightChildPage);
            return SQLiteLayout.TableInteriorHeaderSize;
        }

        return SQLiteLayout.TableLeafHeaderSize;
    }
}