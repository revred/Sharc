using System;
using System.Collections.Generic;
using Sharc.Core.BTree;
using Sharc.Core.Format;
using Sharc.Core.IO;
using Sharc.Core.Records;
using Sharc.Core.Schema;

namespace Sharc.Core.Storage;

/// <summary>
/// Unified utility for managing system table mutations and record encoding.
/// Reduces code bloat by consolidating boilerplate used across Trust and Metadata modules.
/// </summary>
public static class SystemStore
{
    /// <summary>
    /// Inserts a record into a system table with a long RowId key.
    /// handles transaction logic, encoding, and B-tree mutation in one call.
    /// </summary>
    public static void InsertRecord(
        IWritablePageSource pageSource,
        int pageSize,
        uint rootPage,
        long rowId,
        ColumnValue[] columns)
    {
        int recordSize = RecordEncoder.ComputeEncodedSize(columns);
        byte[] buffer = new byte[recordSize];
        RecordEncoder.EncodeRecord(columns, buffer);

        var mutator = new BTreeMutator(pageSource, pageSize);
        mutator.Insert(rootPage, rowId, buffer);
    }

    /// <summary>
    /// Decodes a system record into a typed LedgerEntry or similar DTO.
    /// </summary>
    public static T Decode<T>(ReadOnlySpan<byte> payload, IRecordDecoder decoder, Func<ColumnValue[], T> mapper)
    {
        var values = decoder.DecodeRecord(payload);
        return mapper(values);
    }

    /// <summary>
    /// Helper to find a system table's root page by name.
    /// </summary>
    public static uint GetRootPage(SharcSchema schema, string tableName)
    {
        var table = schema.GetTable(tableName)
            ?? throw new KeyNotFoundException($"System table '{tableName}' not found.");
        return (uint)table.RootPage;
    }
}
