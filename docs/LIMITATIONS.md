# Current Limitations (as of Feb 2026)

This document summarizes what is **missing**, **experimental**, or **intentionally omitted** in Sharc.

## 1. The Write Engine

*   **Status**: Full CRUD (INSERT/UPDATE/DELETE) with ACID transactions. Single-writer.
*   **Capabilities**: B-tree leaf/interior splits, cell insertion/removal, page defragmentation, freelist recycling, overflow page support, root splits, vacuum.
* **Remaining Limitations**:
  * **No Concurrency**: No WAL locking. Single writer at a time.
  * **No MVCC**: No snapshot isolation for concurrent readers + writers.
  * **Single-Overflow Writes Only**: The write path chains at most one overflow page per record (~8 KB max payload on 4,096-byte pages). Records exceeding this limit produce corrupt overflow chains. The read path correctly handles multi-overflow chains. See ADR-021.

## 2. Querying Capabilities
*   **Status**: `Sharq` parser + `JIT` filter + streaming query pipeline.
*   **Supported (via Query API)**:
    *   `SELECT`, `WHERE`, `JOIN` (INNER/LEFT/CROSS), `ORDER BY`, `LIMIT`, `OFFSET`
    *   `GROUP BY`, `HAVING`, `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`
    *   `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`
    *   Common Table Expressions (`WITH ... AS`)
    *   Parameterized queries (`$param`)
*   **Missing Features**:
    *   **Limited JOINs**: `INNER JOIN`, `LEFT JOIN`, and `CROSS JOIN` are supported via hash join. `RIGHT JOIN` and `FULL OUTER JOIN` are not yet supported.
    *   **No Virtual Tables**: `FTS5`, `R*Tree`, `json_each` are not supported.
    *   **No CASE execution**: `CASE` expressions are parsed but not yet executable.
    *   **No Window Functions**: `OVER`, `PARTITION BY` are parsed but not yet executable.
*   **Performance Notes**:
    *   Aggregations are streaming (O(groups) memory).
    *   Cotes materialize the Cote result before re-scanning.
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

