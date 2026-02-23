// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

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
    private readonly Core.Trust.AgentInfo? _agent;
    private bool _completed;
    private bool _disposed;

    internal SharcWriteTransaction(SharcDatabase db, Transaction innerTx, Dictionary<string, uint>? rootCache = null, Core.Trust.AgentInfo? agent = null)
    {
        _db = db;
        _innerTx = innerTx;
        _rootCache = rootCache;
        _agent = agent;
    }

    /// <summary>
    /// Inserts a record into the given table within this transaction.
    /// Returns the assigned rowid.
    /// </summary>
    public long Insert(string tableName, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");
        
        var tableInfo = TryGetTableInfo(tableName);
        if (_agent != null)
        {
            Trust.EntitlementEnforcer.EnforceWrite(_agent, tableName, GetColumnNames(tableInfo, values.Length));
        }

        return SharcWriter.InsertCore(_innerTx, tableName, values, tableInfo, _rootCache);
    }

    /// <summary>
    /// Deletes a record by rowid within this transaction.
    /// Returns true if the row existed and was removed.
    /// </summary>
    public bool Delete(string tableName, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Transaction already completed.");

        if (_agent != null)
        {
            Trust.EntitlementEnforcer.EnforceWrite(_agent, tableName, null);
        }

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

        var tableInfo = TryGetTableInfo(tableName);
        if (_agent != null)
        {
            Trust.EntitlementEnforcer.EnforceWrite(_agent, tableName, GetColumnNames(tableInfo, values.Length));
        }

        return SharcWriter.UpdateCore(_innerTx, tableName, rowId, values, tableInfo, _rootCache);
    }

    private static string[]? GetColumnNames(TableInfo? table, int valueCount)
    {
        if (table == null || valueCount == 0) return null;
        var cols = new string[Math.Min(valueCount, table.Columns.Count)];
        for (int i = 0; i < cols.Length; i++)
            cols[i] = table.Columns[i].Name;
        return cols;
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
