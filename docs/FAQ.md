# FAQ

## General

### What is Sharc?
Sharc is a **high-performance, managed context engine** for AI agents. It reads and writes the standard SQLite file format (Format 3) but bypasses the SQLite library entirely to achieve **2-75x faster read performance**. It is built in pure C# with no native dependencies.

### Why not just use SQLite?
If you need JOINs, views, triggers, or stored procedures, use SQLite. Sharc now supports `GROUP BY`, CTEs, and compound queries (`UNION`/`INTERSECT`/`EXCEPT`) via the Query API.

**Use Sharc when:**

*   **Latency matters:** You need sub-microsecond point lookups (392ns vs 24,011ns in SQLite).
*   **Memory matters:** You need zero per-row allocations (using `ref struct` and `Span<T>`).
*   **Context matters:** You need to traverse graphs at 13.5x the speed of SQLite recursive CTEs.
*   **Deployment matters:** You need a <50KB WASM binary (vs 1MB+ for SQLite WASM).

### Is this production-ready?
The **Core Read Engine**, **Graph Layer**, **Trust Layer**, and **JIT Filter** are production-ready (Phase 2).

**The Write Engine is EXPERIMENTAL and has severe limitations:**
*   **Insert-Only**: `UPDATE` and `DELETE` are not implemented.
*   **No Index Updates**: Inserting into a table does *not* update its secondary indexes.
*   **No Overflow Support**: Records larger than the page size will fail or corrupt the page.
*   **Single-Writer**: No concurrency control (WAL/Locking Byte) is implemented.
*   **Root Split Issues**: Tables are not resilient to root page splits.

Use the Write Engine *only* for:
1.  Appending rigid logs (Trust Ledger).
2.  Creating simple, non-indexed datasets from scratch.

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
The Graph API already achieves ~13.5x faster traversal than SQLite recursive CTEs.
See [Sharq Reference](ParsingTsql.md).

### Can I just use regular SQL?
Yes! Sharq supports `SELECT`, `FROM`, `WHERE`, `ORDER BY`, `GROUP BY`, `HAVING`, `LIMIT`, `OFFSET`, `UNION`/`INTERSECT`/`EXCEPT`, CTEs (`WITH ... AS`), and parameterized queries (`$param`). You only need the arrow syntax for Graph Engine features.

> **Note:** `CASE` expressions and window functions (`OVER`, `PARTITION BY`) are parsed but not yet executable. `JOIN` is not supported — use `UNION`/CTE for multi-table workflows.

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

