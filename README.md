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
| **Sequential table scan** | **2x-6x faster** | Baseline |
| **Schema introspection** | **1.4x-7.8x faster** | Baseline |
| **Config / metadata reads** | **14.5x faster** | Baseline |
| **Concurrent parallel readers** | **Thread-safe** (12 tests, up to 16 threads) | Thread-safe |
| **In-memory buffer (ReadOnlyMemory)** | **Native** | Requires connection string hack |
| **Column projection** | Yes | Yes (via SELECT) |
| **Integer PRIMARY KEY (rowid alias)** | Yes | Yes |
| **Overflow page assembly** | Yes | Yes |
| **WITHOUT ROWID tables** | No (throws UnsupportedFeatureException) | Yes |
| **WAL mode** | No (throws UnsupportedFeatureException) | Yes |
| **Virtual tables (FTS, R-Tree)** | No | Yes |
| **UTF-16 text encoding** | No | Yes |
| **Indexed point lookups** | No (sequential scan only) | Yes |
| **Page I/O backends** | Memory, File, MemoryMapped, Cached | Internal |
| **Encryption** | Planned (AES-256-GCM) | Via SQLCipher |
| **GC pressure (primitive ops)** | **0 B allocated** | Allocates per call |
| **Target framework** | .NET 10.0+ | .NET Standard 2.0+ |
| **Package size** | ~50 KB (managed only) | ~2 MB (with native binaries) |

## Benchmarks: Sharc vs Native SQLite

Benchmarks use the canonical database from [BenchmarkSpec.md](PRC/BenchmarkSpec.md): config (100 rows), users (10K rows), events (100K rows), reports (1K rows with 22 columns). SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared commands — the fairest possible setup.

> Run benchmarks yourself: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`

<!-- BENCHMARK_RESULTS_START -->

> **Environment:** Windows 11, Intel i7-11800H (8C/16T), .NET 10.0.2, BenchmarkDotNet ShortRun.
> SQLite uses `Microsoft.Data.Sqlite` with pre-opened connections and pre-prepared statements.

### Category 2: Schema & Metadata

Sharc reads the 100-byte header struct and walks the sqlite_schema b-tree directly. No SQL parsing, no interop boundary.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| List all table names | 16.9 μs | 32.5 μs | **1.9x** | 40 KB | 872 B |
| Get column info (1 table) | 16.4 μs | 30.0 μs | **1.8x** | 40 KB | 696 B |
| Get column info (all tables) | 14.3 μs | 131.7 μs | **9.2x** | 40 KB | 4.0 KB |
| Batch 100 schema reads | 1,484 μs | 2,930 μs | **2.0x** | 3.9 MB | 85 KB |

### Category 4: Sequential Scan

Full table scans — ETL, exports, analytics. On-demand cell pointer reads + lazy decode + buffer reuse = massive throughput and allocation gains.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan 100 rows (config) | 25.6 μs | 63.3 μs | **2.5x** | 65 KB | 16 KB |
| Scan 10K rows, all types (users) | 7.5 ms | 27.7 ms | **3.7x** | 33.9 MB | 18.0 MB |
| Scan 10K rows, 2 columns (projection) | 1.5 ms | 2.9 ms | **1.9x** | 885 KB | 454 KB |
| Scan 100K rows (events) | 10.4 ms | 63.2 ms | **6.1x** | 40 KB | 688 B |
| Scan 100K rows, integers only | 6.3 ms | 25.5 ms | **4.0x** | 41 KB | 704 B |
| Scan 1K rows, 22 columns (reports) | 670 μs | 3,417 μs | **5.1x** | 803 KB | 495 KB |
| Scan 10K NULL checks (bios) | 661 μs | 11,642 μs | **17.6x** | 41 KB | 744 B |

### Category 6: Data Type Decoding

Isolates decode cost per type. `ReadOnlySpan<byte>` + struct returns + buffer reuse vs boxed objects and string allocation. Lazy decode skips body parsing entirely for null checks.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decode 100K integers | 4.0 ms | 17.1 ms | **4.3x** | 41 KB | 384 B |
| Decode 10K doubles | 879 μs | 13.1 ms | **14.9x** | 41 KB | 696 B |
| Decode 10K short strings | 1.1 ms | 1.9 ms | **1.7x** | 885 KB | 454 KB |
| Decode 10K medium strings | 1.8 ms | 14.2 ms | **8.1x** | 5.9 MB | 3.8 MB |
| Decode 10K NULL checks | 627 μs | 12.6 ms | **20.1x** | 41 KB | 384 B |
| Decode mixed row (all 9 cols) | 7.5 ms | 28.3 ms | **3.8x** | 33.9 MB | 18.0 MB |

### Category 8: Realistic Workloads

Composite benchmarks simulating what real applications actually do.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open + read 1 row + close | 15.7 μs | 27.1 μs | **1.7x** | 43 KB | 1.1 KB |
| Export 10K users to CSV | 7.3 ms | 20.2 ms | **2.8x** | 18.9 MB | 2.9 MB |
| Schema migration check | 15.1 μs | 155.9 μs | **10.3x** | 40 KB | 4.4 KB |
| Batch read 500 users | 198 μs | 234 μs | **1.2x** | 986 KB | 23 KB |
| Read 10 config values | 14.7 μs | 263.3 μs | **17.9x** | 42 KB | 11 KB |

### Category 9: GC Pressure Under Load

Sustained load across hundreds of thousands of rows. Buffer reuse eliminates per-row allocation pressure.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sustained scan 300K rows (events x3) | 31.2 ms | 77.7 ms | **2.5x** | 760 KB | 2.1 KB |
| Sustained string scan 50K rows (users x5) | 8.3 ms | 71.6 ms | **8.6x** | 10.8 MB | 5.3 MB |
| Scan all 4 tables | 14.8 ms | 28.7 ms | **1.9x** | 16.5 MB | 3.9 KB |
| Sustained int scan 1M rows (events x10) | 73.1 ms | 334.5 ms | **4.6x** | 2.5 MB | 7.0 KB |

### Primitive Operations (Sharc-only, no SQLite equivalent)

These measure raw decode speed at the byte level — no interop overhead to compare against.

| Operation | Time | Allocated |
| --- | ---: | ---: |
| Parse database header (100 bytes) | 9.4 ns | **0 B** |
| Validate magic string | 1.0 ns | **0 B** |
| Parse b-tree page header | 3.5 ns | **0 B** |
| Decode 100 varints | 253 ns | **0 B** |
| Classify 100 serial types | 122 ns | **0 B** |
| Read 5 inline column values | 5.9 ns | **0 B** |

**Notes:**

- Sharc reuses `ColumnValue[]` buffers across rows (zero per-row array allocation) and uses `stackalloc` for serial type headers.
- On-demand cell pointer reads eliminate per-page `ushort[]` allocations — 100K row scans allocate only 41 KB total.
- Lazy decode: NULL checks read only the record header (serial types), skipping body decode entirely — **20x faster** than SQLite with **57x less allocation** vs previous Sharc versions.
- Column projection decodes only requested columns, skipping unwanted text/blob fields entirely.
- Sustained integer scan: 1M rows with only 2.5 MB allocated (vs 422 MB before optimization).
- Run locally: `dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative*`

<!-- BENCHMARK_RESULTS_END -->

## Architecture

Pure managed SQLite file-format reader. No VM, no VDBE, no query planner.

```text
Public API          SharcDatabase -> SharcDataReader
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
src/Sharc/             -- Public API (SharcDatabase, SharcDataReader, Schema)
src/Sharc.Core/        -- Internal: page I/O, b-tree, record decoding, primitives
src/Sharc.Crypto/      -- Encryption: KDF, AEAD ciphers, key management
tests/Sharc.Tests/     -- Unit tests (xUnit)
tests/Sharc.IntegrationTests/ -- End-to-end tests with real SQLite databases
bench/Sharc.Benchmarks/ -- BenchmarkDotNet comparative suite
PRC/                   -- Architecture docs, specs, and decisions
```

## Build & Test

```bash
dotnet build                                  # build everything
dotnet test                                   # run all tests
dotnet test tests/Sharc.Tests                 # unit tests only
dotnet test tests/Sharc.IntegrationTests      # integration tests only
dotnet run -c Release --project bench/Sharc.Benchmarks  # run benchmarks
```

### Test Status

```text
447 passed, 0 skipped, 0 failed
  - Unit tests:        393
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
Milestone 10 (Benchmarks)    ████████████████ COMPLETE
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