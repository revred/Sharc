# When NOT to Use Sharc

Sharc is a specialized **Context Engine**, not a general-purpose database. Honesty about limitations builds more trust than benchmarks.

## 🛑 STOP if you need:

### 1. SQL JOINs, Views, Triggers, or Stored Procedures

Sharc's query pipeline now supports `GROUP BY`, `HAVING`, `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, Cotes (`WITH ... AS`), and compound queries (`UNION`, `INTERSECT`, `EXCEPT`). However, it does **NOT** support:

*   `JOIN` (use `UNION`/Cote for multi-table workflows, or the Graph API for relationship traversal)
*   Views, Triggers, or Stored Procedures
*   `CASE` expressions, Window Functions (parsed but not yet executable)

**Use SQLite** if you need JOINs, views, triggers, or complex multi-table queries. For large-scale analytics, consider DuckDB.

### 2. General-Purpose Writes (UPDATE / DELETE)
The Sharc Write Engine is **EXPERIMENTAL** and **APPEND-ONLY**.
*   **NO `UPDATE`**: You cannot modify existing rows.
*   **NO `DELETE`**: You cannot remove rows.
*   **NO Index Maintenance**: Inserting data does *not* update secondary indexes.
*   **NO Concurrency**: Sharc supports only a single writer.

**Use SQLite** for any application that needs standard CRUD (Create, Read, Update, Delete) or concurrent writes.

### 3. Full-Text Search (FTS)
Sharc scans standard B-trees. It does not support SQLite's `FTS5`, `R*Tree`, or other virtual tables. If you need full-text search, use SQLite's FTS5 extension or a dedicated search engine.

### 4. Large-Scale OLAP
Sharc is a row-store. It reads row-by-row. If you need to scan 10GB of data to compute an average, use **DuckDB** or **SQLite** (columnar mode). Sharc is built for **Latency** (finding one needle in a haystack), not **Throughput** (moving the whole haystack).

---

## ✅ GO if you need:

| Capability | Why Sharc Wins |
| :--- | :--- |
| **Graph Traversal** | Two-phase BFS with zero-alloc cursors is **31x faster** than SQLite recursive CTEs. |
| **Point Lookups** | **392ns** vs 24,011ns (61x faster). If you do thousands of lookups per request, Sharc is the only choice. |
| **Agent Context** | Precision retrieval allows you to fit **100% relevant context** into small token windows. |
| **Trust & Audit** | Built-in cryptographic ledger (`_sharc_ledger`) proves *who* wrote *what*. |
| **WASM / Edge** | **<50KB** binary. Runs in-browser without Emscripten or multithreading headers. |

## Summary

*   Building a **Blog**? Use SQLite.
*   Building a **Dashboard**? Use DuckDB.
*   Building an **AI Agent**? **Use Sharc.**
