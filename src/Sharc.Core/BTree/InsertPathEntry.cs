// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.BTree;

/// <summary>
/// Tracks a position in the B-tree ancestor path during insertion.
/// Used with <c>stackalloc</c> to avoid heap allocation during tree descent.
/// </summary>
internal readonly record struct InsertPathEntry(uint PageNum, int CellIndex);
