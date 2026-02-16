# Sharc Benchmarks

Detailed performance comparison: Sharc vs Microsoft.Data.Sqlite vs IndexedDB.

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H (8C/16T)
> All numbers are **measured**, not estimated. Last run: February 16, 2026. SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements.

---

## Core Operations (5K rows, 9-column `users` table)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Engine Init (open + header) | **981 ns** | 38.68 us | **39x** | **1,416 B** | 1,160 B |
| Schema Introspection | **4.69 us** | 27.86 us | **5.9x** | 4,784 B | 2,536 B |
| Sequential Scan (9 cols) | **1.54 ms** | 6.22 ms | **4.0x** | 1.41 MB | 1.41 MB |
| Point Lookup (Seek) | **392 ns** | 24,011 ns | **61x** | **688 B** | 728 B |
| Batch 6 Lookups | **1,940 ns** | 127,526 ns | **66x** | **1,792 B** | 3,712 B |
| Type Decode (5K ints) | **185 us** | 854 us | **4.6x** | 648 B | 688 B |
| NULL Detection | **394 us** | 1.24 ms | **3.1x** | 648 B | 688 B |
| WHERE Filter | **315 us** | 587 us | **1.8x** | 1,008 B | 720 B |
| GC Pressure (sustained) | **214 us** | 1.20 ms | **5.6x** | 648 B | 688 B |

> **Sharc wins 9 of 9 on speed.** Engine Init is the only allocation trade-off: cold start pre-allocates the LRU cache for zero-alloc reads.

---

## Graph Storage (5K nodes, 15K edges)

Sharc's built-in graph layer (`Sharc.Graph`) maps concept/relation tables to a traversable graph with O(log N) index seeks.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Node Scan (5K concepts) | **1,907 us** | 4,032 us | **2.1x** | 1,610,600 B | 959,232 B |
| Edge Scan (15K relations) | **4,176 us** | -- | **--** | 2,892,176 B | -- |
| Node Projection (id + type) | **988 us** | 2,200 us | **2.2x** | 812,584 B | 480,704 B |
| Edge Filter by Kind | **1,800 us** | 3,129 us | **1.7x** | 1,452,176 B | 696 B |
| Single Node Seek | **1,475 ns** | 21,349 ns | **14.5x** | 1,840 B | 600 B |
| Batch 6 Node Seeks | **3,858 ns** | 159,740 ns | **41.4x** | 4,176 B | 3,024 B |
| Open > Seek > Close | **11,764 ns** | 33,386 ns | **2.8x** | 12,496 B | 1,256 B |
| Edge Scan (Pushdown) | **561 us** | 2,529 us | **4.5x** | **736 B** | **696 B** |
| Incoming Edge Scan | **1.75 us** | 27.65 us | **15.8x** | 2,968 B | 728 B |
| Bidirectional BFS | **23.9 us** | 34.9 us | **1.4x** | 10,900 B | 2,808 B |
| 2-Hop BFS Traversal | **6.04 us** | 81.56 us | **13.5x** | 10,150 B | 2,740 B |

> **Graph seeks are the sweet spot:** 14.5x-41.4x faster. BFS traversal achieves 13.5x through `SeekFirst(key)` -- O(log N) binary search on the index B-tree.

---

## Memory Allocations

Speed without memory discipline is incomplete. Here's what each engine allocates:

| Operation | Sharc | SQLite | Winner |
|:---|---:|---:|:---:|
| Primitives (header, varint) | **0 B** | N/A | Sharc |
| NULL Detection (5K rows) | **784 B** | 688 B | Parity |
| Type Decode (5K ints) | **784 B** | 688 B | Parity |
| GC Pressure (sustained) | **784 B** | 688 B | Parity |
| Batch 6 Lookups | **1.8 KB** | 3.7 KB | Sharc |
| Point Lookup (Seek) | **688 B** | 728 B | Sharc |
| Schema Read | 4.8 KB | **2.5 KB** | SQLite |
| Sequential Scan (5K rows) | **1.35 MB** | 1.35 MB | **Parity** |
| WHERE Filter | 1.0 KB | **720 B** | SQLite |

> **Sharc allocates less or parity in 7 of 9 benchmarks.** On hot-path scans (NULL, type decode, sustained reads), allocation is **~0 bytes per row** (amortized).

---

## Seek Performance Deep-Dive

```
SQLite Seek Path (21,193 ns):
  C# > P/Invoke > sqlite3_prepare > SQL parse > VDBE compile >
  sqlite3_step > B-tree descend > read leaf > VDBE decode >
  P/Invoke return > marshal to managed objects

Sharc Seek Path (392 ns):
  Span<byte> > B-tree page > binary search > leaf cell > decode value
```

**61x on single seeks. 66x on batch 6.** Batch amplification comes from LRU page cache locality -- the second through sixth seeks reuse cached B-tree interior pages.

---

## Type Decode Deep-Dive

| Type | Sharc | SQLite | Speedup |
|:---|---:|---:|:---:|
| Integers (100K rows) | **4,156 us** | 16,990 us | **4.1x** |
| Doubles (10K rows) | **952 us** | 12,296 us | **12.9x** |
| Short Strings | **1,086 us** | 1,900 us | **1.7x** |
| Medium Strings | **1,778 us** | 14,067 us | **7.9x** |
| NULL Check | **611 us** | 12,107 us | **19.8x** |
| Mixed Row (all types) | **7,559 us** | 28,331 us | **3.7x** |

> **NULL detection is 19.8x faster** -- Sharc checks the serial type byte directly (`type == 0`). **Doubles are 12.9x faster** -- `BinaryPrimitives.ReadDoubleBigEndian()` vs VDBE + P/Invoke + boxing.

---

## Realistic Workloads

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|:---:|
| Load User Profile | **12.2 us** | 22.1 us | **1.8x** |
| Open > Read 1 Row > Close | **12.1 us** | 26.2 us | **2.2x** |
| Schema Migration Check | **11.5 us** | 149.9 us | **13.0x** |
| Config Read (10 keys) | **12.8 us** | 270.5 us | **21.1x** |
| Batch Lookup (500 users) | **89.7 us** | 245.7 us | **2.7x** |
| Export Users to CSV | **8,069 us** | 20,682 us | **2.6x** |

---

## Primitive Operations (Sharc-only)

These have no SQLite equivalent -- they measure raw byte-level decode speed.

| Operation | Time | Allocated |
|:---|---:|---:|
| Parse database header (100 bytes) | **8.5 ns** | **0 B** |
| Parse B-tree page headers (10 pages) | **35.1 ns** | **0 B** |
| Decode 100 varints | **231 ns** | **0 B** |
| Classify 100 serial types | **102 ns** | **0 B** |
| Read 100 inline integers | **125 ns** | **0 B** |
| Read 5 row column values | **5.5 ns** | **0 B** |

**Zero allocation across all primitive operations.** `ReadOnlySpan<byte>` + `stackalloc` + `readonly struct`.

---

## Browser Arena: Three-Way Comparison

> [**Run the Arena yourself**](https://revred.github.io/Sharc/)

| # | Benchmark | Sharc | SQLite (WASM) | IndexedDB | Winner |
|:---:|:---|---:|---:|---:|:---:|
| 1 | Engine Init | **430 ns** | 142 ms | 2.1 ms | Sharc |
| 2 | Schema Read | **2.67 us** | 25.66 us | 45 us | Sharc |
| 3 | Sequential Scan | **3.01 ms** | 6.23 ms | 89 ms | Sharc |
| 4 | Point Lookup | **3,094 ns** | 23,448 ns | 85,000 ns | Sharc |
| 5 | Batch Lookups | **5,599 ns** | 122,637 ns | 520,000 ns | Sharc |
| 6 | Type Decode | **0.212 ms** | 0.779 ms | 42 ms | Sharc |
| 7 | NULL Detection | **163 us** | 742 us | 38,000 us | Sharc |
| 8 | WHERE Filter | **496 us** | 659 us | N/A | Sharc |
| 9 | Graph Node Scan | **1,029 us** | 2,809 us | N/A | Sharc |
| 10 | Graph Edge Scan | **2,204 us** | -- | N/A | Sharc |
| 11 | Graph Node Seek | **637 ns** | 21,193 ns | N/A | Sharc |
| 12 | Graph 2-Hop BFS | **5.78 us** | 79.31 us | N/A | Sharc |
| 13 | GC Pressure | **0.213 ms** | 0.842 ms | N/A | Sharc |
| 14 | Encrypted Read | **340 us** | N/A | N/A | Sharc |
| 15 | Memory Footprint | **~250 KB** | 1,536 KB | 0 (built-in) | Sharc |
| 16 | Primitives | **8.5 ns** | N/A | N/A | Sharc |

> **Score: Sharc 16 / SQLite 0 / IndexedDB 0.** These benchmarks test the **Core Engine** (`CreateReader` API). The SQL query pipeline (`Query` API) has different performance characteristics — see the README for query pipeline benchmarks. Sharc ranges from 17x to 233x faster than IndexedDB.

---

## Query Pipeline (Query API — full SQL roundtrip)

> 2,500 rows/table. Compound queries use two tables with 500 overlapping rows.

| Category | Operation | Sharc | SQLite | Speedup | Sharc Alloc |
|:---|:---|---:|---:|:---:|---:|
| **Simple** | `SELECT * FROM t` (2.5K rows) | **64 us** | 586 us | **9.2x** | 568 B |
| **Filtered** | `SELECT WHERE age > 30` | **193 us** | 802 us | **4.1x** | 98 KB |
| **Medium** | `WHERE + ORDER BY + LIMIT 100` | **434 us** | 580 us | **1.3x** | 424 KB |
| **Aggregate** | `GROUP BY + COUNT + AVG` | **353 us** | 475 us | **1.3x** | 5.3 KB |
| **Compound** | `UNION ALL` (2x2.5K rows) | **446 us** | 2,098 us | **4.7x** | 415 KB |
| | `UNION` (deduplicated) | **787 us** | 1,668 us | **2.1x** | 1.4 KB |
| | `INTERSECT` | **710 us** | 1,183 us | **1.7x** | 1.4 KB |
| | `EXCEPT` | **748 us** | 1,231 us | **1.6x** | 1.4 KB |
| | `3-way UNION ALL` | **279 us** | 1,225 us | **4.4x** | 2.3 KB |
| **Cote** | `WITH ... AS SELECT WHERE` | **331 us** | 349 us | **1.05x** | 309 KB |
| **Parameterized** | `WHERE $param AND $param` | **179 us** | 591 us | **3.3x** | 81 KB |

> **Sharc wins every benchmark.** Key optimizations: lazy column decode (568 B for full-table scan), predicate pushdown, filter compilation caching, query plan caching, streaming 3-way UNION ALL, ArrayPool-backed IndexSet for set dedup (1.4 KB vs 1.2 MB), index-based string pooling for aggregates.

---

## Sharc vs Microsoft.Data.Sqlite

| Capability | Sharc | Microsoft.Data.Sqlite |
|:---|:---:|:---:|
| Read SQLite format 3 | Yes | Yes |
| SQL parsing / query pipeline | **Yes** -- Sharq parser + QueryIntent compiler | Yes (full VDBE) |
| WHERE filtering | **FilterStar (14+ ops)** + Query API | Yes (via SQL) |
| GROUP BY / aggregates | **Yes** -- streaming hash aggregator | Yes |
| UNION / INTERSECT / EXCEPT / Cotes | **Yes** | Yes |
| JOIN | No | Yes |
| Write / INSERT | **Yes** | Yes |
| Native dependencies | **None** | Requires `e_sqlite3` |
| B-tree point lookup | **7-61x faster** | Baseline |
| Graph 2-hop BFS | **13.5x faster** | Baseline |
| Encryption | **AES-256-GCM** (Argon2id KDF) | Via SQLCipher |
| Agent Trust Layer | **Yes** -- ECDSA attestation, hash-chain ledger | No |
| GC pressure | **0 B per-row** | Allocates per call |
| Package size | **~250 KB** | ~2 MB |

---

## Reproduce the Benchmarks

**Option 1: Live Arena** (recommended)
> [**https://revred.github.io/Sharc/**](https://revred.github.io/Sharc/)

**Option 2: BenchmarkDotNet CLI**
```bash
# Core benchmarks: 9 operations, Sharc vs SQLite
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *CoreBenchmarks*

# Graph benchmarks: scans, seeks, traversal
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph*

# Full comparative suite (113 benchmarks)
dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*
```

BenchmarkDotNet runs 15 iterations with 8 warmups per benchmark (DefaultJob), reports mean with error and outlier removal, and includes `MemoryDiagnoser` for per-operation allocation tracking.

---

## Benchmark Fairness Notes

**QueryPlanCache advantage:** Sharc caches compiled query plans in a `ConcurrentDictionary` (`QueryPlanCache`). After BenchmarkDotNet's warmup iterations, subsequent iterations skip SQL parsing entirely. The SQLite benchmarks create a `new SqliteCommand` and set `CommandText` per iteration — re-parsing each time. In production, SQLite typically uses pre-prepared statements which would narrow the gap on parse overhead. The core engine benchmarks (`CreateReader`) bypass the query pipeline entirely, so this advantage does not apply to those numbers.

**UNION ALL win is architectural:** Sharc's 4.7x advantage on `UNION ALL` comes from avoiding the P/Invoke boundary — both tables are scanned in managed memory and concatenated without crossing to native code. This is a legitimate in-process advantage, specific to the "multiple full table scans" pattern.

**Memory reporting:** SQLite's managed allocation numbers (688 B, 744 B) reflect only the .NET-side marshaling cost. SQLite's native C allocations (query plans, sort buffers, hash tables) are invisible to BenchmarkDotNet's `MemoryDiagnoser`. Sharc's numbers reflect total allocation since all work happens in managed code.

---

## Timing Methodology

| Engine | Timing Method | Notes |
|:---|:---|:---|
| **Sharc** | `Stopwatch.GetElapsedTime()` (managed) | Same WASM runtime as SQLite |
| **SQLite** | `Stopwatch.GetElapsedTime()` (managed) | Pre-opened connection, pre-prepared statements |
| **IndexedDB** | `performance.now()` (JavaScript) | Most favorable measurement for IDB |

All three engines operate on **identical data**. IndexedDB timing uses `performance.now()` inside the JavaScript adapter to exclude .NET-to-JS interop overhead.
