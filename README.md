# Sharc

[![CI](https://github.com/user/sharc/actions/workflows/ci.yml/badge.svg)](https://github.com/user/sharc/actions/workflows/ci.yml)

**Read SQLite files at memory speed. Pure C#. Zero native dependencies. Zero GC pressure.**

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
| **Sequential table scan** | **2.4x-16.2x faster** | Baseline |
| **B-tree point lookup (Seek)** | **37x faster** | Baseline |
| **Schema introspection** | **1.8x-9.0x faster** | Baseline |
| **Config / metadata reads** | **17.5x faster** | Baseline |
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
| **GC pressure (primitive ops)** | **0 B allocated** | Allocates per call |
| **Target framework** | .NET 10.0+ | .NET Standard 2.0+ |
| **Package size** | ~50 KB (managed only) | ~2 MB (with native binaries) |

## Benchmarks: Sharc vs Native SQLite

All benchmarks run on the same machine with the same data. SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared commands — the fairest possible setup.

> Run benchmarks yourself:
>
> - Standard: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`
> - Graph: `dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph*`

<!-- BENCHMARK_RESULTS_START -->

> **Environment:** Windows 11, Intel i7-11800H (8C/16T), .NET 10.0.2, BenchmarkDotNet v0.15.8 ShortRun.
> SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements.

### Schema & Metadata

Sharc reads the 100-byte header struct and walks the sqlite_schema b-tree directly. No SQL parsing, no interop boundary.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| List all table names | 13.2 μs | 26.9 μs | **2.0x** | 40 KB | 872 B |
| Get column info (1 table) | 13.2 μs | 23.4 μs | **1.8x** | 40 KB | 696 B |
| Get column info (all tables) | 14.5 μs | 130.3 μs | **9.0x** | 40 KB | 4.0 KB |
| Batch 100 schema reads | 1,472 μs | 2,672 μs | **1.8x** | 3.9 MB | 85 KB |

### Sequential Scan

Full table scans — ETL, exports, analytics. On-demand cell pointer reads + lazy decode + buffer reuse = massive throughput gains.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 100 rows (config) | 29.0 μs | 69.1 μs | **2.4x** | 65 KB | 16 KB |
| Scan 10K rows, all types (users) | 8.8 ms | 28.4 ms | **3.2x** | 33.9 MB | 18.0 MB |
| Scan 10K rows, 2 columns (projection) | 1.7 ms | 2.9 ms | **1.7x** | 885 KB | 454 KB |
| Scan 100K rows, all columns (events) | 11.7 ms | 57.8 ms | **4.9x** | 40 KB | 688 B |
| Scan 100K rows, integers only | 7.0 ms | 29.1 ms | **4.2x** | 41 KB | 704 B |
| Scan 1K rows, 22 columns (reports) | 734 μs | 3,564 μs | **4.9x** | 803 KB | 495 KB |
| Scan 10K NULL checks (bios) | 692 μs | 11,213 μs | **16.2x** | 41 KB | 744 B |

### Data Type Decoding

Isolates decode cost per type. `ReadOnlySpan<byte>` + struct returns + buffer reuse vs boxed objects and string allocation.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decode 100K integers | 4.3 ms | 19.0 ms | **4.4x** | 41 KB | 384 B |
| Decode 10K doubles | 898 μs | 13.4 ms | **14.9x** | 41 KB | 696 B |
| Decode 10K short strings | 1.2 ms | 2.0 ms | **1.6x** | 885 KB | 454 KB |
| Decode 10K medium strings | 2.3 ms | 14.9 ms | **6.5x** | 5.9 MB | 3.8 MB |
| Decode 10K NULL checks | 682 μs | 13.1 ms | **19.2x** | 41 KB | 384 B |
| Decode mixed row (all 9 cols) | 7.9 ms | 28.7 ms | **3.6x** | 33.9 MB | 18.0 MB |

### Realistic Workloads

Composite benchmarks simulating what real applications actually do.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open + read 1 row + close | 13.7 μs | 25.4 μs | **1.9x** | 43 KB | 1.1 KB |
| Export 10K users to CSV | 7.6 ms | 20.5 ms | **2.7x** | 18.7 MB | 2.9 MB |
| Schema migration check | 13.2 μs | 146.7 μs | **11.1x** | 40 KB | 4.4 KB |
| Batch read 500 users | 217 μs | 240 μs | **1.1x** | 972 KB | 23 KB |
| Read 10 config values | 15.0 μs | 262.6 μs | **17.5x** | 42 KB | 11 KB |

### GC Pressure Under Load

Sustained load across hundreds of thousands of rows. Buffer reuse eliminates per-row allocation pressure.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sustained scan 300K rows (events x3) | 29.5 ms | 79.8 ms | **2.7x** | 121 KB | 2.1 KB |
| Sustained string scan 50K rows (users x5) | 9.9 ms | 76.4 ms | **7.7x** | 9.7 MB | 5.3 MB |
| Scan all 4 tables | 15.4 ms | 27.2 ms | **1.8x** | 16.1 MB | 3.9 KB |
| Sustained int scan 1M rows (events x10) | 112.9 ms | 355.4 ms | **3.1x** | 407 KB | 7.0 KB |

### Graph Storage: Scans (5K Nodes, 15K Edges)

Graph-shaped data stored in SQLite format: `_concepts` (nodes) and `_relations` (edges). Benchmarks use `Sharc.Graph` layer vs raw SQLite queries.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 5K nodes (all columns) | 1,005 μs | 2,639 μs | **2.6x** | 1.5 MB | 937 KB |
| Scan 15K edges (all columns) | 2,143 μs | 6,967 μs | **3.3x** | 2.8 MB | 1.4 MB |
| Scan 5K nodes (id + type_id only) | 489 μs | 1,507 μs | **3.1x** | 798 KB | 470 KB |

### Graph Storage: B-Tree Seeks (Point Lookups)

Direct B-tree binary search via `cursor.Seek(rowid)` — bypasses schema and sequential scan entirely. This is where Sharc's zero-overhead format access shines brightest.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Single node lookup (rowid 2500) | 561 ns | 21,117 ns | **37.6x** | 1,840 B | 600 B |
| Open + scan to row + close | 373 μs | 25.4 μs | SQLite 14.7x | 329 KB | 1.2 KB |

### Primitive Operations (Sharc-only, no SQLite equivalent)

These measure raw decode speed at the byte level — no interop overhead to compare against.

| Operation | Time | Allocated |
| --- | ---: | ---: |
| Parse database header (100 bytes) | 8.6 ns | **0 B** |
| Validate magic string | 0.3 ns | **0 B** |
| Parse b-tree page header | 3.2 ns | **0 B** |
| Decode 100 varints | 261 ns | **0 B** |
| Classify 100 serial types | 101 ns | **0 B** |
| Read 5 inline column values | 5.7 ns | **0 B** |

**Notes:**

- Sharc reuses `ColumnValue[]` buffers across rows (zero per-row array allocation) and uses `stackalloc` for serial type headers.
- On-demand cell pointer reads eliminate per-page `ushort[]` allocations — 100K row scans allocate only 41 KB total.
- Lazy decode: NULL checks read only the record header (serial types), skipping body decode entirely — **19x faster** than SQLite.
- Column projection decodes only requested columns, skipping unwanted text/blob fields entirely.
- B-tree Seek performs binary search descent to a specific rowid in **561 ns** — 37x faster than SQLite's prepared `WHERE key = ?`.
- Sustained integer scan: 1M rows with only 407 KB allocated.
- The "Open + scan to row + close" benchmark is unfavorable because Sharc's public reader API does sequential scan; SQLite uses indexed `WHERE` lookup. Direct cursor Seek (37x faster) is available via the internal API.
- Run locally: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`

<!-- BENCHMARK_RESULTS_END -->

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
489 passed, 0 skipped, 0 failed
  - Unit tests:        393
  - Graph unit tests:   42
  - Integration tests:  54 (includes 12 concurrency/parallel tests)
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
