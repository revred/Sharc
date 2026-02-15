# When NOT to Use Sharc

Sharc is a specialized **Context Engine**, not a general-purpose database. Honesty about limitations builds more trust than benchmarks.

## What Sharc Now Supports (as of Feb 2026)

The Sharq query pipeline supports **SELECT**, **WHERE** (with AND/OR/NOT, LIKE, IN, BETWEEN), **ORDER BY**, **LIMIT/OFFSET**, **GROUP BY** with aggregates (COUNT, SUM, AVG, MIN, MAX), **UNION / UNION ALL / INTERSECT / EXCEPT**, **CTEs** (WITH ... AS), and **parameterized queries**.

These features work correctly — 1,593 tests passing — but carry a **materialization cost** detailed below.

---

## Known Performance Gaps

### 1. Query Pipeline Memory Allocation

This is the most significant gap. Sharc's `Query()` API materializes results into managed `object?[]` arrays on the heap. SQLite does equivalent work in native C with near-zero managed allocation.

| Query Type | Sharc Allocation | SQLite Allocation | Gap |
| :--- | ---: | ---: | ---: |
| `SELECT *` (2.5K rows) | 414 KB | 688 B | **616x** |
| `UNION ALL` (2x2.5K rows) | 1,323 KB | 404 KB | 3.3x |
| `UNION` / `INTERSECT` / `EXCEPT` | 1.3–1.6 MB | 744 B | **~2,000x** |
| `GROUP BY` + aggregates | 1,486 KB | 920 B | **1,653x** |
| `CTE` + filter | 282 KB | 31 KB | 9x |

**Root causes:**

- Compound queries (UNION/INTERSECT/EXCEPT) must materialize **both sides** into `List<object?[]>` before applying set operations
- CTEs are materialized into arrays, then re-scanned — effectively a double copy
- `QueryPostProcessor` re-materializes for ORDER BY / DISTINCT, adding a third copy
- Every column value is boxed into `object?` — integers, doubles, strings all heap-allocated

**Impact:** For queries returning < 10K rows this is unnoticeable. For sustained load or WASM environments with constrained heaps, the GC pressure becomes significant.

**Why it matters:** The core engine (`CreateReader`) achieves zero-allocation reads — the entire value proposition of pure managed C#. The query pipeline undoes this by allocating megabytes per compound query.

### 2. ORDER BY + LIMIT Without Streaming Top-N

SQLite's query optimizer implements streaming top-N sort: it keeps only the top K rows in a heap, processing rows as they arrive without materializing the full result set. Sharc materializes everything, sorts, then slices.

| Query | Sharc | SQLite | Gap |
| :--- | ---: | ---: | ---: |
| `UNION ALL + ORDER BY + LIMIT 50` | 1,786 us | 428 us | **4.2x** |
| `WHERE + ORDER BY + LIMIT 100` | 2,054 us | 277 us | **7.4x** |

This is the single largest speed gap and is directly tied to the materialization problem above.

### 3. No Multithreaded WASM

Sharc runs in the browser via Blazor WebAssembly — but strictly single-threaded. The platform supports `SharedArrayBuffer` and Web Workers for parallel execution, but Sharc does not leverage them. For workloads that scan large tables or execute compound queries, a multithreaded WASM runtime could parallelize:

- Left and right sides of UNION/INTERSECT/EXCEPT
- CTE materialization and main query execution
- Multiple independent sub-queries

This is a platform capability we have not yet exploited.

---

## Still True Limitations

### No JOIN Support

Single-table queries only. Use `UNION`/`CTE` for multi-table workflows, or `|>` graph syntax for relationship traversal. Standard SQL JOINs (INNER, LEFT, CROSS) are not supported.

### No UPDATE / DELETE

The Write Engine supports **INSERT** with B-tree splits and ACID transactions. UPDATE and DELETE are not yet implemented.

### No Full-Text Search (FTS)

Sharc scans standard B-trees. It does not support SQLite's `FTS5`, `R*Tree`, or other virtual tables. Use SQLite's FTS5 extension or a dedicated search engine for full-text search.

### No Large-Scale OLAP

Sharc is a row-store. It reads row-by-row. For scanning gigabytes of data to compute aggregates, use **DuckDB** or **SQLite** (columnar mode). Sharc is built for **latency** (finding one needle), not **throughput** (moving the whole haystack).

### No Index Maintenance on Write

Inserting data does not update secondary indexes. Only the primary B-tree (rowid) is maintained.

### Single Writer

No concurrent write support. One writer at a time.

---

## Where Sharc Wins

| Capability | Sharc | SQLite | Why |
| :--- | ---: | ---: | :--- |
| **B-tree Seek** | 392 ns | 24,011 ns | **61x** — zero-copy struct pipeline |
| **Graph 2-Hop BFS** | 6.04 us | 81.56 us | **13.5x** — `\|>` edge traversal |
| **UNION ALL** (5K rows) | 930 us | 2,419 us | **2.6x** — in-process, no interop |
| **Full Table Scan** | 461 us | 603 us | **1.3x** — Span-based decode |
| **Binary Size** | ~250 KB | ~2 MB (native) | No Emscripten, no P/Invoke |
| **Per-Row Allocation** | 0 B | varies | Zero-alloc via CreateReader |
| **Trust Layer** | Built-in | N/A | ECDSA attestation, audit ledger |

## Decision Matrix

| If you need... | Use |
| :--- | :--- |
| Point lookups (< 1 us) | **Sharc** |
| Graph traversal | **Sharc** |
| Agent context + trust | **Sharc** |
| WASM / Edge / IoT (< 250 KB) | **Sharc** |
| UNION ALL of large tables | **Sharc** |
| JOINs or complex analytics | **SQLite** |
| UPDATE / DELETE | **SQLite** |
| Full-text search | **SQLite** (FTS5) |
| OLAP / columnar scans | **DuckDB** |
| Compound queries with ORDER BY + LIMIT on large results | **SQLite** (for now) |
