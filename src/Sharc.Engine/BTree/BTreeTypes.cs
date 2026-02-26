// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Format;

namespace Sharc.Core.BTree;

/// <summary>
/// Result of a B-tree mutation (delete or update) indicating whether the target row
/// was found and the (possibly new) root page number.
/// </summary>
/// <param name="Found">Whether the target row was found in the B-tree.</param>
/// <param name="RootPage">The root page number after the operation (may change due to splits).</param>
internal readonly record struct MutationResult(bool Found, uint RootPage);

/// <summary>
/// Tracks a position in the B-tree ancestor path during insertion.
/// Used with <c>stackalloc</c> to avoid heap allocation during tree descent.
/// </summary>
/// <param name="PageNum">The page number of the ancestor node.</param>
/// <param name="CellIndex">The cell index within the ancestor page where the child pointer was followed.</param>
internal readonly record struct InsertPathEntry(uint PageNum, int CellIndex);

/// <summary>
/// Represents a single frame in the B-Tree cursor's traversal stack.
/// Used by <see cref="IndexBTreeCursor{TPageSource}"/> and write engine.
/// </summary>
/// <param name="PageId">The page number of the node.</param>
/// <param name="CellIndex">The current cell index being visited in this node.</param>
/// <param name="HeaderOffset">The byte offset of the cell pointer array start.</param>
/// <param name="Header">The parsed header of the page.</param>
internal readonly record struct CursorStackFrame(
    uint PageId,
    int CellIndex,
    int HeaderOffset,
    BTreePageHeader Header
);

/// <summary>
/// Fixed-capacity inline stack for B-tree cursor traversal.
/// Each slot is a packed <see cref="ulong"/>: PageId in bits 16..47, CellIndex in bits 0..15.
/// Embedded directly in the cursor object — zero separate heap allocation.
/// CellCount and RightChildPage are re-derived from the cached page on pop.
/// </summary>
internal struct CursorStack
{
    private ulong _f0, _f1, _f2, _f3, _f4, _f5, _f6, _f7;

    /// <summary>Gets or sets a packed frame at the given stack depth.</summary>
    internal ulong this[int index]
    {
        get => index switch
        {
            0 => _f0, 1 => _f1, 2 => _f2, 3 => _f3,
            4 => _f4, 5 => _f5, 6 => _f6, 7 => _f7,
            _ => 0
        };
        set
        {
            switch (index)
            {
                case 0: _f0 = value; break;
                case 1: _f1 = value; break;
                case 2: _f2 = value; break;
                case 3: _f3 = value; break;
                case 4: _f4 = value; break;
                case 5: _f5 = value; break;
                case 6: _f6 = value; break;
                case 7: _f7 = value; break;
            }
        }
    }

    /// <summary>Packs a PageId and CellIndex into a single ulong.</summary>
    internal static ulong Pack(uint pageId, int cellIndex)
        => ((ulong)pageId << 16) | (uint)(ushort)cellIndex;

    /// <summary>Extracts the PageId from a packed frame.</summary>
    internal static uint PageId(ulong frame) => (uint)(frame >> 16);

    /// <summary>Extracts the CellIndex from a packed frame.</summary>
    internal static int CellIndex(ulong frame) => (int)(ushort)frame;
}
