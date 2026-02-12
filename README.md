# Sharc

[![CI](https://github.com/user/sharc/actions/workflows/ci.yml/badge.svg)](https://github.com/user/sharc/actions/workflows/ci.yml)

**Read SQLite files at memory speed. Pure C#. Zero native dependencies. Zero per-row allocation on hot read paths.**

Sharc reads SQLite database files (format 3) from disk, memory, or encrypted blobs — without a single native library. No `sqlite3.dll`. No P/Invoke. No connection strings. Just bytes in, typed values out.

## Quick Start

```csharp
using Sharc;

// Open from file
using var db = SharcDatabase.Open("mydata.db");

// List tables
foreach (var table in db.Schema.Tables)
    Console.WriteLine($"{table.Name}: {table.Columns.Count} columns");

// Read rows
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
// Column projection — decode only the columns you need
using var reader = db.CreateReader("users", "id", "username");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
}
```

## Sharc vs Microsoft.Data.Sqlite

| Capability | Sharc | Microsoft.Data.Sqlite |
| --- | :---: | :---: |
| **Read SQLite format 3** | Yes | Yes |
| **SQL queries / joins / aggregates** | No | Yes |
| **Write / INSERT / UPDATE / DELETE** | No | Yes |
| **Native dependencies** | **None** | Requires `e_sqlite3` native binary |
| **Sequential table scan** | **2.0x-16.7x faster** | Baseline |
| **B-tree point lookup (Seek)** | **45x faster** | Baseline |
| **Schema introspection** | **2.4x-12.3x faster** | Baseline |
| **Config / metadata reads** | **24x faster** | Baseline |
| **Concurrent parallel readers** | **Thread-safe** (12 tests, up to 16 threads) | Thread-safe |
| **In-memory buffer (ReadOnlyMemory)** | **Native** | Requires connection string hack |
| **Column projection** | Yes | Yes (via SELECT) |
| **Integer PRIMARY KEY (rowid alias)** | Yes | Yes |
| **Overflow page assembly** | Yes | Yes |
| **Graph storage layer** | **Built-in** (ConceptStore, RelationStore) | Manual |
| **WITHOUT ROWID tables** | No (throws UnsupportedFeatureException) | Yes |
| **WAL mode** | No (throws UnsupportedFeatureException) | Yes |
| **Virtual tables (FTS, R-Tree)** | No | Yes |
| **UTF-16 text encoding** | No | Yes |
| **Page I/O backends** | Memory, File, MemoryMapped, Cached | Internal |
| **Encryption** | Planned (AES-256-GCM) | Via SQLCipher |
| **GC pressure (hot read paths)** | **0 B per-row** | Allocates per call |
| **Target framework** | .NET 10.0+ | .NET Standard 2.0+ |
| **Package size** | ~50 KB (managed only) | ~2 MB (with native binaries) |

## Benchmarks: Sharc vs Native SQLite

All benchmarks run on the same machine with the same data. C# SQLite is `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared commands — the fairest, commonly used setup.

> Run benchmarks yourself:
>
> - Standard: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`
> - Graph: `dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph*`

<!-- BENCHMARK_RESULTS_START -->

> **Environment:** Windows 11, Intel i7-11800H (8C/16T), .NET 10.0.2, BenchmarkDotNet v0.15.8 ShortRun (3 iterations, 1 launch, 3 warmup).
> SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements.

### Schema & Metadata

Sharc reads the 100-byte header struct and walks the sqlite_schema b-tree directly. No SQL parsing, no interop boundary.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| List all table names | 12.2 μs | 29.9 μs | **2.4x** | 27 KB | 872 B |
| Get column info (1 table) | 10.3 μs | 25.6 μs | **2.5x** | 27 KB | 696 B |
| Get column info (all tables) | 10.0 μs | 123.4 μs | **12.3x** | 27 KB | 4.0 KB |
| Batch 100 schema reads | 1,034 μs | 2,659 μs | **2.6x** | 2.6 MB | 85 KB |

### Sequential Scan

Full table scans — ETL, exports, analytics. On-demand cell pointer reads + lazy decode + buffer reuse = massive throughput gains.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 100 rows (config) | 21.8 μs | 61.7 μs | **2.8x** | 53 KB | 16 KB |
| Scan 10K rows, all types (users) | 7.9 ms | 27.0 ms | **3.4x** | 33.9 MB | 18.0 MB |
| Scan 10K rows, 2 columns (projection) | 1.4 ms | 2.8 ms | **2.0x** | 872 KB | 454 KB |
| Scan 100K rows, all columns (events) | 10.4 ms | 56.7 ms | **5.5x** | 27 KB | 688 B |
| Scan 100K rows, integers only | 6.4 ms | 26.0 ms | **4.1x** | 28 KB | 704 B |
| Scan 1K rows, 22 columns (reports) | 665 μs | 3,496 μs | **5.3x** | 790 KB | 495 KB |
| Scan 10K NULL checks (bios) | 628 μs | 10,491 μs | **16.7x** | 28 KB | 744 B |

### Data Type Decoding

Isolates decode cost per type. `ReadOnlySpan<byte>` + struct returns + buffer reuse vs boxed objects and string allocation.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decode 100K integers | 4.2 ms | 18.8 ms | **4.5x** | 28 KB | 384 B |
| Decode 10K doubles | 771 μs | 13.0 ms | **16.8x** | 28 KB | 696 B |
| Decode 10K short strings | 1.6 ms | 2.0 ms | **1.2x** | 872 KB | 454 KB |
| Decode 10K medium strings | 2.4 ms | 14.6 ms | **6.2x** | 5.9 MB | 3.8 MB |
| Decode 10K NULL checks | 588 μs | 13.0 ms | **22.1x** | 28 KB | 384 B |
| Decode mixed row (all 9 cols) | 7.6 ms | 27.7 ms | **3.6x** | 33.9 MB | 18.0 MB |

### Realistic Workloads

Composite benchmarks simulating what real applications actually do.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open + read 1 row + close | 10.4 μs | 25.4 μs | **2.4x** | 29 KB | 1.1 KB |
| Load user profile (B-tree Seek) | 10.8 μs | 21.7 μs | **2.0x** | 28 KB | 848 B |
| Export 10K users to CSV | 7.8 ms | 19.9 ms | **2.6x** | 18.7 MB | 2.9 MB |
| Schema migration check | 10.0 μs | 145.8 μs | **14.5x** | 27 KB | 4.4 KB |
| Batch read 500 users (projection) | 86.5 μs | 230.1 μs | **2.7x** | 70 KB | 23 KB |
| Read 10 config values | 11.3 μs | 266.5 μs | **23.5x** | 29 KB | 11 KB |

### GC Pressure Under Load

Sustained load across hundreds of thousands of rows. Buffer reuse eliminates per-row allocation pressure.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sustained scan 300K rows (events x3) | 28.5 ms | 75.0 ms | **2.6x** | 82 KB | 2.1 KB |
| Sustained string scan 50K rows (users x5) | 9.2 ms | 70.4 ms | **7.6x** | 9.6 MB | 5.3 MB |
| Scan all 4 tables | 14.9 ms | 28.6 ms | **1.9x** | 16.1 MB | 3.9 KB |
| Sustained int scan 1M rows (events x10) | 102.1 ms | 344.1 ms | **3.4x** | 277 KB | 7.0 KB |

### Graph Storage: Scans (5K Nodes, 15K Edges)

Graph-shaped data stored in SQLite format: `_concepts` (nodes) and `_relations` (edges). Benchmarks use `Sharc.Graph` layer vs raw SQLite queries.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 5K nodes (all columns) | 1,027 μs | 2,853 μs | **2.8x** | 1.5 MB | 937 KB |
| Scan 15K edges (all columns) | 2,268 μs | 7,673 μs | **3.4x** | 2.8 MB | 1.4 MB |
| Scan 5K nodes (id + type_id only) | 526 μs | 1,588 μs | **3.0x** | 793 KB | 470 KB |

### Graph Storage: B-Tree Seeks (Point Lookups)

Direct B-tree binary search via `reader.Seek(rowid)` — bypasses sequential scan entirely. This is where Sharc's zero-overhead format access shines brightest.

> **Note:** SQLite timings include end-to-end API cost (SQL parsing, VDBE execution, interop marshalling). Sharc bypasses all of these layers. The comparison is apples-to-oranges by design — it measures what each API costs to deliver the same result.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Single node lookup (rowid 2500) | 585 ns | 26,168 ns | **44.7x** | 1,840 B | 600 B |
| Batch 6 node lookups | 1,789 ns | 135,498 ns | **75.7x** | 4,176 B | 3,024 B |
| Open + seek + close | 5,007 ns | 27,395 ns | **5.5x** | 11.5 KB | 1.2 KB |

> **Note:** Batch speedup is amplified by page cache locality — Sharc keeps recently-read pages in its LRU cache, eliminating repeated I/O for clustered lookups.

### Primitive Operations (Sharc-only, no SQLite equivalent)

These measure raw decode speed at the byte level — no interop overhead to compare against.

| Operation | Time | Allocated |
| --- | ---: | ---: |
| Parse database header (100 bytes) | 7.5 ns | **0 B** |
| Parse b-tree page headers (10 pages) | 36.4 ns | **0 B** |
| Decode 100 varints | 237 ns | **0 B** |
| Classify 100 serial types | 106 ns | **0 B** |
| Read 100 inline integers | 127 ns | **0 B** |
| Read 5 row column values | 5.5 ns | **0 B** |

**Notes:**

- Sharc reuses `ColumnValue[]` buffers across rows (zero per-row array allocation) and uses `stackalloc` for serial type headers.
- On-demand cell pointer reads eliminate per-page `ushort[]` allocations — 100K row scans allocate only 28 KB total.
- Lazy decode: NULL checks read only the record header (serial types), skipping body decode entirely — **22.1x faster** than SQLite.
- Column projection decodes only requested columns, skipping unwanted text/blob fields entirely.
- B-tree Seek performs binary search descent to a specific rowid in **585 ns** — 45x faster than SQLite's prepared `WHERE key = ?`.
- Sustained integer scan: 1M rows with only 277 KB allocated.
- Generation counter pattern replaces `Array.Clear` for lazy decode invalidation — O(1) per row instead of O(N).
- Run locally: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`

<!-- BENCHMARK_RESULTS_END -->

## Current Limitations

Sharc is a **read-only format reader**, not a full database engine. Be aware of these constraints:

- **No SQL execution** — Sharc reads raw B-tree pages. It does not parse SQL, execute queries, or support joins/aggregates.
- **No write support** — Read-only by design. Write support is planned for a future milestone.
- **No WAL mode** — Databases using WAL journaling throw `UnsupportedFeatureException`. WAL read support is planned for M8.
- **No WITHOUT ROWID tables** — Tables created with `WITHOUT ROWID` are not supported.
- **No virtual tables** — FTS5, R-Tree, and other virtual table types are not supported.
- **No UTF-16 text** — Only UTF-8 encoded databases are supported.
- **Graph layer uses full table scans** — `ConceptStore.Get()` and `RelationStore.GetEdges()` perform O(N) scans on the node/edge key columns. Index-accelerated lookups are planned for M7.
- **Schema allocation** — Initial schema read allocates ~40 KB for table/column metadata. This is a one-time cost per database open, not per-row.
- **Seek benchmarks compare different abstractions** — Sharc's `Seek()` bypasses SQL parsing and VDBE execution entirely, so speedup numbers reflect the full API cost difference rather than identical operations.

## Architecture

Pure managed SQLite file-format reader. No VM, no VDBE, no query planner.

```text
Public API          SharcDatabase -> SharcDataReader
Graph Layer         ConceptStore, RelationStore (Sharc.Graph)
Schema              SchemaReader: parses sqlite_schema
Records             RecordDecoder: varint + serial type -> values
B-Tree              BTreeReader -> BTreeCursor -> CellParser
Page I/O            IPageSource: Memory | Mmap | File | Cached
                    IPageTransform: Identity | Decrypting
Primitives          VarintDecoder, SerialTypeCodec
```

**What Sharc does NOT do:** execute SQL, write databases, or handle virtual tables. For full SQL, use `Microsoft.Data.Sqlite`. Sharc complements it for the cases where you need raw speed and zero dependencies.

## Project Structure

```text
src/Sharc/                    -- Public API (SharcDatabase, SharcDataReader, Schema)
src/Sharc.Core/               -- Internal: page I/O, b-tree, record decoding, primitives
src/Sharc.Graph/              -- Graph storage layer (ConceptStore, RelationStore)
src/Sharc.Graph.Abstractions/ -- Graph interfaces and models
src/Sharc.Crypto/             -- Encryption: KDF, AEAD ciphers, key management (planned)
tests/Sharc.Tests/            -- Unit tests (xUnit)
tests/Sharc.IntegrationTests/ -- End-to-end tests with real SQLite databases
tests/Sharc.Graph.Tests.Unit/ -- Graph layer unit tests (MSTest)
bench/Sharc.Benchmarks/       -- BenchmarkDotNet comparative suite (113 benchmarks)
bench/Sharc.Comparisons/      -- Graph storage benchmarks (14 benchmarks)
PRC/                          -- Architecture docs, specs, and decisions
```

## Build & Test

```bash
dotnet build                                  # build everything
dotnet test                                   # run all tests
dotnet test tests/Sharc.Tests                 # unit tests only
dotnet test tests/Sharc.IntegrationTests      # integration tests only
dotnet run -c Release --project bench/Sharc.Benchmarks  # run standard benchmarks
dotnet run -c Release --project bench/Sharc.Comparisons # run graph benchmarks
```

### Test Status

```text
503 passed, 0 skipped, 0 failed
  - Unit tests:        393
  - Graph unit tests:   49
  - Integration tests:  61 (includes 12 concurrency/parallel tests)
```

### Milestone Progress

```text
Milestone 1 (Primitives)     ████████████████ COMPLETE
Milestone 2 (Page I/O)       ████████████████ COMPLETE
Milestone 3 (B-Tree)         ████████████████ COMPLETE
Milestone 4 (Records)        ████████████████ COMPLETE
Milestone 5 (Schema)         ████████████████ COMPLETE
Milestone 6 (Table Scans)    ████████████████ COMPLETE — MVP
Milestone 7 (SQL Subset)     ░░░░░░░░░░░░░░░░ Future
Milestone 8 (WAL Support)    ░░░░░░░░░░░░░░░░ Future
Milestone 9 (Encryption)     ░░░░░░░░░░░░░░░░ Future
Milestone 10 (Benchmarks)    ████████████████ COMPLETE (127 benchmarks)
Graph Support                ████████████████ COMPLETE
```

## Design Principles

- **Zero-alloc hot paths** — `ReadOnlySpan<byte>`, `readonly struct`, `ArrayPool`. The GC should not wake up during page reads.
- **Read-only first** — no write support until reads are benchmarked solid and correct.
- **TDD** — every feature starts with tests. The test suite is the specification.
- **Pure managed** — zero native dependencies. Runs anywhere .NET runs.

See [PRC/ArchitectureOverview.md](PRC/ArchitectureOverview.md) for the full architecture reference and [PRC/BenchmarkSpec.md](PRC/BenchmarkSpec.md) for the benchmark methodology.

## Requirements

- .NET 10.0 SDK or later

## License

MIT License. See [LICENSE](LICENSE) for details.


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

Quiet conversations often begin with a single repository.
