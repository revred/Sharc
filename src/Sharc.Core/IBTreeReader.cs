// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Core;

/// <summary>
/// Reads SQLite b-tree structures, supporting sequential table scans and index scans.
/// </summary>
public interface IBTreeReader
{
    /// <summary>
    /// Creates a cursor for iterating over all cells in a table b-tree.
    /// </summary>
    /// <param name="rootPage">The root page number of the table b-tree.</param>
    /// <returns>A cursor positioned before the first cell.</returns>
    IBTreeCursor CreateCursor(uint rootPage);

    /// <summary>
    /// Creates a scan-optimized cursor that pre-collects leaf page numbers for faster
    /// sequential iteration. Eliminates B-tree stack navigation overhead during scan.
    /// Does not support <see cref="IBTreeCursor.Seek"/> or <see cref="IBTreeCursor.MoveLast"/>.
    /// </summary>
    /// <param name="rootPage">The root page number of the table b-tree.</param>
    /// <returns>A scan-optimized cursor positioned before the first cell.</returns>
    IBTreeCursor CreateScanCursor(uint rootPage) => CreateCursor(rootPage);

    /// <summary>
    /// Creates a cursor for iterating over all entries in an index b-tree.
    /// </summary>
    /// <param name="rootPage">The root page number of the index b-tree.</param>
    /// <returns>A cursor positioned before the first entry.</returns>
    IIndexBTreeCursor CreateIndexCursor(uint rootPage);
}

/// <summary>
/// Forward-only cursor over b-tree leaf cells.
/// </summary>
public interface IBTreeCursor : IDisposable
{
    /// <summary>
    /// Advances to the next cell.
    /// </summary>
    /// <returns>True if a cell is available; false at end of tree.</returns>
    bool MoveNext();

    /// <summary>
    /// Resets the cursor to its initial state before the first cell.
    /// </summary>
    void Reset();

    /// <summary>
    /// Moves the cursor to the last cell in the table (highest rowid).
    /// </summary>
    /// <returns>True if the table is not empty.</returns>
    bool MoveLast();

    /// <summary>
    /// Repositions the cursor to the specified rowid (or the first rowid >= key).
    /// </summary>
    /// <param name="rowId">The rowid to seek to.</param>
    /// <returns>True if an exact match is found.</returns>
    bool Seek(long rowId);

    /// <summary>
    /// Gets the rowid of the current cell (table b-trees only).
    /// </summary>
    long RowId { get; }

    /// <summary>
    /// Gets the payload data of the current cell.
    /// May involve reading overflow pages. Valid until <see cref="MoveNext"/> is called.
    /// </summary>
    ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// Gets the total payload size in bytes (including overflow).
    /// </summary>
    int PayloadSize { get; }

    /// <summary>
    /// Returns true if the underlying page source has been mutated since this cursor
    /// was created or last refreshed (via <see cref="Reset"/> or <see cref="Seek"/>).
    /// Returns false for read-only sources that cannot track mutations.
    /// </summary>
    bool IsStale { get; }
}

/// <summary>
/// Forward-only cursor over index b-tree leaf cells.
/// Index payloads are standard SQLite records where the last column is the table rowid.
/// </summary>
public interface IIndexBTreeCursor : IDisposable
{
    /// <summary>
    /// Advances to the next index entry.
    /// </summary>
    /// <returns>True if an entry is available; false at end of tree.</returns>
    bool MoveNext();

    /// <summary>
    /// Resets the cursor to its initial state before the first entry.
    /// </summary>
    void Reset();

    /// <summary>
    /// Seeks to the first index entry whose first column (integer) equals or exceeds
    /// <paramref name="firstColumnKey"/>. Uses binary search through the B-tree for O(log n) access.
    /// After calling this method, <see cref="Payload"/> points to the first matching entry
    /// (if found), and subsequent <see cref="MoveNext"/> calls continue from that position.
    /// </summary>
    /// <param name="firstColumnKey">The integer value to seek for in the first column of the index.</param>
    /// <returns>True if an entry with first column == <paramref name="firstColumnKey"/> was found.</returns>
    bool SeekFirst(long firstColumnKey);

    /// <summary>
    /// Gets the payload data of the current index entry.
    /// The payload is a standard SQLite record where the last column is the table rowid.
    /// May involve reading overflow pages. Valid until <see cref="MoveNext"/> is called.
    /// </summary>
    ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// Gets the total payload size in bytes (including overflow).
    /// </summary>
    int PayloadSize { get; }

    /// <summary>
    /// Returns true if the underlying page source has been mutated since this cursor
    /// was created or last refreshed (via <see cref="Reset"/> or <see cref="SeekFirst"/>).
    /// Returns false for read-only sources that cannot track mutations.
    /// </summary>
    bool IsStale { get; }
}