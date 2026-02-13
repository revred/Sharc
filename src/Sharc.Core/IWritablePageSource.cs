namespace Sharc.Core;

/// <summary>
/// Extends IPageSource with write capabilities.
/// </summary>
public interface IWritablePageSource : IPageSource
{
    /// <summary>
    /// Writes data to a page. Changes may be buffered until Commit or Flush.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="source">Source buffer containing page data.</param>
    void WritePage(uint pageNumber, ReadOnlySpan<byte> source);

    /// <summary>
    /// Ensures all pending writes are persisted to the underlying storage.
    /// </summary>
    void Flush();
}
