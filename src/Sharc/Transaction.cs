using Sharc.Core;
using Sharc.Core.IO;
using Sharc.Exceptions;

namespace Sharc;

/// <summary>
/// Represents an ACID transaction in Sharc.
/// All writes performed during the transaction are buffered in memory and 
/// only persisted to the underlying storage upon <see cref="Commit"/>.
/// </summary>
public sealed class Transaction : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly IWritablePageSource _baseSource;
    private readonly ShadowPageSource _shadowSource;
    private bool _isCompleted;
    private bool _disposed;

    /// <summary>
    /// Gets the transaction-aware page source.
    /// All reads check buffered writes first, and all writes are buffered here.
    /// </summary>
    public IPageSource PageSource => _shadowSource;

    /// <summary>
    /// Returns the shadow page source for advanced write operations.
    /// </summary>
    internal ShadowPageSource GetShadowSource() => _shadowSource;

    internal Transaction(SharcDatabase db, IWritablePageSource baseSource)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _baseSource = baseSource ?? throw new ArgumentNullException(nameof(baseSource));
        _shadowSource = new ShadowPageSource(baseSource);
    }

    /// <summary>
    /// Persists all buffered changes to the underlying storage.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The transaction has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The transaction has already been committed or rolled back.</exception>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCompleted) throw new InvalidOperationException("Transaction already completed.");

        try
        {
            var dirtyPages = _shadowSource.GetDirtyPages();
            if (dirtyPages.Count == 0)
            {
                _isCompleted = true;
                _db.EndTransaction(this);
                return;
            }

            string? journalPath = null;
            if (_db.FilePath != null)
            {
                journalPath = _db.FilePath + ".journal";
                RollbackJournal.CreateJournal(journalPath, _baseSource, dirtyPages.Keys);
            }

            foreach (var (pageNumber, data) in dirtyPages)
            {
                _baseSource.WritePage(pageNumber, data);
            }
            _baseSource.Flush();

            if (journalPath != null && File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            _isCompleted = true;
            _db.EndTransaction(this);
        }
        catch (Exception ex)
        {
            throw new SharcException("Failed to commit transaction. Changes may not be fully persisted.", ex);
        }
    }

    /// <summary>
    /// Discards all buffered changes.
    /// </summary>
    public void Rollback()
    {
        if (_disposed || _isCompleted) return;
        _shadowSource.ClearShadow();
        _isCompleted = true;
        _db.EndTransaction(this);
    }

    /// <summary>
    /// Writes a page to the transaction buffer.
    /// </summary>
    internal void WritePage(uint pageNumber, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCompleted) throw new InvalidOperationException("Transaction already completed.");
        _shadowSource.WritePage(pageNumber, source);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        if (!_isCompleted)
        {
            Rollback();
        }
        _shadowSource.Dispose();
        _disposed = true;
    }
}
