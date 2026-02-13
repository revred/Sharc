using Sharc.Core;

namespace Sharc;

/// <summary>
/// An explicit write transaction. All inserts are buffered until 
/// <see cref="Commit"/> is called. On <see cref="Dispose"/> without
/// commit, changes are rolled back.
/// </summary>
public sealed class SharcWriteTransaction : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly Transaction _innerTx;
    private bool _completed;
    private bool _disposed;

    internal SharcWriteTransaction(SharcDatabase db, Transaction innerTx)
    {
        _db = db;
        _innerTx = innerTx;
    }

    /// <summary>
    /// Inserts a record into the given table within this transaction.
    /// Returns the assigned rowid.
    /// </summary>
    public long Insert(string tableName, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        return SharcWriter.InsertCore(_innerTx, tableName, values);
    }

    /// <summary>
    /// Commits all buffered writes to the database.
    /// </summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        _innerTx.Commit();
        _completed = true;
    }

    /// <summary>
    /// Discards all buffered writes.
    /// </summary>
    public void Rollback()
    {
        if (_disposed || _completed) return;
        _innerTx.Rollback();
        _completed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_completed) _innerTx.Rollback();
        _innerTx.Dispose();
    }
}
