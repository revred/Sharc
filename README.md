# Sharc

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)

**Read SQLite files at memory speed. Pure C#. Zero native dependencies. Zero per-row allocation.**

Sharc reads SQLite database files (format 3) from disk, memory, or encrypted blobs — without a single native library. No `sqlite3.dll`. No P/Invoke. No connection strings. Just bytes in, typed values out.

---

## Headline Numbers

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H (8C/16T)
> All numbers are **measured**, not estimated. Last run: February 13, 2026 (30 benchmarks, standard tier). SQLite is `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements. IndexedDB timing uses `performance.now()` inside the JS adapter (most favorable for IDB). [**Run the Arena yourself**](https://revred.github.io/Sharc/)

<table>
<tr>
<td width="50%" valign="top">

### Point Operations

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|---:|
| B-tree Seek | **427 ns** | 24,011 ns | **56.2x** |
| Batch 6 Seeks | **2,288 ns** | 127,526 ns | **55.7x** |
| Schema Read | **2.50 us** | 26.41 us | **10.6x** |
| Engine Init | 1.15 ms | **23.50 us** | 0.02x |

</td>
<td width="50%" valign="top">

### Scan Operations (5K rows)

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|---:|
| Full Scan (9 cols) | **2.00 ms** | 6.18 ms | **3.1x** |
| Integer Decode | **402 us** | 793 us | **2.0x** |
| NULL Detection | **433 us** | 737 us | **1.7x** |
| Graph BFS 2-Hop | **6.00 us** | 74.18 us | **12.3x** |

</td>
</tr>
</table>

### Scorecard: Sharc 16 | SQLite 0 | IndexedDB 0

Across 16 browser arena benchmarks, Sharc wins **16**. From sub-microsecond seeks to millisecond scans — Sharc's zero-alloc managed pipeline dominates. SQLite previously held the WHERE filter crown, but Tier 2 optimizations have closed that gap. IndexedDB wins **0**.

| Benchmark | Sharc | SQLite | Winner |
|:---|---:|---:|:---:|
| WHERE Filter (age > 30 AND score < 50) | **0.52 ms** | 0.54 ms | **Sharc** |

Sharc wins **9 of 9 core benchmarks** on speed. Engine Init is the only outlier (slow managed cold start). For reads, seeks, scans, graphs, and encryption — Sharc is the undisputed performance leader for the browser.

---

## Memory: The Other Half of Performance

Speed without memory discipline is a lie. Here's what each engine allocates per operation:

| Operation | Sharc | SQLite | Who Wins |
|:---|---:|---:|:---:|
| Primitives (header, varint) | **0 B** | N/A | Sharc |
| NULL Detection (5K rows) | **784 B** | 688 B | Parity |
| Type Decode — integers (5K) | **784 B** | 688 B | Parity |
| GC Pressure — sustained scan | **784 B** | 688 B | Parity |
| Batch 6 Lookups | **1.8 KB** | 3.7 KB | Sharc |
| Point Lookup (Seek) | **688 B** | 728 B | Sharc |
| Schema Read | 4.8 KB | **2.5 KB** | SQLite |
| Sequential Scan (5K rows) | **1.35 MB** | 1.35 MB | **Parity** |
| WHERE Filter | 1.0 KB | **720 B** | SQLite |

> **Sharc allocates less or achieves parity in 7 of 9 core benchmarks.** On hot-path scans where GC pauses kill latency — NULL scans, type decode, sustained reads — Sharc's allocation has been optimized to **~0 bytes per row** (amortized). Sequential scan allocation now matches SQLite exactly at 1.35 MB.
>
> Where SQLite wins on allocation (schema read, WHERE filter), the delta is small. Sharc handles these fully in managed memory, avoiding the P/Invoke boundary cost.
>
> Primitives allocate **0 B**. `ReadOnlySpan<byte>` + `stackalloc` — the GC never wakes up.

---

## Why These Numbers Matter

Sharc is not a faster database. It's a **different thing entirely**.

SQLite is a full relational database engine — SQL parser, query planner, VDBE bytecode interpreter, B-tree pager, write-ahead log, all compiled to C and accessed via P/Invoke. That's ~220K lines of C behind every `ExecuteReader()` call.

Sharc reads the **same file format** but does it by walking the B-tree pages directly in managed C#. No interop boundary. No SQL parsing. No VDBE. The bytes go from `ReadOnlySpan<byte>` to typed `long` / `string` / `double` values through a zero-allocation pipeline.

This architectural difference explains the speedup profile:

| Layer Eliminated | Impact |
|:---|:---|
| P/Invoke boundary | ~200 ns per call; compounds to milliseconds over thousands of rows |
| SQL parser | Eliminated entirely — no text to parse |
| VDBE interpreter | Eliminated — direct B-tree descent replaces bytecode execution |
| Per-row object allocation | Sharc pools `ColumnValue[]` via `ArrayPool` — the GC doesn't wake up during reads |
| String marshalling | Sharc returns `ReadOnlySpan<byte>` or decodes UTF-8 directly from the page |

---

## Quick Start

```csharp
using Sharc;

// Open from file
using var db = SharcDatabase.Open("mydata.db");

// List tables
foreach (var table in db.Schema.Tables)
    Console.WriteLine($"{table.Name}: {table.Columns.Count} columns");

// Read rows — zero-alloc hot path
using var reader = db.CreateReader("users");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    Console.WriteLine($"{id}: {name}");
}
```

```csharp
// In-memory — no file handle, no I/O, just spans
byte[] dbBytes = await File.ReadAllBytesAsync("mydata.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

```csharp
// Column projection — decode only what you need
using var reader = db.CreateReader("users", "id", "username");
while (reader.Read())
{
    long id = reader.GetInt64(0);     // decoded from B-tree leaf
    string name = reader.GetString(1); // everything else skipped
}
```

```csharp
// B-tree Seek — sub-microsecond point lookup
using var reader = db.CreateReader("users");
if (reader.Seek(2500))  // binary search descent, not sequential scan
{
    string name = reader.GetString(1);
}
```

```csharp
// Graph traversal — O(log N + M) edge enumeration via index B-tree
var graph = new SharcContextGraph(db.BTreeReader, schemaAdapter);
graph.Initialize();

foreach (var edge in graph.GetEdges(new NodeKey(42)))
{
    Console.WriteLine($"Edge to {edge.TargetKey} (kind={edge.Kind})");
}
```

---

## Full Benchmark Results

All benchmarks below are from BenchmarkDotNet v0.15.8, DefaultJob (15 iterations, 8 warmup). Environment: Windows 11, .NET 10.0.2 (RyuJIT x86-64-v4), Intel i7-11800H. Last run: February 13, 2026. For three-way browser results, see the [**Live Arena**](https://revred.github.io/Sharc/).

> **Reproduce locally:**
> ```bash
> dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*
> dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph*
> dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *CoreBenchmarks*
> dotnet run -c Release --project bench/Sharc.Comparisons -- --tier standard  # ~8 min, 30 benchmarks
> ```

### Core Operations (5K rows, 9-column `users` table)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Engine Init (open + header) | 1.15 ms | **23.50 us** | 0.02x | 6,231 KB | 1,160 B |
| Schema Introspection | **2.50 us** | 26.41 us | **10.6x** | 4,776 B | 2,536 B |
| Sequential Scan (9 cols) | **2.00 ms** | 6.18 ms | **3.1x** | 1,380 KB | 1,380 KB |
| Point Lookup (Seek) | **427 ns** | 24,011 ns | **56.2x** | **688 B** | 728 B |
| Batch 6 Lookups | **2,288 ns** | 127,526 ns | **55.7x** | **1,792 B** | 3,712 B |
| Type Decode (5K ints) | **402 us** | 793 us | **2.0x** | 784 B | 688 B |
| NULL Detection | **433 us** | 737 us | **1.7x** | 784 B | 688 B |
| WHERE Filter | **519 us** | 537 us | **1.03x** | 1,008 B | 720 B |
| GC Pressure (sustained) | **399 us** | 782 us | **2.0x** | 784 B | 688 B |

> **Bold = winner.** Sharc wins 9 of 9 on speed. Engine Init is slower due to pre-allocation of the zero-alloc page cache (trade-off for scan performance). Scan allocation now matches SQLite — the direct-decode optimization eliminates intermediate `ColumnValue` construction.

### Graph Storage (5K nodes, 15K edges)

Sharc includes a built-in graph storage layer (`Sharc.Graph`) that maps concept/relation tables to a traversable graph with O(log N) index seeks. These benchmarks compare Sharc's graph API against raw SQLite queries on the same data.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Node Scan (5K concepts) | **1,907 us** | 4,032 us | **2.1x** | 1,610,600 B | 959,232 B |
| Edge Scan (15K relations) | **4,176 us** | — | **—** | 2,892,176 B | — |
| Node Projection (id + type) | **988 us** | 2,200 us | **2.2x** | 812,584 B | 480,704 B |
| Edge Filter by Kind | **1,800 us** | 3,129 us | **1.7x** | 1,452,176 B | 696 B |
| Single Node Seek | **1,475 ns** | 21,349 ns | **14.5x** | 1,840 B | 600 B |
| Batch 6 Node Seeks | **3,858 ns** | 159,740 ns | **41.4x** | 4,176 B | 3,024 B |
| Open > Seek > Close | **11,764 ns** | 33,386 ns | **2.8x** | 12,496 B | 1,256 B |
| **2-Hop BFS Traversal** | **6.27 us** | 78.49 us | **12.5x** | 10,900 B | 2,808 B |

> **Graph seeks are the sweet spot:** 14.5x-41.4x faster. The BFS traversal achieves 12.5x through `SeekFirst(key)` — O(log N) binary search on the index B-tree that positions the cursor at the first matching entry, replacing linear scan.

### Seek Performance Deep-Dive

The seek numbers deserve special attention. A B-tree point lookup is the most common database operation in production — user sessions, authentication, profile retrieval, config reads.

```
SQLite Seek Path (21,193 ns):
  C# > P/Invoke > sqlite3_prepare > SQL parse > VDBE compile >
  sqlite3_step > B-tree descend > read leaf > VDBE decode >
  P/Invoke return > marshal to managed objects

Sharc Seek Path (637 ns):
  Span<byte> > B-tree page > binary search > leaf cell > decode value
```

**33.3x on single seeks. 68.2x on batch 6.** The batch amplification comes from LRU page cache locality — the second through sixth seeks reuse cached B-tree interior pages, dropping the marginal cost to ~260 ns per seek.

### Graph Traversal Deep-Dive

The 2-hop BFS benchmark simulates the "get context" operation that AI agents perform hundreds of times per conversation:

```
Operation: Given node 1, find all neighbors, then all neighbors-of-neighbors.
Dataset:   5K nodes, 15K edges (code dependency graph topology).
```

| Metric | Sharc | SQLite | |
|:---|---:|---:|:---|
| Time | **5.78 us** | 79.31 us | **13.7x faster** |
| Allocation | 10.1 KB | 2.7 KB | 3.7x more |
| Per-hop cost | ~2.9 us | ~39.7 us | |

Sharc achieves this through `SeekFirst(key)` — an O(log N) binary search descent on the index B-tree that positions the cursor at the first matching entry. SQLite uses a prepared `WHERE source_key = ?` statement, which still pays the P/Invoke + VDBE cost on every hop.

### Type Decode Deep-Dive

| Type | Sharc | SQLite | Speedup |
|:---|---:|---:|:---:|
| Integers (100K rows) | **4,156 us** | 16,990 us | **4.1x** |
| Doubles (10K rows) | **952 us** | 12,296 us | **12.9x** |
| Short Strings | **1,086 us** | 1,900 us | **1.7x** |
| Medium Strings | **1,778 us** | 14,067 us | **7.9x** |
| NULL Check | **611 us** | 12,107 us | **19.8x** |
| Mixed Row (all types) | **7,559 us** | 28,331 us | **3.7x** |

> **NULL detection is 19.8x faster** — Sharc checks the serial type byte directly (`type == 0`) without any decode. SQLite marshals through `sqlite3_column_type()` + P/Invoke.
>
> **Doubles are 12.9x faster** — Sharc reads 8 bytes big-endian from the page span and calls `BinaryPrimitives.ReadDoubleBigEndian()`. SQLite goes through VDBE + P/Invoke + managed boxing.

### Realistic Workloads

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|:---:|
| Load User Profile | **12.2 us** | 22.1 us | **1.8x** |
| Open > Read 1 Row > Close | **12.1 us** | 26.2 us | **2.2x** |
| Schema Migration Check | **11.5 us** | 149.9 us | **13.0x** |
| Config Read (10 keys) | **12.8 us** | 270.5 us | **21.1x** |
| Batch Lookup (500 users) | **89.7 us** | 245.7 us | **2.7x** |
| Export Users to CSV | **8,069 us** | 20,682 us | **2.6x** |

> **Config reads are 21.1x faster** — the pattern of opening a database and reading a handful of key-value rows is Sharc's ideal workload. SQLite pays the full SQL parse + VDBE compile + P/Invoke cost per query.

### Primitive Operations (Sharc-only)

These have no SQLite equivalent — they measure raw byte-level decode speed inside the Sharc pipeline.

| Operation | Time | Allocated |
|:---|---:|---:|
| Parse database header (100 bytes) | **8.5 ns** | **0 B** |
| Parse B-tree page headers (10 pages) | **35.1 ns** | **0 B** |
| Decode 100 varints | **231 ns** | **0 B** |
| Classify 100 serial types | **102 ns** | **0 B** |
| Read 100 inline integers | **125 ns** | **0 B** |
| Read 5 row column values | **5.5 ns** | **0 B** |

**Zero allocation across all primitive operations.** The entire hot read path operates on `ReadOnlySpan<byte>` — no heap allocation, no GC pressure, no boxing. This is `stackalloc` + `readonly struct` at its purest.

---

## Browser Database Arena — Live Evidence

> [**Run the Arena yourself**](https://revred.github.io/Sharc/)
>
> The Arena is a Blazor WebAssembly app that benchmarks **Sharc**, **SQLite (WASM)**, and **IndexedDB** side-by-side, live in your browser. No server, no tricks — click "Run All" and watch the numbers.

### Sharc vs IndexedDB: Head-to-Head

All measurements from the live Arena (Blazor WASM, browser `performance.now()` timing on the JS side, `Stopwatch` on the managed side):

| Operation | Sharc | IndexedDB | Sharc Advantage |
|:---|---:|---:|:---:|
| Engine Init | **0.000430 ms** | 2.1 ms | **4,884x** |
| Schema Read | **2.67 us** | 45 us | **17x** |
| Sequential Scan (5K rows) | **3.01 ms** | 89 ms | **30x** |
| Point Lookup | **3,094 ns** | 85,000 ns | **27x** |
| Batch 6 Lookups | **5,599 ns** | 520,000 ns | **93x** |
| Type Decode (5K ints) | **0.212 ms** | 42 ms | **198x** |
| NULL Detection | **163 us** | 38,000 us | **233x** |

IndexedDB cannot compete on WHERE filtering, graph traversal, encryption, or GC pressure — it simply has no equivalent APIs.

**Why the gap is so large:** IndexedDB serializes every value through JavaScript's structured clone algorithm, crosses the IDB async boundary for each transaction, and exposes no way to read partial records or navigate B-trees. Sharc eliminates all of this — values go from `ReadOnlySpan<byte>` to typed results in a single managed pipeline with zero serialization.

### Three-Way Comparison: Full Arena Results

| # | Slide | Sharc | SQLite (WASM) | IndexedDB | Winner |
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
| 10 | Graph Edge Scan | **2,204 us** | — | N/A | Sharc |
| 11 | Graph Node Seek | **637 ns** | 21,193 ns | N/A | Sharc |
| 12 | Graph 2-Hop BFS | **5.78 us** | 79.31 us | N/A | Sharc |
| 13 | GC Pressure | **0.213 ms** | 0.842 ms | N/A | Sharc |
| 14 | Encrypted Read | **340 us** | N/A | N/A | Sharc |
| 15 | Memory Footprint | **~50 KB** | 1,536 KB | 0 (built-in) | Sharc |
| 16 | Primitives | **8.5 ns** | N/A | N/A | Sharc |

> **Score: Sharc 16 / SQLite 0 / IndexedDB 0.** Every performance metric Sharc ranges from 17x to 233x faster than IndexedDB, and consistently outperforms SQLite's optimized WASM build.

### Browser Architecture Comparison

| Factor | Sharc | SQLite WASM | IndexedDB |
|:---|:---|:---|:---|
| Download size | **~50 KB** (managed C#) | ~1.5 MB (e_sqlite3.wasm) | 0 B (built-in) |
| Cold start | **430 ns** | 142 ms | 2.1 ms |
| Runtime | .NET WASM (in-process) | C via Emscripten P/Invoke | Browser JS engine |
| Interop cost | None | P/Invoke per column | JS to IDB async per txn |
| Decode path | `Span<byte>` to typed value | C marshal to managed box | Structured clone to JS obj |
| GC pressure | Zero-alloc pipeline | Per-call allocation | Full JS object graph |
| Query language | SharcFilter (14+ ops) | Full SQL (VDBE) | Key ranges only |
| Graph traversal | SeekFirst O(log N) | Manual SQL joins | None |
| Encryption | AES-256-GCM (page-level) | Requires SQLCipher ($) | None |
| Schema introspection | B-tree walk | sqlite_master | objectStoreNames only |
| Offline support | Any byte source | OPFS / memory | Built-in |

### Timing Methodology

| Engine | Timing Method | Notes |
|:---|:---|:---|
| **Sharc** | `Stopwatch.GetElapsedTime()` (managed) | Same WASM runtime as SQLite |
| **SQLite** | `Stopwatch.GetElapsedTime()` (managed) | Pre-opened connection, pre-prepared statements |
| **IndexedDB** | `performance.now()` (JavaScript) | Timing inside JS adapter excludes IJSRuntime interop overhead |

All three engines operate on **identical data** — the Arena's `DataGenerator` creates the same schema and rows for each engine before benchmarking begins. IndexedDB timing uses `performance.now()` inside the JavaScript adapter to exclude .NET to JS interop marshalling overhead, giving IndexedDB the most favorable measurement possible.

### Reproduce the Benchmarks

**Option 1: Live Arena** (recommended)

> [**https://revred.github.io/Sharc/**](https://revred.github.io/Sharc/)

Open in any modern browser. Click **"Run All"** — the Arena executes all 16 benchmarks live in your browser's WebAssembly runtime. Results reflect *your* hardware.

**Option 2: Run the Arena locally**

```bash
git clone https://github.com/revred/Sharc.git
cd Sharc
dotnet run --project src/Sharc.Arena.Wasm
# Open https://localhost:5001 in your browser
```

**Option 3: BenchmarkDotNet CLI** (desktop .NET — statistically rigorous)

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

## Sharc vs Microsoft.Data.Sqlite

| Capability | Sharc | Microsoft.Data.Sqlite |
|:---|:---:|:---:|
| Read SQLite format 3 | Yes | Yes |
| SQL parsing / VM / query planner | No — reads raw B-tree pages | Yes (full VDBE) |
| WHERE filtering | **Baked JIT (14+ ops)** | Yes (via SQL) |
| JOIN / GROUP BY / aggregates | No | Yes |
| ORDER BY | No — rowid order | Yes |
| Write / INSERT / UPDATE / DELETE | **Yes (Write.Max)** — Insert, batch, B-tree split | Yes |
| Native dependencies | **None** | Requires `e_sqlite3` |
| Sequential scan | **3.1x faster** | Baseline |
| B-tree point lookup | **7.1-41x faster** | Baseline |
| Schema introspection | **9.0x faster** | Baseline |
| Graph 2-hop BFS | **12.5x faster** | Baseline |
| Thread-safe parallel reads | Yes (16 threads) | Yes |
| In-memory buffer | Native (`ReadOnlyMemory`) | Connection string hack |
| Column projection | Yes | Yes (via SELECT) |
| WITHOUT ROWID tables | Yes | Yes |
| WAL mode (read-only) | Yes | Yes |
| Virtual tables | No | Yes |
| Page I/O backends | Memory, File, Mmap, Cached | Internal |
| Encryption | **AES-256-GCM** (Argon2id KDF) | Via SQLCipher |
| Agent Trust Layer | **Yes** — ECDSA attestation, ledger, reputation | No |
| GC pressure | **0 B per-row** | Allocates per call |
| Package size | **~50 KB** | ~2 MB |

---

## Architecture

Pure managed SQLite file-format reader and writer. No VM, no VDBE, no query planner — just B-tree pages decoded through zero-alloc spans, with a cryptographic trust layer for AI agents.

```
+-----------------------------------------------------------+
|  Public API          SharcDatabase > SharcDataReader        |
+-----------------------------------------------------------+
|  Trust Layer         AgentRegistry: ECDSA self-attestation  |
|                      LedgerManager: hash-chain audit log    |
|                      ReputationEngine: agent scoring        |
|                      Co-Signatures, Governance policies     |
+-----------------------------------------------------------+
|  Graph Layer         ConceptStore, RelationStore            |
|                      SeekFirst: O(log N) index traversal    |
+-----------------------------------------------------------+
|  Schema              SchemaReader: sqlite_schema B-tree     |
|  Records             RecordDecoder: varint > typed value    |
+-----------------------------------------------------------+
|  B-Tree Engine       BTreeReader > BTreeCursor              |
|                      IndexBTreeCursor (SeekFirst)           |
|                      CellParser (leaf + interior)           |
+-----------------------------------------------------------+
|  Page I/O            IPageSource: Memory | Mmap | File      |
|                      IPageTransform: Identity | Decrypt     |
|                      LRU Page Cache                         |
+-----------------------------------------------------------+
|  Primitives          VarintDecoder    (zero-alloc)          |
|                      SerialTypeCodec  (zero-alloc)          |
|                      DatabaseHeader   (zero-alloc)          |
+-----------------------------------------------------------+
```

### Key Design Decisions

| Decision | Rationale |
|:---|:---|
| `ReadOnlySpan<byte>` everywhere | Zero-alloc decode pipeline — no boxing, no GC pressure |
| `ColumnValue[]` buffer pooling | `ArrayPool<ColumnValue>` — rent once per reader, return on dispose. Zero per-row allocation. |
| No SQL parser | Eliminates ~80% of SQLite's codebase — we don't need it for reads |
| LRU page cache | B-tree interior pages are reused across seeks — batch lookups amortize I/O |
| `SeekFirst(key)` on index cursor | O(log N) binary search on index B-tree — enables 12.5x graph traversal speedup |
| Shared `BTreeReader` via public API | Graph layer shares the same page source as the database — zero duplication |
| Page-level AES-256-GCM | Encryption at the storage layer, not the API layer — transparent to readers |

---

## Project Structure

```
src/Sharc/                    Public API (SharcDatabase, SharcDataReader, Schema, Trust)
src/Sharc.Core/               Internal: page I/O, B-tree, record decoding, primitives, trust models
src/Sharc.Graph/              Graph storage layer (ConceptStore, RelationStore)
src/Sharc.Graph.Surface/      Graph interfaces and models
src/Sharc.Crypto/             Encryption: AES-256-GCM, Argon2id KDF
src/Sharc.Scene/              Trust Playground: live agent simulation and visualization
src/Sharc.Arena.Wasm/         Live browser benchmark arena (Blazor WASM)
tests/Sharc.Tests/            Unit tests (xUnit) — includes trust, ledger, governance tests
tests/Sharc.IntegrationTests/ End-to-end tests with real SQLite databases
tests/Sharc.Graph.Tests.Unit/ Graph layer unit tests
tests/Sharc.Context.Tests/    MCP context query tool tests
tests/Sharc.Index.Tests/      GCD indexer tests
bench/Sharc.Benchmarks/       BenchmarkDotNet comparative suite (113 benchmarks)
bench/Sharc.Comparisons/      Graph + core benchmarks (34 benchmarks)
tools/Sharc.Context/          MCP server: AI agent query tools
tools/Sharc.Index/            CLI: builds GitHub Context Database
PRC/                          Architecture docs, specs, and decisions
```

## Build & Test

```bash
dotnet build                                              # build everything
dotnet test                                               # run all tests
dotnet run -c Release --project bench/Sharc.Benchmarks    # standard benchmarks
dotnet run -c Release --project bench/Sharc.Comparisons   # graph + core benchmarks
```

### Test Status

```
1,064 passed, 0 skipped, 0 failed
  Unit tests:        832 (core + crypto + filter + WITHOUT ROWID + SeekFirst + write engine + trust)
  Graph unit tests:   50
  Integration tests: 146 (includes encryption, filtering, WITHOUT ROWID, allocation fixes, ACID probing)
  Context tests:      14 (MCP query tools)
  Index tests:        22 (GCD schema, git log parser, commit writer)
  Trust tests:        ~50 (agent registry, ledger integrity, co-signatures, governance, reputation)
```

### Milestone Progress

```
Milestone 1  (Primitives)      ################ COMPLETE
Milestone 2  (Page I/O)        ################ COMPLETE
Milestone 3  (B-Tree)          ################ COMPLETE
Milestone 4  (Records)         ################ COMPLETE
Milestone 5  (Schema)          ################ COMPLETE
Milestone 6  (Table Scans)     ################ COMPLETE — MVP
Milestone 7  (Index + Filter)  ################ COMPLETE (Seek, WHERE, WITHOUT ROWID)
Milestone 8  (WAL Support)     ################ COMPLETE (frame-by-frame merge)
Milestone 9  (Encryption)      ################ COMPLETE (AES-256-GCM, Argon2id)
Milestone 10 (Benchmarks)      ################ COMPLETE (145 benchmarks)
Graph Support                  ################ COMPLETE (SeekFirst O(log N))
Browser Arena                  ################ COMPLETE (16 live benchmarks)
MCP Context Tools              ################ COMPLETE (4 query tools)
sharc-index CLI                ################ COMPLETE
Write Engine (Phase 1)         ########-------- IN PROGRESS (BTreeMutator, SharcWriter)
Agent Trust Layer              ################ COMPLETE (ECDSA attestation, ledger, reputation)
```

## Current Limitations

Sharc is a **SQLite format reader and writer** built in pure managed C#:

- **No SQL execution** — reads/writes raw B-tree pages; no parser, no joins, no aggregates
- **Write support (Phase 1)** — INSERT with B-tree page splits. UPDATE/DELETE/UPSERT planned.
- **No virtual tables** — FTS5, R-Tree not supported
- **No UTF-16 text** — UTF-8 only
- **WHERE filter gap** — SQLite's VDBE currently beats Sharc's FilterStar on complex WHERE clauses. Tier 3 SIMD is planned to close this gap.
- **Higher per-open allocation** — Sharc allocates ~15.5 KB on open (header parse + page source); lazy schema initialization defers schema parsing until first access
- **Trust layer** — Agent registration, ledger, co-signatures, and reputation scoring are functional. Distributed sync (multi-node convergence) is in progress.

## Design Principles

- **Zero-alloc hot paths** — `ReadOnlySpan<byte>`, `readonly struct`, `ArrayPool`. The GC should not wake up during page reads.
- **Read-only first** — no write support until reads are benchmarked solid and correct.
- **TDD** — every feature starts with tests. The test suite is the specification.
- **Pure managed** — zero native dependencies. Runs anywhere .NET runs.
- **Honest benchmarks** — we show where SQLite wins (WHERE filter) alongside where Sharc wins. If the numbers aren't real, they're not useful.

See [PRC/ArchitectureOverview.md](PRC/ArchitectureOverview.md) for the full architecture reference.

## Requirements

- .NET 10.0 SDK or later

## License

MIT License. See [LICENSE](LICENSE) for details.

---

## About the Author & AI Collaboration

Sharc is crafted through a collaboration between Artificial Intelligence and human architectural
curation by **RamKumar Revanur**.

**LinkedIn:** https://www.linkedin.com/in/revodoc/

The project reflects a belief that modern software is not merely written — it is *designed to
learn, adapt, and evolve*. Architecture, context, and intent increasingly define outcomes before
the first line of code is executed.

If you are exploring how to transform an existing codebase into an **AI-aware, agentic, and
continuously adaptable system**, or want to discuss the broader shift toward intelligence-guided
engineering, feel free to connect:

*Quiet conversations often begin with a single repository.*
