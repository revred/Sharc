using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Core.Trust;

namespace Sharc;

/// <summary>
/// Public API for writing to a Sharc database.
/// Wraps <see cref="SharcDatabase"/> with typed write operations.
/// </summary>
public sealed class SharcWriter : IDisposable
{
    private readonly SharcDatabase _db;
    private readonly bool _ownsDb;
    private bool _disposed;

    /// <summary>
    /// Opens a database for reading and writing.
    /// </summary>
    public static SharcWriter Open(string path)
    {
        var db = SharcDatabase.Open(path, new SharcOpenOptions { Writable = true });
        return new SharcWriter(db, ownsDb: true);
    }

    /// <summary>
    /// Wraps an existing <see cref="SharcDatabase"/> for write operations.
    /// The caller retains ownership of the database.
    /// </summary>
    public static SharcWriter From(SharcDatabase db)
    {
        return new SharcWriter(db, ownsDb: false);
    }

    private SharcWriter(SharcDatabase db, bool ownsDb)
    {
        _db = db;
        _ownsDb = ownsDb;
    }

    /// <summary>
    /// Gets the underlying database for read operations.
    /// </summary>
    public SharcDatabase Database => _db;

    /// <summary>
    /// Inserts a single record into the given table. Auto-commits.
    /// Returns the assigned rowid.
    /// </summary>
    public long Insert(string tableName, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var tx = _db.BeginTransaction();
        long rowId = InsertCore(tx, tableName, values, TryGetTableInfo(tableName));
        tx.Commit();
        return rowId;
    }

    /// <summary>
    /// Inserts a single record with agent write-scope enforcement. Auto-commits.
    /// Throws <see cref="UnauthorizedAccessException"/> if the agent's WriteScope denies access.
    /// </summary>
    public long Insert(AgentInfo agent, string tableName, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, null);

        using var tx = _db.BeginTransaction();
        long rowId = InsertCore(tx, tableName, values, TryGetTableInfo(tableName));
        tx.Commit();
        return rowId;
    }

    /// <summary>
    /// Inserts multiple records in a single transaction.
    /// Returns the assigned rowids.
    /// </summary>
    public long[] InsertBatch(string tableName, IEnumerable<ColumnValue[]> records)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tableInfo = TryGetTableInfo(tableName);
        var rowIds = records is ICollection<ColumnValue[]> coll ? new List<long>(coll.Count) : new List<long>();
        using var tx = _db.BeginTransaction();
        foreach (var values in records)
        {
            rowIds.Add(InsertCore(tx, tableName, values, tableInfo));
        }
        tx.Commit();
        return rowIds.ToArray();
    }

    /// <summary>
    /// Inserts multiple records with agent write-scope enforcement.
    /// Throws <see cref="UnauthorizedAccessException"/> if the agent's WriteScope denies access.
    /// </summary>
    public long[] InsertBatch(AgentInfo agent, string tableName, IEnumerable<ColumnValue[]> records)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, null);

        var tableInfo = TryGetTableInfo(tableName);
        var rowIds = records is ICollection<ColumnValue[]> coll ? new List<long>(coll.Count) : new List<long>();
        using var tx = _db.BeginTransaction();
        foreach (var values in records)
        {
            rowIds.Add(InsertCore(tx, tableName, values, tableInfo));
        }
        tx.Commit();
        return rowIds.ToArray();
    }

    /// <summary>
    /// Deletes a single record by rowid. Auto-commits.
    /// Returns true if the row existed and was removed.
    /// </summary>
    public bool Delete(string tableName, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var tx = _db.BeginTransaction();
        bool found = DeleteCore(tx, tableName, rowId);
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Deletes a single record with agent write-scope enforcement. Auto-commits.
    /// Throws <see cref="UnauthorizedAccessException"/> if the agent's WriteScope denies access.
    /// </summary>
    public bool Delete(AgentInfo agent, string tableName, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, null);

        using var tx = _db.BeginTransaction();
        bool found = DeleteCore(tx, tableName, rowId);
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Updates a single record by rowid with new column values. Auto-commits.
    /// Returns true if the row existed and was updated.
    /// </summary>
    public bool Update(string tableName, long rowId, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var tx = _db.BeginTransaction();
        bool found = UpdateCore(tx, tableName, rowId, values, TryGetTableInfo(tableName));
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Updates a single record with agent write-scope enforcement. Auto-commits.
    /// Throws <see cref="UnauthorizedAccessException"/> if the agent's WriteScope denies access.
    /// </summary>
    public bool Update(AgentInfo agent, string tableName, long rowId, params ColumnValue[] values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, null);

        using var tx = _db.BeginTransaction();
        bool found = UpdateCore(tx, tableName, rowId, values, TryGetTableInfo(tableName));
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Begins an explicit write transaction.
    /// </summary>
    public SharcWriteTransaction BeginTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tx = _db.BeginTransaction();
        return new SharcWriteTransaction(_db, tx);
    }

    /// <summary>
    /// Core insert: encode record → insert into B-tree via the transaction's shadow source.
    /// </summary>
    internal static long InsertCore(Transaction tx, string tableName, ColumnValue[] values,
        TableInfo? tableInfo = null)
    {
        var shadow = tx.GetShadowSource();
        int pageSize = shadow.PageSize;
        int usableSize = pageSize; // TODO: subtract reserved bytes if applicable

        // Expand merged GUID columns to physical hi/lo Int64 pairs
        if (tableInfo != null)
            values = ExpandMergedColumns(values, tableInfo);

        // Find table root page from schema (read from the base source's schema)
        // For now, we traverse sqlite_master manually from page 1
        uint rootPage = FindTableRootPage(shadow, tableName, usableSize);
        if (rootPage == 0)
            throw new InvalidOperationException($"Table '{tableName}' not found.");

        var mutator = tx.FetchMutator(usableSize);
        long rowId = mutator.GetMaxRowId(rootPage) + 1;

        // Encode the record
        int encodedSize = RecordEncoder.ComputeEncodedSize(values);
        Span<byte> recordBuf = encodedSize <= 512 ? stackalloc byte[encodedSize] : new byte[encodedSize];
        RecordEncoder.EncodeRecord(values, recordBuf);

        // Insert into B-tree
        uint newRoot = mutator.Insert(rootPage, rowId, recordBuf);

        // If the root page changed (due to root split), we need to update sqlite_master.
        // For Phase 1, we only support tables where the root doesn't change,
        // or we handle root change by updating the schema page.
        if (newRoot != rootPage)
        {
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);
        }

        return rowId;
    }

    /// <summary>
    /// Core delete: find table root → mutator.Delete → update root if changed.
    /// </summary>
    internal static bool DeleteCore(Transaction tx, string tableName, long rowId)
    {
        var shadow = tx.GetShadowSource();
        int usableSize = shadow.PageSize;

        uint rootPage = FindTableRootPage(shadow, tableName, usableSize);
        if (rootPage == 0)
            throw new InvalidOperationException($"Table '{tableName}' not found.");

        var mutator = tx.FetchMutator(usableSize);
        var (found, newRoot) = mutator.Delete(rootPage, rowId);

        if (found && newRoot != rootPage)
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);

        return found;
    }

    /// <summary>
    /// Core update: find table root → encode record → mutator.Update → update root if changed.
    /// </summary>
    internal static bool UpdateCore(Transaction tx, string tableName, long rowId, ColumnValue[] values,
        TableInfo? tableInfo = null)
    {
        var shadow = tx.GetShadowSource();
        int usableSize = shadow.PageSize;

        // Expand merged GUID columns to physical hi/lo Int64 pairs
        if (tableInfo != null)
            values = ExpandMergedColumns(values, tableInfo);

        uint rootPage = FindTableRootPage(shadow, tableName, usableSize);
        if (rootPage == 0)
            throw new InvalidOperationException($"Table '{tableName}' not found.");

        int encodedSize = RecordEncoder.ComputeEncodedSize(values);
        Span<byte> recordBuf = encodedSize <= 512 ? stackalloc byte[encodedSize] : new byte[encodedSize];
        RecordEncoder.EncodeRecord(values, recordBuf);

        var mutator = tx.FetchMutator(usableSize);
        var (found, newRoot) = mutator.Update(rootPage, rowId, recordBuf);

        if (found && newRoot != rootPage)
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);

        return found;
    }

    /// <summary>
    /// Looks up a table's metadata from the database schema.
    /// Returns null if the table is not found (caller falls back to schema-less path).
    /// </summary>
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
    /// Finds the root page of a table by scanning sqlite_master (page 1).
    /// </summary>
    private static uint FindTableRootPage(IPageSource source, string tableName, int usableSize)
    {
        using var cursor = new BTreeCursor(source, 1, usableSize);
        var columnBuffer = new ColumnValue[5];
        var decoder = new RecordDecoder();

        while (cursor.MoveNext())
        {
            decoder.DecodeRecord(cursor.Payload, columnBuffer);
            if (columnBuffer[0].IsNull) continue;

            string type = columnBuffer[0].AsString();
            if (type != "table") continue;

            string name = columnBuffer[1].IsNull ? "" : columnBuffer[1].AsString();
            if (string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                return columnBuffer[3].IsNull ? 0u : (uint)columnBuffer[3].AsInt64();
            }
        }

        return 0;
    }

    /// <summary>
    /// Updates the root page of a table in sqlite_master after a root split.
    /// </summary>
    /// <remarks>
    /// PHASE 1 LIMITATION: Root page updates are not yet persisted to sqlite_master.
    /// This means operations that trigger a B-tree root split will not be reflected
    /// in the schema table. In practice, root splits only occur during Insert when a
    /// leaf page overflows — Delete and Update do not cause root splits because they
    /// do not increase tree size. For tables with existing data, root splits are rare
    /// unless bulk-inserting enough rows to overflow the initial leaf page.
    /// This will be fully implemented in Phase 2 (sqlite_master record re-encoding).
    /// </remarks>
    private static void UpdateTableRootPage(IWritablePageSource source, string tableName,
        uint newRootPage, int usableSize)
    {
        // TODO(phase2): Read page 1, find the sqlite_master entry for this table,
        // re-encode the record with the new rootpage value, and update the cell.
    }

    /// <summary>
    /// Expands logical column values into physical column values for tables with merged columns.
    /// GUID values at merged column positions are split into hi/lo Int64 pairs.
    /// Returns the same array unchanged if the table has no merged columns (fast path).
    /// </summary>
    internal static ColumnValue[] ExpandMergedColumns(ColumnValue[] logical, TableInfo table)
    {
        if (!table.HasMergedColumns) return logical;

        var physical = new ColumnValue[table.PhysicalColumnCount];
        var columns = table.Columns;

        for (int i = 0; i < columns.Count && i < logical.Length; i++)
        {
            var col = columns[i];
            if (col.IsMergedGuidColumn)
            {
                var mergedOrdinals = col.MergedPhysicalOrdinals!;
                if (logical[i].IsNull)
                {
                    physical[mergedOrdinals[0]] = ColumnValue.Null();
                    physical[mergedOrdinals[1]] = ColumnValue.Null();
                }
                else
                {
                    var guid = logical[i].AsGuid();
                    var (hi, lo) = ColumnValue.SplitGuidForMerge(guid);
                    physical[mergedOrdinals[0]] = hi;
                    physical[mergedOrdinals[1]] = lo;
                }
            }
            else
            {
                int physOrd = col.MergedPhysicalOrdinals?[0] ?? col.Ordinal;
                physical[physOrd] = logical[i];
            }
        }

        return physical;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsDb) _db.Dispose();
    }
}
