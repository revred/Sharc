using Sharc.Core;
using Sharc.Core.Schema;

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
    private readonly Dictionary<string, uint>? _rootCache;
    private bool _completed;
    private bool _disposed;

    internal SharcWriteTransaction(SharcDatabase db, Transaction innerTx, Dictionary<string, uint>? rootCache = null)
    {
        _db = db;
        _innerTx = innerTx;
        _rootCache = rootCache;
    }

    /// <summary>
    /// Inserts a record into the given table within this transaction.
    /// Returns the assigned rowid.
    /// </summary>
    public long Insert(string tableName, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        return SharcWriter.InsertCore(_innerTx, tableName, values, TryGetTableInfo(tableName), _rootCache);
    }

    /// <summary>
    /// Deletes a record by rowid within this transaction.
    /// Returns true if the row existed and was removed.
    /// </summary>
    public bool Delete(string tableName, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        return SharcWriter.DeleteCore(_innerTx, tableName, rowId, _rootCache);
    }

    /// <summary>
    /// Updates a record by rowid with new column values within this transaction.
    /// Returns true if the row existed and was updated.
    /// </summary>
    public bool Update(string tableName, long rowId, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        return SharcWriter.UpdateCore(_innerTx, tableName, rowId, values, TryGetTableInfo(tableName), _rootCache);
    }

    private TableInfo? TryGetTableInfo(string tableName)
    {
        var tables = _db.Schema.Tables;
        for (int i = 0; i < tables.Count; i++)
        {
            if (tables[i].Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                return tables[i];
        }
        return null;
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

    /// <summary>
    /// Executes a Data Definition Language (DDL) statement (e.g., CREATE TABLE, ALTER TABLE).
    /// </summary>
    public void Execute(string sql, Core.Trust.AgentInfo? agent = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");

        SharcSchemaWriter.Execute(_db, _innerTx, sql, agent);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        if (!_completed)
        {
            Rollback();
        }
        _innerTx.Dispose();
        _disposed = true;
    }
}
