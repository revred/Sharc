// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using Sharc.Core;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
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
    private readonly Dictionary<string, uint> _tableRootCache = new(StringComparer.OrdinalIgnoreCase);
    private ShadowPageSource? _cachedShadow;
    private bool _disposed;

    /// <summary>Number of cached table root pages. Exposed for test observability.</summary>
    internal int TableRootCacheCount => _tableRootCache.Count;

    /// <summary>
    /// Opens a database for reading and writing.
    /// </summary>
    public static SharcWriter Open(string path)
    {
        // Write-appropriate cache: BTreeMutator._pageCache handles intra-transaction pages;
        // LRU only serves cross-transaction reads for schema + root pages.
        // 16 pages × 4096 = 64 KB maximum, demand-grown from 0.
        var db = SharcDatabase.Open(path, new SharcOpenOptions { Writable = true, PageCacheSize = 16 });
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
    /// Begins an auto-commit transaction using a pooled ShadowPageSource.
    /// The shadow is created on first use, then Reset and reused across transactions.
    /// A fresh BTreeMutator is always created per-transaction to avoid stale freelist state.
    /// </summary>
    private Transaction BeginAutoCommitTransaction()
    {
        if (_cachedShadow == null)
            return _db.BeginTransaction();

        return _db.BeginPooledTransaction(_cachedShadow);
    }

    /// <summary>
    /// Captures the ShadowPageSource from a completed auto-commit transaction for reuse.
    /// </summary>
    private void CapturePooledShadow(Transaction tx)
    {
        _cachedShadow ??= tx.GetShadowSource();
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

        using var tx = BeginAutoCommitTransaction();
        long rowId = InsertCore(tx, tableName, values, TryGetTableInfo(tableName), _tableRootCache);
        CapturePooledShadow(tx);
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
        var tableInfo = TryGetTableInfo(tableName);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, GetColumnNames(tableInfo, values.Length));

        using var tx = BeginAutoCommitTransaction();
        long rowId = InsertCore(tx, tableName, values, tableInfo, _tableRootCache);
        CapturePooledShadow(tx);
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
        using var tx = BeginAutoCommitTransaction();
        foreach (var values in records)
        {
            rowIds.Add(InsertCore(tx, tableName, values, tableInfo, _tableRootCache));
        }
        CapturePooledShadow(tx);
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
        var tableInfo = TryGetTableInfo(tableName);
        
        // Peek first record to get column count for enforcement
        int colCount = records is IReadOnlyList<ColumnValue[]> list && list.Count > 0 ? list[0].Length : 0;
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, GetColumnNames(tableInfo, colCount));

        var rowIds = records is ICollection<ColumnValue[]> coll ? new List<long>(coll.Count) : new List<long>();
        using var tx = BeginAutoCommitTransaction();
        foreach (var values in records)
        {
            rowIds.Add(InsertCore(tx, tableName, values, tableInfo, _tableRootCache));
        }
        CapturePooledShadow(tx);
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

        using var tx = BeginAutoCommitTransaction();
        bool found = DeleteCore(tx, tableName, rowId, _tableRootCache);
        CapturePooledShadow(tx);
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
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, null); // Delete usually requires all-columns or just table write

        using var tx = BeginAutoCommitTransaction();
        bool found = DeleteCore(tx, tableName, rowId, _tableRootCache);
        CapturePooledShadow(tx);
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

        using var tx = BeginAutoCommitTransaction();
        bool found = UpdateCore(tx, tableName, rowId, values, TryGetTableInfo(tableName), _tableRootCache);
        CapturePooledShadow(tx);
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
        var tableInfo = TryGetTableInfo(tableName);
        Trust.EntitlementEnforcer.EnforceWrite(agent, tableName, GetColumnNames(tableInfo, values.Length));

        using var tx = BeginAutoCommitTransaction();
        bool found = UpdateCore(tx, tableName, rowId, values, tableInfo, _tableRootCache);
        CapturePooledShadow(tx);
        tx.Commit();
        return found;
    }

    /// <summary>
    /// Creates a <see cref="PreparedWriter"/> for zero-overhead repeated writes to the given table.
    /// Schema and root page are resolved once at creation time.
    /// </summary>
    /// <param name="tableName">The table to write to.</param>
    /// <returns>A reusable <see cref="PreparedWriter"/> bound to the table.</returns>
    public PreparedWriter PrepareWriter(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tableInfo = TryGetTableInfo(tableName);
        return new PreparedWriter(_db, tableName, tableInfo, _tableRootCache);
    }

    /// <summary>
    /// Begins an explicit write transaction.
    /// </summary>
    public SharcWriteTransaction BeginTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tx = _db.BeginTransaction();
        return new SharcWriteTransaction(_db, tx, _tableRootCache);
    }

    /// <summary>
    /// Begins an explicit write transaction bound to an agent for entitlement enforcement.
    /// </summary>
    public SharcWriteTransaction BeginTransaction(AgentInfo agent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tx = _db.BeginTransaction();
        return new SharcWriteTransaction(_db, tx, _tableRootCache, agent);
    }

    /// <summary>
    /// Core insert: encode record → insert into B-tree via the transaction's shadow source.
    /// </summary>
    internal static long InsertCore(Transaction tx, string tableName, ColumnValue[] values,
        TableInfo? tableInfo = null, Dictionary<string, uint>? rootCache = null)
    {
        var shadow = tx.GetShadowSource();
        int pageSize = shadow.PageSize;
        int usableSize = pageSize; // Reserved bytes are zero for all Sharc-created databases

        // Expand merged GUID columns to physical hi/lo Int64 pairs
        if (tableInfo != null)
            values = ExpandMergedColumns(values, tableInfo);

        uint rootPage = FindTableRootPageCached(shadow, tableName, usableSize, rootCache);
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

        if (newRoot != rootPage)
        {
            if (rootCache != null) rootCache[tableName] = newRoot;
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);
        }

        return rowId;
    }

    /// <summary>
    /// Core delete: find table root → mutator.Delete → update root if changed.
    /// </summary>
    internal static bool DeleteCore(Transaction tx, string tableName, long rowId,
        Dictionary<string, uint>? rootCache = null)
    {
        var shadow = tx.GetShadowSource();
        int usableSize = shadow.PageSize;

        uint rootPage = FindTableRootPageCached(shadow, tableName, usableSize, rootCache);
        if (rootPage == 0)
            throw new InvalidOperationException($"Table '{tableName}' not found.");

        var mutator = tx.FetchMutator(usableSize);
        var (found, newRoot) = mutator.Delete(rootPage, rowId);

        if (found && newRoot != rootPage)
        {
            if (rootCache != null) rootCache[tableName] = newRoot;
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);
        }

        return found;
    }

    /// <summary>
    /// Core update: find table root → encode record → mutator.Update → update root if changed.
    /// </summary>
    internal static bool UpdateCore(Transaction tx, string tableName, long rowId, ColumnValue[] values,
        TableInfo? tableInfo = null, Dictionary<string, uint>? rootCache = null)
    {
        var shadow = tx.GetShadowSource();
        int usableSize = shadow.PageSize;

        // Expand merged GUID columns to physical hi/lo Int64 pairs
        if (tableInfo != null)
            values = ExpandMergedColumns(values, tableInfo);

        uint rootPage = FindTableRootPageCached(shadow, tableName, usableSize, rootCache);
        if (rootPage == 0)
            throw new InvalidOperationException($"Table '{tableName}' not found.");

        int encodedSize = RecordEncoder.ComputeEncodedSize(values);
        Span<byte> recordBuf = encodedSize <= 512 ? stackalloc byte[encodedSize] : new byte[encodedSize];
        RecordEncoder.EncodeRecord(values, recordBuf);

        var mutator = tx.FetchMutator(usableSize);
        var (found, newRoot) = mutator.Update(rootPage, rowId, recordBuf);

        if (found && newRoot != rootPage)
        {
            if (rootCache != null) rootCache[tableName] = newRoot;
            UpdateTableRootPage(shadow, tableName, newRoot, usableSize);
        }

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

    private static string[]? GetColumnNames(TableInfo? table, int valueCount)
    {
        if (table == null || valueCount == 0) return null;
        var cols = new string[Math.Min(valueCount, table.Columns.Count)];
        for (int i = 0; i < cols.Length; i++)
            cols[i] = table.Columns[i].Name;
        return cols;
    }

    /// <summary>
    /// Checks the cache first, then falls back to scanning sqlite_master.
    /// </summary>
    private static uint FindTableRootPageCached(IPageSource source, string tableName,
        int usableSize, Dictionary<string, uint>? cache)
    {
        if (cache != null && cache.TryGetValue(tableName, out uint cached))
            return cached;

        uint rootPage = FindTableRootPage(source, tableName, usableSize);
        if (cache != null && rootPage != 0)
            cache[tableName] = rootPage;

        return rootPage;
    }

    // Reusable scratch buffer and decoder for FindTableRootPage — avoids per-call allocations.
    // ThreadStatic ensures thread safety without locking.
    [ThreadStatic] private static ColumnValue[]? t_schemaColumnBuffer;
    [ThreadStatic] private static RecordDecoder? t_schemaDecoder;

    /// <summary>
    /// Finds the root page of a table by scanning sqlite_master (page 1).
    /// </summary>
    private static uint FindTableRootPage(IPageSource source, string tableName, int usableSize)
    {
        using var cursor = new BTreeCursor<IPageSource>(source, 1, usableSize);
        var columnBuffer = t_schemaColumnBuffer ??= new ColumnValue[5];
        var decoder = t_schemaDecoder ??= new RecordDecoder();

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
    /// Scans page 1 to find the matching sqlite_master row, re-encodes the record
    /// with the new rootpage value, and updates it via BTreeMutator.
    /// </summary>
    private static void UpdateTableRootPage(IWritablePageSource source, string tableName,
        uint newRootPage, int usableSize)
    {
        using var cursor = new BTreeCursor<IWritablePageSource>(source, 1, usableSize);
        var columnBuffer = new ColumnValue[5];
        var decoder = new RecordDecoder();

        while (cursor.MoveNext())
        {
            decoder.DecodeRecord(cursor.Payload, columnBuffer);
            if (columnBuffer[0].IsNull) continue;

            string type = columnBuffer[0].AsString();
            if (type != "table" && type != "index") continue;

            string name = columnBuffer[1].IsNull ? "" : columnBuffer[1].AsString();
            if (!string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Found the matching sqlite_master entry — re-encode with new rootpage
            long rowId = cursor.RowId;

            // Preserve all columns except rootpage (column 3)
            var record = new ColumnValue[5];
            record[0] = columnBuffer[0];
            record[1] = columnBuffer[1];
            record[2] = columnBuffer[2];
            record[3] = ColumnValue.FromInt64(1, (long)newRootPage);
            record[4] = columnBuffer[4];

            int size = RecordEncoder.ComputeEncodedSize(record);
            Span<byte> buf = size <= 512 ? stackalloc byte[size] : new byte[size];
            RecordEncoder.EncodeRecord(record, buf);

            // Must dispose cursor before mutating the same B-tree it's reading
            cursor.Dispose();

            using var mutator = new BTreeMutator(source, usableSize);
            mutator.Update(1, rowId, buf);
            return;
        }
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

    /// <summary>
    /// Compacts the database by rebuilding all tables, removing fragmentation and clearing the freelist.
    /// After vacuum, the database file size reflects only the pages actually in use.
    /// </summary>
    public void Vacuum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var schema = _db.Schema;
        int pageSize = _db.Header.PageSize;
        int usableSize = _db.UsablePageSize;

        // 1. Read all table data (raw record bytes + count)
        int userTableCount = Math.Max(schema.Tables.Count - 1, 0);
        var tableRows = new List<(TableInfo Table, List<byte[]> Records)>(userTableCount);
        foreach (var table in schema.Tables)
        {
            if (table.Name.Equals("sqlite_master", StringComparison.OrdinalIgnoreCase)) continue;
            var records = new List<byte[]>();
            using var cursor = new BTreeCursor<IPageSource>(_db.PageSource, (uint)table.RootPage, usableSize);
            while (cursor.MoveNext())
            {
                records.Add(cursor.Payload.ToArray());
            }
            tableRows.Add((table, records));
        }

        // 2. Build fresh database with same schema
        int schemaPageCount = 1 + tableRows.Count; // page 1 (schema) + one page per table
        var freshData = new byte[pageSize * schemaPageCount];

        // Build header
        var oldHeader = _db.Header;
        var newHeader = new DatabaseHeader(
            pageSize, oldHeader.WriteVersion, oldHeader.ReadVersion,
            oldHeader.ReservedBytesPerPage, oldHeader.ChangeCounter + 1,
            schemaPageCount, 0, 0, // freelist cleared
            oldHeader.SchemaCookie, oldHeader.SchemaFormat,
            oldHeader.TextEncoding, oldHeader.UserVersion,
            oldHeader.ApplicationId, oldHeader.SqliteVersionNumber);
        DatabaseHeader.Write(freshData, newHeader);

        // Build sqlite_master entries (schema page on page 1)
        var schemaCells = new List<(int Size, byte[] Cell)>(tableRows.Count);
        var tableTypeBytes = "table"u8.ToArray(); // cached — same for every entry
        var schemaCols = new ColumnValue[5]; // reuse across iterations
        for (int i = 0; i < tableRows.Count; i++)
        {
            var table = tableRows[i].Table;
            uint rootPage = (uint)(i + 2); // tables start at page 2
            var sqlBytes = Encoding.UTF8.GetBytes(table.Sql);
            var nameBytes = Encoding.UTF8.GetBytes(table.Name);

            schemaCols[0] = ColumnValue.Text(2 * 5 + 13, tableTypeBytes);
            schemaCols[1] = ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes);
            schemaCols[2] = ColumnValue.Text(2 * nameBytes.Length + 13, nameBytes);
            schemaCols[3] = ColumnValue.FromInt64(1, (long)rootPage);
            schemaCols[4] = ColumnValue.Text(2 * sqlBytes.Length + 13, sqlBytes);

            int recSize = RecordEncoder.ComputeEncodedSize(schemaCols);
            var recBuf = new byte[recSize];
            RecordEncoder.EncodeRecord(schemaCols, recBuf);

            long rowId = i + 1;
            int cellSize = CellBuilder.ComputeTableLeafCellSize(rowId, recSize, usableSize);
            var cellBuf = new byte[cellSize];
            CellBuilder.BuildTableLeafCell(rowId, recBuf, cellBuf, usableSize);
            schemaCells.Add((cellSize, cellBuf));
        }

        // Write schema cells to page 1 (after the 100-byte database header)
        int hdrOff = SQLiteLayout.DatabaseHeaderSize; // 100
        ushort cellContentOffset = (ushort)pageSize;
        for (int i = 0; i < schemaCells.Count; i++)
        {
            var (size, cell) = schemaCells[i];
            cellContentOffset -= (ushort)size;
            cell.CopyTo(freshData.AsSpan(cellContentOffset));
            // Write cell pointer
            int ptrOff = hdrOff + SQLiteLayout.TableLeafHeaderSize + i * SQLiteLayout.CellPointerSize;
            BinaryPrimitives.WriteUInt16BigEndian(freshData.AsSpan(ptrOff), cellContentOffset);
        }

        var schemaHdr = new BTreePageHeader(
            BTreePageType.LeafTable, 0, (ushort)schemaCells.Count, cellContentOffset, 0, 0);
        BTreePageHeader.Write(freshData.AsSpan(hdrOff), schemaHdr);

        // Write empty table pages
        for (int i = 0; i < tableRows.Count; i++)
        {
            int pageOff = pageSize * (i + 1);
            var tableHdr = new BTreePageHeader(BTreePageType.LeafTable, 0, 0, (ushort)pageSize, 0, 0);
            BTreePageHeader.Write(freshData.AsSpan(pageOff), tableHdr);
        }

        // 3. Open fresh database and re-insert all rows
        using var freshDb = SharcDatabase.OpenMemory(freshData);
        using var freshWriter = SharcWriter.From(freshDb);

        for (int t = 0; t < tableRows.Count; t++)
        {
            var (table, records) = tableRows[t];
            if (records.Count == 0) continue;

            uint rootPage = (uint)(t + 2);
            using var tx = freshDb.BeginTransaction();
            var mutator = tx.FetchMutator(usableSize);

            for (int r = 0; r < records.Count; r++)
            {
                long rowId = r + 1;
                rootPage = mutator.Insert(rootPage, rowId, records[r]);
            }

            // Update root page in schema if it changed
            if (rootPage != (uint)(t + 2))
            {
                UpdateTableRootPage(tx.GetShadowSource(), table.Name, rootPage, usableSize);
            }

            tx.Commit();
        }

        // 4. Copy all pages from fresh database back to original source
        using var vacuumTx = _db.BeginTransaction();
        var shadow = vacuumTx.GetShadowSource();

        int freshPageCount = freshDb.PageSource.PageCount;
        Span<byte> pageBuf = stackalloc byte[pageSize];

        for (uint p = 1; p <= (uint)freshPageCount; p++)
        {
            freshDb.PageSource.ReadPage(p, pageBuf);
            shadow.WritePage(p, pageBuf);
        }

        // Update header on the vacuumed database
        // Re-read page 1 from shadow, update header with correct page count and cleared freelist
        shadow.ReadPage(1, pageBuf);
        var vacuumHeader = DatabaseHeader.Parse(pageBuf);
        var finalHeader = new DatabaseHeader(
            vacuumHeader.PageSize, vacuumHeader.WriteVersion, vacuumHeader.ReadVersion,
            vacuumHeader.ReservedBytesPerPage, vacuumHeader.ChangeCounter,
            freshPageCount, 0, 0, // freelist cleared
            vacuumHeader.SchemaCookie, vacuumHeader.SchemaFormat,
            vacuumHeader.TextEncoding, vacuumHeader.UserVersion,
            vacuumHeader.ApplicationId, vacuumHeader.SqliteVersionNumber);
        DatabaseHeader.Write(pageBuf, finalHeader);
        shadow.WritePage(1, pageBuf);

        vacuumTx.Commit();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cachedShadow?.Dispose();
        _cachedShadow = null;
        if (_ownsDb) _db.Dispose();
    }
}
