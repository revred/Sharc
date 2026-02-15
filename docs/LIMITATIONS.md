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
*   **Status**: `Sharq` parser + `JIT` filter.
*   **Missing Features**:
    *   ? **No Aggregations**: `SUM`, `AVG`, `COUNT`, `GROUP BY`, `HAVING`.
    *   ? **No SQL Joins**: Standard `JOIN` syntax is not supported. Use arrow syntax `|>` for graph traversal or join in memory.
    *   ? **No CTEs**: Recursive Common Table Expressions are replaced by native graph traversal.
    *   ? **No Virtual Tables**: `FTS5`, `R*Tree`, `json_each` are not supported.

## 3. Workload Suitability (OLAP vs OLTP)
*   **Status**: Optimized for Context Engineering (Latency).
*   **Hard Stops**:
    *   ?? **Analytics (OLAP)**: Sharc is a row-store. Scanning 1M rows to compute an average is slow compared to DuckDB.
    *   ?? **Heavy Write OLTP**: Lack of MVCC and locking means Sharc cannot handle high-throughput concurrent writes.
    *   ?? **Complex Reporting**: Lack of `GROUP BY` and window functions makes Sharc unsuitable for report generation.

## 4. Platform
*   **Status**: .NET 10+ Managed Only.
*   **Missing**:
    *   ? **No Native Interop**: Cannot use SQLite extensions (`.dll`/`.so`).
    *   ? **No Multithreaded WASM**: Currently strictly single-threaded in browser (though non-blocking via async).

