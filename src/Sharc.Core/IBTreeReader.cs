namespace Sharc.Core;

/// <summary>
/// Reads SQLite b-tree structures, supporting sequential table scans.
/// </summary>
public interface IBTreeReader
{
    /// <summary>
    /// Creates a cursor for iterating over all cells in a table b-tree.
    /// </summary>
    /// <param name="rootPage">The root page number of the table b-tree.</param>
    /// <returns>A cursor positioned before the first cell.</returns>
    IBTreeCursor CreateCursor(uint rootPage);
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
}
