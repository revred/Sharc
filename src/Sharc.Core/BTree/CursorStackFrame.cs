// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Format;

namespace Sharc.Core.BTree;

/// <summary>
/// Represents a single frame in the B-Tree cursor's traversal stack.
/// Replaces the ValueTuple (uint, int, int, byte[]) for better readability.
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
