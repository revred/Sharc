// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.BTree;

/// <summary>
/// Result of a B-tree mutation (delete or update) indicating whether the target row
/// was found and the (possibly new) root page number.
/// </summary>
internal readonly record struct MutationResult(bool Found, uint RootPage);
