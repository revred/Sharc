# Sharc vs Alternatives

An honest comparison to help you choose the right tool.

---

## Sharc vs Microsoft.Data.Sqlite

| Dimension | Sharc | Microsoft.Data.Sqlite |
| :--- | :--- | :--- |
| **Implementation** | Pure managed C# | P/Invoke wrapper around native `e_sqlite3` |
| **Native dependencies** | **None** | Requires `e_sqlite3.dll` / `libe_sqlite3.so` |
| **WASM support** | **~40KB**, no Emscripten | Requires Emscripten build (~1MB+) |
| **Point lookup speed** | **272 ns (95x faster)** | 25,875 ns |
| **Table scan speed** | **1.54 ms (4x faster)** | 6.22 ms |
| **Per-row allocation** | **0 B** (Span-based) | Allocates per call |
| **SQL support** | SELECT, WHERE, JOIN, GROUP BY, UNION, CTEs | Full SQL engine (VDBE) |
| **Views / Triggers** | Not supported | Full support |
| **RIGHT/FULL OUTER JOIN** | Not supported | Full support |
| **Window functions** | Parsed, not executable | Full support |
| **Concurrent writers** | Single writer | WAL mode concurrent writes |
| **Package size** | **~250 KB** | ~2 MB |
| **Encryption** | AES-256-GCM (Argon2id KDF) | Via SQLCipher (separate) |
| **Graph traversal** | **31x faster** (built-in) | Via recursive CTEs |
| **Trust / Audit** | **Built-in** ECDSA + ledger | Not available |

**Choose Sharc when:** Speed matters, native deps are a problem (WASM/mobile/IoT), or you need graph/trust features.

**Choose SQLite when:** You need views, triggers, stored procedures, concurrent writers, window functions, or the full SQL standard.

---

## Sharc vs LiteDB

| Dimension | Sharc | LiteDB |
| :--- | :--- | :--- |
| **Data model** | Relational (SQLite format) | Document store (BSON) |
| **File format** | Standard SQLite Format 3 | Proprietary `.db` |
| **Interop with SQLite** | **Read/write same .db files** | Separate format |
| **Query language** | SQL (Sharq parser) | LINQ-like API |
| **Point lookup** | **272 ns** | ~microseconds |
| **Per-row allocation** | **0 B** | Allocates per document |
| **WASM support** | **Yes (~40KB)** | Limited |
| **Graph traversal** | **Built-in (31x vs SQLite)** | Not built-in |
| **Encryption** | AES-256-GCM | AES encryption |

**Choose Sharc when:** You have SQLite data, need speed, or need graph/trust features.

**Choose LiteDB when:** You want a document store with a LINQ API and don't need SQLite interop.

---

## Sharc vs DuckDB

| Dimension | Sharc | DuckDB |
| :--- | :--- | :--- |
| **Optimized for** | **Latency** (point lookups, context retrieval) | **Throughput** (OLAP, analytics) |
| **Data model** | Row store | Columnar store |
| **Point lookup** | **272 ns** | Not designed for this |
| **Scan 1M rows** | Slower (row-by-row) | **Much faster** (vectorized) |
| **Aggregations** | Streaming (O(groups) memory) | **Vectorized, parallel** |
| **Native dependencies** | **None** | Requires native library |
| **WASM support** | **~40KB** | ~10MB+ |
| **Package size** | **~250 KB** | ~50 MB |
| **Write support** | Full CRUD | Full CRUD |

**Choose Sharc when:** You need fast point lookups, context retrieval for AI agents, or WASM deployment.

**Choose DuckDB when:** You need to scan millions of rows, compute aggregations over large datasets, or do OLAP analytics.

---

## Sharc vs SQLitePCLRaw

| Dimension | Sharc | SQLitePCLRaw |
| :--- | :--- | :--- |
| **Approach** | **Reimplements** SQLite format in C# | **Wraps** native SQLite via P/Invoke |
| **Native dependencies** | **None** | Requires native sqlite3 binary |
| **WASM** | **~40KB, trivial** | Requires Emscripten |
| **Performance** | **2-95x faster** (no P/Invoke overhead) | Baseline SQLite speed |
| **API level** | High-level (SharcDatabase) | Low-level (raw C bindings) |
| **SQL completeness** | Subset (no views/triggers) | Full SQLite |
| **Maintainer** | Maker.AI | Microsoft (Eric Sink) |

**Choose Sharc when:** You want zero native dependencies and maximum read performance.

**Choose SQLitePCLRaw when:** You need the full SQLite C engine with raw API access.

---

## Decision Flowchart

```
Do you need views, triggers, or stored procedures?
  YES → Use Microsoft.Data.Sqlite or SQLitePCLRaw

Do you need to scan millions of rows for analytics?
  YES → Use DuckDB

Do you need a document store (not relational)?
  YES → Use LiteDB

Do you need any of these?
  - Zero native dependencies (WASM, mobile, IoT)
  - Sub-microsecond point lookups
  - Zero per-row GC allocation
  - Graph traversal with trust/audit
  - <50KB embedded database
  YES → Use Sharc
```
