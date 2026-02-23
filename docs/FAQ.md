# FAQ

## General

### What is Sharc?
Sharc is a **high-performance, managed context engine** for AI agents. It reads and writes the standard SQLite file format (Format 3) but bypasses the SQLite library entirely to achieve **2-95x faster read performance**. It is built in pure C# with no native dependencies.

### Why not just use SQLite?
If you need views, triggers, or stored procedures, use SQLite. Sharc supports `JOIN` (INNER/LEFT/CROSS), `GROUP BY`, Cotes, and compound queries (`UNION`/`INTERSECT`/`EXCEPT`) via the Query API.

**Use Sharc when:**

*   **Latency matters:** You need sub-microsecond point lookups (272ns vs 25,875ns in SQLite).
*   **Memory matters:** You need zero per-row allocations (using `ref struct` and `Span<T>`).
*   **Context matters:** You need to traverse graphs at 31x the speed of SQLite recursive CTEs (Cotes in Sharc).
*   **Deployment matters:** You need a <50KB WASM binary (vs 1MB+ for SQLite WASM).

### Is this production-ready?
The **Core Read Engine**, **Write Engine**, **Graph Layer**, **Trust Layer**, and **JIT Filter** are production-ready with comprehensive test coverage across 6 test projects.

The **Write Engine** supports:
*   **Full CRUD**: `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`, `ALTER TABLE`
*   **B-tree Mutations**: Leaf/interior splits, cell insertion/removal, page defragmentation
*   **ACID Transactions**: Rollback journal with auto-commit and explicit `BEGIN`/`COMMIT`/`ROLLBACK`
*   **Freelist Recycling**: Deleted pages are returned to the freelist and reused by subsequent writes
*   **Vacuum**: Reclaim unused space and compact the database file
*   **Single-Writer**: One writer at a time (no WAL-mode concurrent writes)

---

## Parsing & Querying

### Does Sharc use a standard SQL parser?
**No.** Sharc uses **Sharq** (Sharc Query), a custom recursive descent parser built on .NET 8's `SearchValues<T>`.
*   **Why?** Standard SQL parsers allocate memory for every token. Sharq is **allocation-free**.
*   **How?** It uses SIMD-accelerated character scanning to process queries at memory bandwidth speeds (GB/s).
*   See [Deep Dive: Parsing](DeepDive_Parsing.md) for the architecture.

### What is "Arrow Syntax" (`|>`)?

> **Parsed but not yet executable.** Arrow syntax is recognized by the Sharq parser but graph traversal execution via SQL is planned for a future release. Use the Graph API directly (`SharcContextGraph`) for graph traversal today.

Sharq extends SQL with graph traversal operators.
Instead of complicated `JOIN` syntax, you can write:
```sql
SELECT id |> friend |> name FROM users
```
The Graph API already achieves ~31x faster traversal than SQLite recursive CTEs (Cotes in Sharc).
See [Sharq Reference](ParsingTsql.md).

### Can I just use regular SQL?
Yes! Sharq supports `SELECT`, `FROM`, `WHERE`, `JOIN` (INNER/LEFT/CROSS), `ORDER BY`, `GROUP BY`, `HAVING`, `LIMIT`, `OFFSET`, `UNION`/`INTERSECT`/`EXCEPT`, Cotes (`WITH ... AS`), and parameterized queries (`$param`). You only need the arrow syntax for Graph Engine features.

> **Note:** `CASE` expressions and window functions (`OVER`, `PARTITION BY`) are parsed but not yet executable. `INNER JOIN`, `LEFT JOIN`, and `CROSS JOIN` are supported; `RIGHT JOIN` and `FULL OUTER JOIN` are not.

---

## Technical

### Why is Sharc faster than standard SQLite?
1.  **No P/Invoke**: No boundary crossing between C# and C.
2.  **No VDBE**: Queries compile to JIT delegates, not bytecode.
3.  **Span<T>**: Data moves from disk to user code without copying.

### What is the Trust Layer?
A built-in cryptographic audit system.
*   **Identity**: Agents sign writes with ECDsa keys.
*   **Provenance**: Every entry is hash-linked (SHA-256).
*   **Result**: You can cryptographically prove *who* added a piece of data and *when*.
See [Distributed Trust Architecture](DistributedTrustArchitecture.md).

### Does Sharc support WAL mode?
Yes. Sharc implements a full WAL reader that merges `-wal` frames with the main database file in memory, ensuring you always see the latest committed data.

### Can I use this in Blazor WASM?
Yes. This is a primary target. Sharc is **~40KB**, making it 25-40x smaller than sql.js or SQLite WASM. It requires no Emscripten and no special headers (like `Cross-Origin-Opener-Policy`).

---

## Troubleshooting

### `InvalidDatabaseException`
The file is likely not a valid SQLite Format 3 database. Check that the first 16 bytes are `SQLite format 3\0`.

### `CorruptPageException`
Sharc is strict. If a page checksum fails or a pointer is out of bounds, it throws immediately to prevent data corruption. This usually means the file was truncated during a download.

### Column index out of range?
If you use **projections** (`db.CreateReader("table", "colA", "colB")`), the reader only sees those 2 columns as index 0 and 1. The original table schema indices are ignored for performance.

