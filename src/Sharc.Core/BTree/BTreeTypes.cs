// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Format;

namespace Sharc.Core.BTree;

/// <summary>
/// Result of a B-tree mutation (delete or update) indicating whether the target row
/// was found and the (possibly new) root page number.
/// </summary>
internal readonly record struct MutationResult(bool Found, uint RootPage);

/// <summary>
/// Tracks a position in the B-tree ancestor path during insertion.
/// Used with <c>stackalloc</c> to avoid heap allocation during tree descent.
/// </summary>
internal readonly record struct InsertPathEntry(uint PageNum, int CellIndex);

/// <summary>
/// Represents a single frame in the B-Tree cursor's traversal stack.
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
