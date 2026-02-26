// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc;

/// <summary>
/// Execution strategy used by a non-vector query reader.
/// </summary>
public enum QueryExecutionStrategy
{
    /// <summary>Unknown or not yet initialized.</summary>
    Unknown = 0,
    /// <summary>Full table cursor scan.</summary>
    TableScan = 1,
    /// <summary>Single-index seek with table row materialization.</summary>
    SingleIndexSeek = 2,
    /// <summary>Rowid intersection across multiple indexes.</summary>
    RowIdIntersection = 3,
    /// <summary>Materialized row source.</summary>
    Materialized = 4,
    /// <summary>Concatenated row sources (for example UNION ALL).</summary>
    Concat = 5,
    /// <summary>Set-operation dedup/intersection/except row source.</summary>
    SetDedup = 6
}

/// <summary>
/// Lightweight execution diagnostics for non-vector query readers.
/// </summary>
/// <param name="Strategy">Execution strategy for the reader.</param>
/// <param name="ScannedRows">Rows scanned from the underlying execution source.</param>
/// <param name="ReturnedRows">Rows returned to the caller via <c>Read()</c>.</param>
/// <param name="IndexEntriesScanned">Index entries examined (0 for non-index strategies).</param>
/// <param name="IndexHits">Index matches before table materialization (0 for non-index strategies).</param>
/// <param name="ElapsedMs">Wall-clock elapsed time in milliseconds (0 if not measured).</param>
public readonly record struct QueryExecutionInfo(
    QueryExecutionStrategy Strategy,
    int ScannedRows,
    int ReturnedRows,
    int IndexEntriesScanned,
    int IndexHits,
    double ElapsedMs = 0)
{
    /// <summary>Default diagnostics before scanning begins.</summary>
    public static QueryExecutionInfo None => new(
        Strategy: QueryExecutionStrategy.Unknown,
        ScannedRows: 0,
        ReturnedRows: 0,
        IndexEntriesScanned: 0,
        IndexHits: 0,
        ElapsedMs: 0);
}
