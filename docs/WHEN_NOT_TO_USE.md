# When NOT to Use Sharc

Sharc is a specialized read/write engine, not a general-purpose database. Honesty about limitations builds more trust than benchmarks.

## Use Standard SQLite Instead If You Need:

### SQL Queries

Sharc has no SQL parser. You cannot run `SELECT a, SUM(b) FROM t GROUP BY a` or any SQL at all. Sharc reads raw B-tree pages directly. If your workload requires ad-hoc queries, aggregation, or views, use SQLite.

### JOINs

Sharc reads one table at a time. Cross-table joining must be done in your application code or through the Graph Layer's traversal API. For relational queries across many tables, use SQLite.

### Heavy Write Workloads

Sharc's write engine supports INSERT with B-tree splits and ACID transactions. UPDATE and DELETE are not yet implemented. For write-heavy OLTP workloads, use SQLite.

### Full-Text Search or R-Tree

FTS5, R-Tree, and other virtual tables are not supported. These are SQLite extensions that require the VDBE.

### Multi-GB OLAP Analytics

Sharc is optimized for row-oriented point lookups and sequential scans. For columnar analytics over large datasets, consider DuckDB or SQLite with appropriate indexing.

### Concurrent Writers

Sharc supports multiple parallel readers but uses a single-writer model. For high-concurrency write scenarios, use SQLite with WAL mode.

### Legacy Platforms

Sharc targets .NET 10+. For older .NET versions, other runtimes, or non-.NET languages, use the official SQLite C library via `Microsoft.Data.Sqlite`.

## Where Sharc Excels

| Strength | Why |
| :--- | :--- |
| Point lookups | 7-61x faster than SQLite via P/Invoke |
| Graph traversal | 13.5x faster BFS through O(log N) index seeks |
| AI context delivery | 62-133x token reduction through precision retrieval |
| Zero dependencies | Pure managed C# — no native DLLs, works on WASM/Mobile/IoT |
| Encrypted reads | Page-level AES-256-GCM, transparent to application code |
| Audit trail | Tamper-evident hash-chain ledger with ECDSA attestation |

## Summary

Sharc is a **complement** to SQLite, not a replacement. Use Sharc for reads, seeks, graph traversal, and trusted context delivery. Use SQLite for SQL queries, writes, joins, and full-text search.
