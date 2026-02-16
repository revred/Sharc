# Current Limitations (as of Feb 2026)

This document summarizes what is **missing**, **experimental**, or **intentionally omitted** in Sharc.

## 1. The Write Engine (Experimental)
*   **Status**: `INSERT`-only. Single-writer.
*   **Missing Features**:
    *   ? **No UPDATE/DELETE**: Records are immutable once written.
    *   ? **No Index Maintenance**: Writing to a table does not update its secondary indexes. Lookups will fail for new rows.
    *   ? **No Overflow Support**: Records > Page Size will corrupt the page.
    *   ? **No Root Splits**: If a table grows enough to split its root page, the table definition becomes invalid.
    *   ? **No Concurrency**: No WAL locking. Single thread/process only.

## 2. Querying Capabilities
*   **Status**: `Sharq` parser + `JIT` filter + streaming query pipeline.
*   **Supported (via Query API)**:
    *   `SELECT`, `WHERE`, `ORDER BY`, `LIMIT`, `OFFSET`
    *   `GROUP BY`, `HAVING`, `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`
    *   `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`
    *   Common Table Expressions (`WITH ... AS`)
    *   Parameterized queries (`$param`)
*   **Missing Features**:
    *   **No SQL JOINs**: Standard `JOIN` syntax is not supported. Use `UNION`/CTE for multi-table workflows or the Graph API for relationship traversal.
    *   **No Virtual Tables**: `FTS5`, `R*Tree`, `json_each` are not supported.
    *   **No CASE execution**: `CASE` expressions are parsed but not yet executable.
    *   **No Window Functions**: `OVER`, `PARTITION BY` are parsed but not yet executable.
*   **Performance Notes**:
    *   Aggregations are streaming (O(groups) memory).
    *   CTEs materialize the CTE result before re-scanning.
    *   `UNION`/`INTERSECT`/`EXCEPT` use streaming set operations with spare-array reuse (1.2-1.6 MB for 2×2.5K rows). String allocation (~500 KB) dominates — further reduction requires raw-byte comparison before materialization.

## 3. Workload Suitability (OLAP vs OLTP)
*   **Status**: Optimized for Context Engineering (Latency).
*   **Hard Stops**:
    *   ?? **Analytics (OLAP)**: Sharc is a row-store. Scanning 1M rows to compute an average is slow compared to DuckDB.
    *   ?? **Heavy Write OLTP**: Lack of MVCC and locking means Sharc cannot handle high-throughput concurrent writes.
    *   ?? **Complex Reporting**: `GROUP BY` and aggregations are now supported but window functions are not yet executable. For heavy reporting, use SQLite or DuckDB.

## 4. Platform
*   **Status**: .NET 10+ Managed Only.
*   **Missing**:
    *   ? **No Native Interop**: Cannot use SQLite extensions (`.dll`/`.so`).
    *   ? **No Multithreaded WASM**: Currently strictly single-threaded in browser (though non-blocking via async).

