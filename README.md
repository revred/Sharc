# 🦈 Sharc

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
| **SQL parsing / VM / query planner** | No — reads raw B-tree pages | Yes (full VDBE) |
| **WHERE filtering** | **Yes** — 6 operators, all types, scan-based | Yes (via SQL) |
| **JOIN / GROUP BY / aggregates** | No — consumer's responsibility | Yes |
| **ORDER BY** | No — rows returned in rowid order | Yes |
| **Write / INSERT / UPDATE / DELETE** | No | Yes |
| **Native dependencies** | **None** | Requires `e_sqlite3` native binary |
| **Sequential table scan** | **2.1x-17.9x faster** | Baseline |
| **B-tree point lookup (Seek)** | **45x faster** | Baseline |
| **Schema introspection** | **2.1x-11.3x faster** | Baseline |
| **Config / metadata reads** | **21x faster** | Baseline |
| **Concurrent parallel readers** | **Thread-safe** (12 tests, up to 16 threads) | Thread-safe |
| **In-memory buffer (ReadOnlyMemory)** | **Native** | Requires connection string hack |
| **Column projection** | Yes | Yes (via SELECT) |
| **Integer PRIMARY KEY (rowid alias)** | Yes | Yes |
| **Overflow page assembly** | Yes | Yes |
| **Graph storage layer** | **Built-in** (ConceptStore, RelationStore) | Manual |
| **WITHOUT ROWID tables** | **Yes** | Yes |
| **WAL mode** | **Yes** (read-only) | Yes |
| **Virtual tables (FTS, R-Tree)** | No | Yes |
| **UTF-16 text encoding** | No | Yes |
| **Page I/O backends** | Memory, File, MemoryMapped, Cached | Internal |
| **Encryption** | **AES-256-GCM** (page-level, Argon2id KDF) | Via SQLCipher |
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
| List all table names | 12.2 μs | 27.8 μs | **2.3x** | 28 KB | 872 B |
| Get column info (1 table) | 11.4 μs | 24.0 μs | **2.1x** | 28 KB | 696 B |
| Get column info (all tables) | 11.3 μs | 127.9 μs | **11.3x** | 28 KB | 4.0 KB |
| Batch 100 schema reads | 1,181 μs | 2,721 μs | **2.3x** | 2.7 MB | 85 KB |

### Sequential Scan

Full table scans — ETL, exports, analytics. On-demand cell pointer reads + lazy decode + buffer reuse = massive throughput gains.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 100 rows (config) | 25.7 μs | 61.1 μs | **2.4x** | 54 KB | 16 KB |
| Scan 10K rows, all types (users) | 11.3 ms | 28.1 ms | **2.5x** | 33.9 MB | 18.0 MB |
| Scan 10K rows, 2 columns (projection) | 1.3 ms | 2.9 ms | **2.1x** | 873 KB | 454 KB |
| Scan 100K rows, all columns (events) | 11.4 ms | 56.2 ms | **4.9x** | 29 KB | 688 B |
| Scan 100K rows, integers only | 7.8 ms | 25.3 ms | **3.3x** | 29 KB | 704 B |
| Scan 1K rows, 22 columns (reports) | 697 μs | 3,639 μs | **5.2x** | 791 KB | 495 KB |
| Scan 10K NULL checks (bios) | 647 μs | 11,562 μs | **17.9x** | 29 KB | 744 B |

### Data Type Decoding

Isolates decode cost per type. `ReadOnlySpan<byte>` + struct returns + buffer reuse vs boxed objects and string allocation.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decode 100K integers | 4.2 ms | 17.0 ms | **4.1x** | 29 KB | 384 B |
| Decode 10K doubles | 952 μs | 12.3 ms | **12.9x** | 29 KB | 696 B |
| Decode 10K short strings | 1.1 ms | 1.9 ms | **1.7x** | 873 KB | 454 KB |
| Decode 10K medium strings | 1.8 ms | 14.1 ms | **7.9x** | 5.9 MB | 3.8 MB |
| Decode 10K NULL checks | 611 μs | 12.1 ms | **19.8x** | 29 KB | 384 B |
| Decode mixed row (all 9 cols) | 7.6 ms | 28.3 ms | **3.7x** | 33.9 MB | 18.0 MB |

### Realistic Workloads

Composite benchmarks simulating what real applications actually do.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open + read 1 row + close | 12.1 μs | 26.2 μs | **2.2x** | 30 KB | 1.1 KB |
| Load user profile (B-tree Seek) | 12.2 μs | 22.1 μs | **1.8x** | 30 KB | 848 B |
| Export 10K users to CSV | 8.1 ms | 20.7 ms | **2.6x** | 18.7 MB | 2.9 MB |
| Schema migration check | 11.5 μs | 149.9 μs | **13.0x** | 28 KB | 4.4 KB |
| Batch read 500 users (projection) | 89.7 μs | 245.7 μs | **2.7x** | 72 KB | 23 KB |
| Read 10 config values | 12.8 μs | 270.5 μs | **21.1x** | 31 KB | 11 KB |

### GC Pressure Under Load

Sustained load across hundreds of thousands of rows. Buffer reuse eliminates per-row allocation pressure.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sustained scan 300K rows (events x3) | 29.2 ms | 75.1 ms | **2.6x** | 87 KB | 2.1 KB |
| Sustained string scan 50K rows (users x5) | 9.1 ms | 74.1 ms | **8.2x** | 9.6 MB | 5.3 MB |
| Scan all 4 tables | 14.8 ms | 28.4 ms | **1.9x** | 16.1 MB | 3.9 KB |
| Sustained int scan 1M rows (events x10) | 124.9 ms | 354.5 ms | **2.8x** | 292 KB | 7.0 KB |

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
| Parse database header (100 bytes) | 8.5 ns | **0 B** |
| Parse b-tree page headers (10 pages) | 35.1 ns | **0 B** |
| Decode 100 varints | 231 ns | **0 B** |
| Classify 100 serial types | 102 ns | **0 B** |
| Read 100 inline integers | 125 ns | **0 B** |
| Read 5 row column values | 5.5 ns | **0 B** |

**Notes:**

- Sharc reuses `ColumnValue[]` buffers across rows (zero per-row array allocation) and uses `stackalloc` for serial type headers.
- On-demand cell pointer reads eliminate per-page `ushort[]` allocations — 100K row scans allocate only 28 KB total.
- Lazy decode: NULL checks read only the record header (serial types), skipping body decode entirely — **19.8x faster** than SQLite.
- Column projection decodes only requested columns, skipping unwanted text/blob fields entirely.
- B-tree Seek performs binary search descent to a specific rowid in **585 ns** — 45x faster than SQLite's prepared `WHERE key = ?`.
- Sustained integer scan: 1M rows with only 292 KB allocated.
- Generation counter pattern replaces `Array.Clear` for lazy decode invalidation — O(1) per row instead of O(N).
- Run locally: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`

<!-- BENCHMARK_RESULTS_END -->

## Current Limitations

Sharc is a **read-only format reader**, not a full database engine. Be aware of these constraints:

- **No SQL execution** — Sharc reads raw B-tree pages. It does not parse SQL, execute queries, or support joins/aggregates. Simple WHERE filtering is available via `SharcFilter`.
- **No write support** — Read-only by design. Write support is planned for a future milestone.
- **No virtual tables** — FTS5, R-Tree, and other virtual table types are not supported.
- **No UTF-16 text** — Only UTF-8 encoded databases are supported.
- **Schema allocation** — Initial schema read allocates ~28 KB for table/column metadata. This is a one-time cost per database open, not per-row.
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
src/Sharc.Crypto/             -- Encryption: AES-256-GCM, Argon2id KDF, key management
tests/Sharc.Tests/            -- Unit tests (xUnit)
tests/Sharc.IntegrationTests/ -- End-to-end tests with real SQLite databases
tests/Sharc.Graph.Tests.Unit/ -- Graph layer unit tests (MSTest)
tests/Sharc.Context.Tests/    -- MCP context query tool tests
tests/Sharc.Index.Tests/      -- GCD indexer tests
bench/Sharc.Benchmarks/       -- BenchmarkDotNet comparative suite (113 benchmarks)
bench/Sharc.Comparisons/      -- Graph storage benchmarks (14 benchmarks)
tools/Sharc.Context/          -- MCP server: AI agent query tools
tools/Sharc.Index/            -- CLI: builds GitHub Context Database from git history
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
696 passed, 0 skipped, 0 failed
  - Unit tests:        393 (core) + 42 (crypto) + 21 (filter) + 7 (WITHOUT ROWID adapter)
  - Graph unit tests:   49
  - Integration tests:  61 (includes encryption, filtering, WITHOUT ROWID)
  - Context tests:      14 (MCP query tools)
  - Index tests:        22 (GCD schema, git log parser, commit writer)
  - Concurrency:        12 parallel reader tests (up to 16 threads)
```

### Milestone Progress

```text
Milestone 1 (Primitives)     ████████████████ COMPLETE
Milestone 2 (Page I/O)       ████████████████ COMPLETE
Milestone 3 (B-Tree)         ████████████████ COMPLETE
Milestone 4 (Records)        ████████████████ COMPLETE
Milestone 5 (Schema)         ████████████████ COMPLETE
Milestone 6 (Table Scans)    ████████████████ COMPLETE — MVP
Milestone 7 (Index + Filter) ████████████████ COMPLETE (Seek, WHERE, WITHOUT ROWID)
Milestone 8 (WAL Support)    ████████████████ COMPLETE (frame-by-frame merge)
Milestone 9 (Encryption)     ████████████████ COMPLETE (AES-256-GCM, Argon2id)
Milestone 10 (Benchmarks)    ████████████████ COMPLETE (127 benchmarks)
Graph Support                ████████████████ COMPLETE
MCP Context Tools            ████████████████ COMPLETE (4 query tools)
sharc-index CLI              ████████████████ COMPLETE (scaffold)
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
