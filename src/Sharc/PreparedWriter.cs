// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;
using Sharc.Core.IO;
using Sharc.Core.Schema;

namespace Sharc;

/// <summary>
/// A pre-resolved writer handle that eliminates schema lookup and root page
/// scanning on repeated Insert, Delete, and Update calls. Created via
/// <see cref="SharcWriter.PrepareWriter(string)"/>.
/// </summary>
/// <remarks>
/// <para>At construction, <see cref="TableInfo"/> and the table's root page are resolved once.
/// Each mutation auto-commits via a pooled <see cref="ShadowPageSource"/>, matching the
/// <see cref="SharcWriter"/> pooling pattern.</para>
/// <para>Thread-safe: each thread gets its own <see cref="ShadowPageSource"/> cache via
/// <see cref="ThreadLocal{T}"/>. A single instance can be shared across N threads.</para>
/// </remarks>
public sealed class PreparedWriter : IPreparedWriter
{
    private readonly SharcDatabase _db;
    private readonly string _tableName;
    private readonly TableInfo? _tableInfo;
    private readonly Dictionary<string, uint> _rootCache;
    private volatile bool _disposed;

    // Per-thread cached ShadowPageSource
    private readonly ThreadLocal<WriterSlot> _slot;

    private sealed class WriterSlot : IDisposable
    {
        internal ShadowPageSource? CachedShadow;

        public void Dispose()
        {
            CachedShadow?.Dispose();
            CachedShadow = null;
        }
    }

    internal PreparedWriter(SharcDatabase db, string tableName, TableInfo? tableInfo,
        Dictionary<string, uint> rootCache)
    {
        _db = db;
        _tableName = tableName;
        _tableInfo = tableInfo;
        _rootCache = rootCache;
        _slot = new ThreadLocal<WriterSlot>(trackAllValues: true);
    }

    /// <inheritdoc/>
    public long Insert(params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var db = _db;
        var slot = GetSlot();
        using var tx = BeginAutoCommitTransaction(db, slot);
        long rowId = SharcWriter.InsertCore(tx, _tableName, values, _tableInfo, _rootCache);
        CapturePooledShadow(tx, slot);
        tx.Commit();
        return rowId;
    }

    /// <inheritdoc/>
    public bool Delete(long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var db = _db;
        var slot = GetSlot();
        using var tx = BeginAutoCommitTransaction(db, slot);
        bool found = SharcWriter.DeleteCore(tx, _tableName, rowId, _rootCache);
        CapturePooledShadow(tx, slot);
        tx.Commit();
        return found;
    }

    /// <inheritdoc/>
    public bool Update(long rowId, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var db = _db;
        var slot = GetSlot();
        using var tx = BeginAutoCommitTransaction(db, slot);
        bool found = SharcWriter.UpdateCore(tx, _tableName, rowId, values, _tableInfo, _rootCache);
        CapturePooledShadow(tx, slot);
        tx.Commit();
        return found;
    }

    private WriterSlot GetSlot() => _slot.Value ??= new WriterSlot();

    private static Transaction BeginAutoCommitTransaction(SharcDatabase db, WriterSlot slot)
    {
        if (slot.CachedShadow == null)
            return db.BeginTransaction();

        return db.BeginPooledTransaction(slot.CachedShadow);
    }

    private static void CapturePooledShadow(Transaction tx, WriterSlot slot)
    {
        slot.CachedShadow ??= tx.GetShadowSource();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up all per-thread slots
        foreach (var slot in _slot.Values)
            slot.Dispose();

        _slot.Dispose();
    }
}
