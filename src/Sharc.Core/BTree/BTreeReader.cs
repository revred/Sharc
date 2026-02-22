// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core.Format;

namespace Sharc.Core.BTree;

/// <summary>
/// Creates cursors for iterating over table and index b-trees.
/// Generic over TPageSource to enable JIT devirtualization of page access calls.
/// </summary>
internal sealed class BTreeReader<TPageSource> : IBTreeReader
    where TPageSource : class, IPageSource
{
    private readonly TPageSource _pageSource;
    private readonly int _usablePageSize;

    /// <summary>
    /// Creates a new BTreeReader.
    /// </summary>
    /// <param name="pageSource">The page source for reading pages.</param>
    /// <param name="header">The parsed database header.</param>
    public BTreeReader(TPageSource pageSource, DatabaseHeader header)
    {
        _pageSource = pageSource;
        _usablePageSize = header.UsablePageSize;
    }

    /// <inheritdoc />
    public IBTreeCursor CreateCursor(uint rootPage)
    {
        return new BTreeCursor<TPageSource>(_pageSource, rootPage, _usablePageSize);
    }

    /// <inheritdoc />
    public IIndexBTreeCursor CreateIndexCursor(uint rootPage)
    {
        return new IndexBTreeCursor<TPageSource>(_pageSource, rootPage, _usablePageSize);
    }
}
