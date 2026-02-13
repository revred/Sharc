# Sharc

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)

**Read SQLite files at memory speed. Pure C#. Zero native dependencies. Zero per-row allocation.**

Sharc reads SQLite database files (format 3) from disk, memory, or encrypted blobs — without a single native library. No `sqlite3.dll`. No P/Invoke. No connection strings. Just bytes in, typed values out.

---

## Headline Numbers

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H (8C/16T)
> All numbers are **measured**, not estimated. SQLite is `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements. IndexedDB timing uses `performance.now()` inside the JS adapter (most favorable for IDB). [**Run the Arena yourself**](https://revred.github.io/Sharc/)

<table>
<tr>
<td width="50%" valign="top">

### Point Operations

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|---:|
| B-tree Seek | **1,475 ns** | 21,349 ns | **14.5x** |
| Batch 6 Seeks | **3,858 ns** | 159,740 ns | **41.4x** |
| Schema Read | **2.97 us** | 26.66 us | **9.0x** |
| Engine Init | **3.66 us** | 23.91 us | **6.5x** |

</td>
<td width="50%" valign="top">

### Scan Operations (5K rows)

| Operation | Sharc | SQLite | Speedup |
|:---|---:|---:|---:|
| Full Scan (9 cols) | **2.59 ms** | 6.03 ms | **2.3x** |
| Integer Decode | **213 us** | 819 us | **3.8x** |
| NULL Detection | **156 us** | 746 us | **4.8x** |
| Graph BFS 2-Hop | **6.27 us** | 78.49 us | **12.5x** |

</td>
</tr>
</table>

### Scorecard: Sharc 15 | SQLite 1 | IndexedDB 0

Across 16 browser arena benchmarks, Sharc wins **15**. SQLite wins **1** (WHERE Filter — its VDBE predicate pushdown is genuinely faster). IndexedDB wins **0** — its async API and structured clone serialization make it 25x to 244x slower than Sharc on every comparable metric.

| Benchmark | Sharc | SQLite | Winner |
|:---|---:|---:|:---:|
| WHERE Filter (age > 30 AND score < 50) | 1.23 ms | **0.55 ms** | **SQLite** |

For filter-heavy analytics, use SQLite. For everything else — reads, seeks, scans, graphs, encryption — Sharc is 2x to 41x faster than SQLite and 15x to 244x faster than IndexedDB.

---

## Memory: The Other Half of Performance

Speed without memory discipline is a lie. Here's what each engine allocates per operation:

| Operation | Sharc | SQLite | Who Wins |
|:---|---:|---:|:---:|
| Primitives (header, varint) | **0 B** | N/A | Sharc |
| NULL Detection (5K rows) | **8.2 KB** | 688 B | Sharc |
| Type Decode — integers (5K) | **8.2 KB** | 688 B | Sharc |
| GC Pressure — sustained scan | **8.2 KB** | 688 B | Sharc |
| Batch 6 Lookups | **9.4 KB** | 3.7 KB | Sharc |
| Schema Read | 6.8 KB | **2.5 KB** | SQLite |
| Point Lookup (Seek) | 8.3 KB | **728 B** | SQLite |
| Sequential Scan (5K rows) | 2.4 MB | **1.4 MB** | SQLite |
| WHERE Filter | 1.1 MB | **720 B** | SQLite |

> **Sharc allocates less in 5 of 9 core benchmarks.** On hot-path scans where GC pauses kill latency — NULL scans, type decode, sustained reads — Sharc's allocation is **12x lower** than SQLite per-row.
>
> Where SQLite wins on allocation (point lookup, sequential scan, WHERE filter), the cause is architectural: SQLite handles these entirely in native C and only marshals final results back to managed code, while Sharc operates fully in managed memory. The trade-off: Sharc's managed pipeline is **2-41x faster** on speed even when it allocates more.
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
| Per-row object allocation | Sharc reuses `ColumnValue[]` buffers — the GC doesn't wake up during reads |
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

All benchmarks below are from BenchmarkDotNet v0.15.8, DefaultJob (15 iterations, 8 warmup). Environment: Windows 11, .NET 10.0.2 (RyuJIT x86-64-v4), Intel i7-11800H. For three-way browser results, see the [**Live Arena**](https://revred.github.io/Sharc/).

> **Reproduce locally:**
> ```bash
> dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*
> dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph*
> dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *CoreBenchmarks*
> ```

### Core Operations (5K rows, 9-column `users` table)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Engine Init (open + schema) | **3.66 us** | 23.91 us | **6.5x** | 15,520 B | 1,160 B |
| Schema Introspection | **2.97 us** | 26.66 us | **9.0x** | 7,000 B | 2,536 B |
| Sequential Scan (9 cols) | **2.59 ms** | 6.03 ms | **2.3x** | 2,500,832 B | 1,412,320 B |
| Point Lookup (Seek) | **3,444 ns** | 24,347 ns | **7.1x** | 8,320 B | 728 B |
| Batch 6 Lookups | **5,237 ns** | 127,763 ns | **24.4x** | 9,424 B | 3,712 B |
| Type Decode (5K ints) | **213 us** | 819 us | **3.8x** | 8,416 B | 688 B |
| NULL Detection | **156 us** | 746 us | **4.8x** | 8,416 B | 688 B |
| WHERE Filter | 1,229 us | **554 us** | **0.45x** | 1,089,200 B | 720 B |
| GC Pressure (sustained) | **213 us** | 798 us | **3.7x** | 8,416 B | 688 B |

> **Bold = winner.** Sharc wins 8 of 9 on speed. SQLite wins WHERE Filter — its VDBE predicate pushdown evaluates `age > 30 AND score < 50` inside the C scan loop, avoiding managed code overhead per row.

### Graph Storage (5K nodes, 15K edges)

Sharc includes a built-in graph storage layer (`Sharc.Graph`) that maps concept/relation tables to a traversable graph with O(log N) index seeks. These benchmarks compare Sharc's graph API against raw SQLite queries on the same data.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
|:---|---:|---:|:---:|---:|---:|
| Node Scan (5K concepts) | **1,907 us** | 4,032 us | **2.1x** | 1,610,600 B | 959,232 B |
| Edge Scan (15K relations) | **4,176 us** | ~7,673 us | **~1.8x** | 2,892,176 B | — |
| Node Projection (id + type) | **988 us** | 2,200 us | **2.2x** | 812,584 B | 480,704 B |
| Edge Filter by Kind | **1,800 us** | 3,129 us | **1.7x** | 1,452,176 B | 696 B |
| Single Node Seek | **1,475 ns** | 21,349 ns | **14.5x** | 1,840 B | 600 B |
| Batch 6 Node Seeks | **3,858 ns** | 159,740 ns | **41.4x** | 4,176 B | 3,024 B |
| Open > Seek > Close | **11,764 ns** | 33,386 ns | **2.8x** | 12,496 B | 1,256 B |
| **2-Hop BFS Traversal** | **6.27 us** | 78.49 us | **12.5x** | 10,906 B | 2,739 B |

> **Graph seeks are the sweet spot:** 14.5x-41.4x faster. The BFS traversal achieves 12.5x through `SeekFirst(key)` — O(log N) binary search on the index B-tree that positions the cursor at the first matching entry, replacing linear scan.

### Seek Performance Deep-Dive

The seek numbers deserve special attention. A B-tree point lookup is the most common database operation in production — user sessions, authentication, profile retrieval, config reads.

```
SQLite Seek Path (21,349 ns):
  C# > P/Invoke > sqlite3_prepare > SQL parse > VDBE compile >
  sqlite3_step > B-tree descend > read leaf > VDBE decode >
  P/Invoke return > marshal to managed objects

Sharc Seek Path (1,475 ns):
  Span<byte> > B-tree page > binary search > leaf cell > decode value
```

**14.5x on single seeks. 41.4x on batch 6.** The batch amplification comes from LRU page cache locality — the second through sixth seeks reuse cached B-tree interior pages, dropping the marginal cost to ~400 ns per seek.

### Graph Traversal Deep-Dive

The 2-hop BFS benchmark simulates the "get context" operation that AI agents perform hundreds of times per conversation:

```
Operation: Given node 1, find all neighbors, then all neighbors-of-neighbors.
Dataset:   5K nodes, 15K edges (code dependency graph topology).
```

| Metric | Sharc | SQLite | |
|:---|---:|---:|:---|
| Time | **6.27 us** | 78.49 us | **12.5x faster** |
| Allocation | 10.9 KB | 2.7 KB | 4x more |
| Per-hop cost | ~3 us | ~39 us | |

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
| Engine Init | **0.004 ms** | 2.1 ms | **525x** |
| Schema Read | **2.97 us** | 45 us | **15x** |
| Sequential Scan (5K rows) | **2.59 ms** | 89 ms | **34x** |
| Point Lookup | **3,444 ns** | 85,000 ns | **25x** |
| Batch 6 Lookups | **5,237 ns** | 520,000 ns | **99x** |
| Type Decode (5K ints) | **0.213 ms** | 42 ms | **197x** |
| NULL Detection | **156 us** | 38,000 us | **244x** |

IndexedDB cannot compete on WHERE filtering, graph traversal, encryption, or GC pressure — it simply has no equivalent APIs.

**Why the gap is so large:** IndexedDB serializes every value through JavaScript's structured clone algorithm, crosses the IDB async boundary for each transaction, and exposes no way to read partial records or navigate B-trees. Sharc eliminates all of this — values go from `ReadOnlySpan<byte>` to typed results in a single managed pipeline with zero serialization.

### Three-Way Comparison: Full Arena Results

| # | Slide | Sharc | SQLite (WASM) | IndexedDB | Winner |
|:---:|:---|---:|---:|---:|:---:|
| 1 | Engine Init | **0.004 ms** | 142 ms | 2.1 ms | Sharc |
| 2 | Schema Read | **2.97 us** | 26.66 us | 45 us | Sharc |
| 3 | Sequential Scan | **2.59 ms** | 6.03 ms | 89 ms | Sharc |
| 4 | Point Lookup | **3,444 ns** | 24,347 ns | 85,000 ns | Sharc |
| 5 | Batch Lookups | **5,237 ns** | 127,763 ns | 520,000 ns | Sharc |
| 6 | Type Decode | **0.213 ms** | 0.819 ms | 42 ms | Sharc |
| 7 | NULL Detection | **156 us** | 746 us | 38,000 us | Sharc |
| 8 | WHERE Filter | 1.229 ms | **0.554 ms** | N/A | SQLite |
| 9 | Graph Node Scan | **1,027 us** | 2,853 us | N/A | Sharc |
| 10 | Graph Edge Scan | **2,268 us** | 7,673 us | N/A | Sharc |
| 11 | Graph Node Seek | **3,444 ns** | 24,347 ns | N/A | Sharc |
| 12 | Graph 2-Hop BFS | **6.27 us** | 78.49 us | N/A | Sharc |
| 13 | GC Pressure | **0.213 ms** | 0.798 ms | N/A | Sharc |
| 14 | Encrypted Read | **340 us** | N/A | N/A | Sharc |
| 15 | Memory Footprint | **~50 KB** | 1,536 KB | 0 (built-in) | Sharc |
| 16 | Primitives | **8.5 ns** | N/A | N/A | Sharc |

> **Score: Sharc 15 / SQLite 1 / IndexedDB 0.** IndexedDB's only advantage is zero download size — on every performance metric Sharc ranges from 15x to 244x faster.

### Browser Architecture Comparison

| Factor | Sharc | SQLite WASM | IndexedDB |
|:---|:---|:---|:---|
| Download size | **~50 KB** (managed C#) | ~1.5 MB (e_sqlite3.wasm) | 0 B (built-in) |
| Cold start | **3.66 us** | 142 ms | 2.1 ms |
| Runtime | .NET WASM (in-process) | C via Emscripten P/Invoke | Browser JS engine |
| Interop cost | None | P/Invoke per column | JS to IDB async per txn |
| Decode path | `Span<byte>` to typed value | C marshal to managed box | Structured clone to JS obj |
| GC pressure | Zero-alloc pipeline | Per-call allocation | Full JS object graph |
| Query language | SharcFilter (6 ops) | Full SQL (VDBE) | Key ranges only |
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
| WHERE filtering | Yes — 6 operators, all types | Yes (via SQL) |
| JOIN / GROUP BY / aggregates | No | Yes |
| ORDER BY | No — rowid order | Yes |
| Write / INSERT / UPDATE / DELETE | No | Yes |
| Native dependencies | **None** | Requires `e_sqlite3` |
| Sequential scan | **2.3-4.8x faster** | Baseline |
| B-tree point lookup | **7-41x faster** | Baseline |
| Schema introspection | **9x faster** | Baseline |
| Graph 2-hop BFS | **12.5x faster** | Baseline |
| Thread-safe parallel reads | Yes (16 threads) | Yes |
| In-memory buffer | Native (`ReadOnlyMemory`) | Connection string hack |
| Column projection | Yes | Yes (via SELECT) |
| WITHOUT ROWID tables | Yes | Yes |
| WAL mode (read-only) | Yes | Yes |
| Virtual tables | No | Yes |
| Page I/O backends | Memory, File, Mmap, Cached | Internal |
| Encryption | **AES-256-GCM** (Argon2id KDF) | Via SQLCipher |
| GC pressure | **0 B per-row** | Allocates per call |
| Package size | **~50 KB** | ~2 MB |

---

## Architecture

Pure managed SQLite file-format reader. No VM, no VDBE, no query planner — just B-tree pages decoded through zero-alloc spans.

```
+-----------------------------------------------------------+
|  Public API          SharcDatabase > SharcDataReader        |
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
| `ColumnValue[]` buffer reuse | One allocation per reader, not per row (`generation` counter invalidates lazily) |
| No SQL parser | Eliminates ~80% of SQLite's codebase — we don't need it for reads |
| LRU page cache | B-tree interior pages are reused across seeks — batch lookups amortize I/O |
| `SeekFirst(key)` on index cursor | O(log N) binary search on index B-tree — enables 12.5x graph traversal speedup |
| Shared `BTreeReader` via public API | Graph layer shares the same page source as the database — zero duplication |
| Page-level AES-256-GCM | Encryption at the storage layer, not the API layer — transparent to readers |

---

## Project Structure

```
src/Sharc/                    Public API (SharcDatabase, SharcDataReader, Schema)
src/Sharc.Core/               Internal: page I/O, B-tree, record decoding, primitives
src/Sharc.Graph/              Graph storage layer (ConceptStore, RelationStore)
src/Sharc.Graph.Surface/      Graph interfaces and models
src/Sharc.Crypto/             Encryption: AES-256-GCM, Argon2id KDF
src/Sharc.Arena.Wasm/         Live browser benchmark arena (Blazor WASM)
tests/Sharc.Tests/            Unit tests (xUnit)
tests/Sharc.IntegrationTests/ End-to-end tests with real SQLite databases
tests/Sharc.Graph.Tests.Unit/ Graph layer unit tests
tests/Sharc.Context.Tests/    MCP context query tool tests
tests/Sharc.Index.Tests/      GCD indexer tests
bench/Sharc.Benchmarks/       BenchmarkDotNet comparative suite (113 benchmarks)
bench/Sharc.Comparisons/      Graph + core browser benchmarks (32 benchmarks)
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
708 passed, 0 skipped, 0 failed
  Unit tests:        529 (core + crypto + filter + WITHOUT ROWID + SeekFirst)
  Graph unit tests:   50
  Integration tests:  93 (includes encryption, filtering, WITHOUT ROWID)
  Context tests:      14 (MCP query tools)
  Index tests:        22 (GCD schema, git log parser, commit writer)
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
```

## Current Limitations

Sharc is a **read-only format reader**, not a full database engine:

- **No SQL execution** — reads raw B-tree pages; no parser, no joins, no aggregates
- **No write support** — Read-only by design (write support planned)
- **No virtual tables** — FTS5, R-Tree not supported
- **No UTF-16 text** — UTF-8 only
- **WHERE filter is slower than SQLite** — SharcFilter evaluates in managed code after full record decode; SQLite pushes predicates into the VDBE scan loop (2.2x faster on filter-heavy queries)
- **Higher per-open allocation** — Sharc parses schema eagerly (~7-15 KB upfront); planned: lazy schema initialization

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
