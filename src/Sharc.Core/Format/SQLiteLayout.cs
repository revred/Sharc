namespace Sharc.Core.Format;

/// <summary>
/// Central registry for SQLite file format constants and layout magic numbers.
/// </summary>
public static class SQLiteLayout
{
    /// <summary>
    /// The size of the SQLite database header in bytes.
    /// </summary>
    public const int DatabaseHeaderSize = 100;

    /// <summary>
    /// Offset of the Right Child Page in an interior B-tree page header.
    /// </summary>
    public const int RightChildPageOffset = 8;

    /// <summary>
    /// The size of a page number reference (uint).
    /// </summary>
    public const int PagePointerSize = 4;

    /// <summary>
    /// The offset of the first overflow page pointer in a leaf cell.
    /// </summary>
    public const int OverflowPointerSize = 4;

    /// <summary>
    /// Minimum payload size for a leaf cell before overflow (SQLite specific fraction).
    /// </summary>
    public const int MinLocalRatio = 32;

    /// <summary>
    /// Maximum payload size for a leaf cell before overflow (SQLite specific fraction).
    /// </summary>
    public const int MaxLocalRatio = 64;

    /// <summary>
    /// Header size for an interior table page.
    /// </summary>
    public const int TableInteriorHeaderSize = 12;

    /// <summary>
    /// Header size for a leaf table page.
    /// </summary>
    public const int TableLeafHeaderSize = 8;

    /// <summary>
    /// Header size for an interior index page.
    /// </summary>
    public const int IndexInteriorHeaderSize = 12;

    /// <summary>
    /// Header size for a leaf index page.
    /// </summary>
    public const int IndexLeafHeaderSize = 8;
}
