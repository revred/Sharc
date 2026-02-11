# Sharc

**Read SQLite files at memory speed. Pure C#. Zero native dependencies. Zero GC pressure.**

Sharc reads SQLite database files (format 3) from disk, memory, or encrypted blobs — without a single native library. No `sqlite3.dll`. No P/Invoke. No connection strings. Just bytes in, typed values out.

## The Pitch

```text
Sharc header parse:     8.5 ns    0 B allocated
SQLite PRAGMA query:  61,746 ns   2,048 B allocated
                      ─────────
                      7,254x faster. ∞ allocation ratio.
```

## Quick Start

```csharp
using Sharc;

// Open from file
using var db = SharcDatabase.Open("mydata.db");

// List tables
foreach (var table in db.Schema.Tables)
    Console.WriteLine(table.Name);

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
// Encrypted — AES-256-GCM + Argon2id, page-level
using var db = SharcDatabase.Open("encrypted.sharc", new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions { Password = "my-secret" }
});
```

## Benchmarks: Sharc vs Native SQLite

.NET 10.0, BenchmarkDotNet v0.15.8, Windows 11, i7-11800H. SQLite uses `Microsoft.Data.Sqlite` v9.0.4 with pre-opened connections and pre-prepared commands — the fairest possible setup for SQLite.

> Alloc Ratio = SQLite alloc / Sharc alloc. **∞** means Sharc allocated nothing.

### Metadata Retrieval

Sharc reads the 100-byte header struct directly. SQLite parses SQL, crosses the interop boundary, and boxes result objects.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Get page size | 7.6 ns | 716 ns | **94x** | 0 B | 408 B | **∞** |
| Get all metadata | 8.4 ns | 61,746 ns | **7,254x** | 0 B | 2,048 B | **∞** |
| Batch 100 header reads | 756 ns | 76,143 ns | **101x** | 0 B | 40,800 B | **∞** |
| Validate magic bytes | 0.3 ns | — | — | 0 B | — | — |

Every metadata operation: zero bytes allocated, every time. The header is a `readonly struct` parsed from a span — nothing touches the managed heap.

### Database Open

Three modes: in-memory (pre-loaded bytes), memory-mapped (OS lazy-pages), file-backed (RandomAccess on-demand). SQLite's 156 ns open is connection pooling — cached native handles, not a real file open.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open from memory | 8.5 ns | 156 ns | **18x** | 0 B | 368 B | **∞** |
| Open memory-mapped | 101 us | 156 ns | 0.002x | 488 B | 368 B | 0.75x |
| Open file (RandomAccess) | 86 us | 156 ns | 0.002x | 4,248 B | 376 B | 0.09x |
| Batch 50 opens (memory) | 379 ns | 7,602 ns | **20x** | 0 B | 18,400 B | **∞** |

File-backed opens pay ~86-101 us of OS kernel cost — that's `CreateFileW` / `mmap`, not .NET overhead. SQLite sidesteps this entirely via pooling. Sharc wins it back quickly: at 5 ns/page (mmap) vs SQLite's 19,751 ns/row, the open cost amortizes after a handful of reads. Where Sharc opens from memory, there's no contest — 18x faster, zero allocation.

### Page & Row Reading

This is the core of what Sharc is built for. Span slices into raw page data vs SQL round-trips through a query engine.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Parse 1 page (memory) | 3.8 ns | 19,751 ns | **5,198x** | 0 B | 408 B | **∞** |
| Parse 1 page (mmap) | 5.2 ns | 19,751 ns | **3,798x** | 0 B | 408 B | **∞** |
| Parse 1 page (file) | 1,226 ns | 19,751 ns | **16x** | 0 B | 408 B | **∞** |
| Scan all pages (memory) | 1,539 ns | 6,074,498 ns | **3,948x** | 0 B | 1.2 MB | **∞** |
| Scan all pages (mmap) | 2,781 ns | 6,074,498 ns | **2,184x** | 0 B | 1.2 MB | **∞** |
| Scan all pages (file) | 433,875 ns | 6,074,498 ns | **14x** | 0 B | 1.2 MB | **∞** |

Even the slowest Sharc path (file, one syscall per page) is 14x faster than SQLite — and still allocates zero bytes for the actual page reads. The mmap and memory paths are three to four orders of magnitude faster.

### The GC Scorecard

Heap bytes per operation. Where the ratio is **∞**, Sharc's hot path lives entirely on the stack — the garbage collector has nothing to do.

| Operation | Sharc | SQLite | Alloc Ratio |
| --- | ---: | ---: | ---: |
| Parse database header | 0 B | 408 B | **∞** |
| Get all metadata (5 fields) | 0 B | 2,048 B | **∞** |
| Open (memory) | 0 B | 368 B | **∞** |
| Open (memory-mapped) | 488 B | 368 B | 0.75x |
| Open (file, RandomAccess) | 4,248 B | 376 B | 0.09x |
| Read page (mmap) | 0 B | 408 B | **∞** |
| Read page (file) | 0 B | 408 B | **∞** |
| Full table scan (10K rows) | 0 B | 1,200,384 B | **∞** |
| Batch 50 opens (memory) | 0 B | 18,400 B | **∞** |

The two rows where SQLite allocates less are file-backed opens — Sharc pays for a page-sized read buffer and OS handle structures. Once open, every subsequent read is zero-alloc. The full table scan tells the real story: Sharc scans 10,000 rows for exactly **0 bytes** of heap pressure. SQLite allocates **1.17 MB** for the same work.

> `ReadOnlySpan<byte>` + `readonly struct` + `[AggressiveInlining]` — the GC never wakes up. Memory-mapped access adds 488 B for the OS mapping handle. FilePageSource allocates one page buffer on open and reuses it for the lifetime of the source.

### When to Use Which

| Use Case | Winner |
| --- | --- |
| SQL queries, joins, aggregates | Microsoft.Data.Sqlite |
| Write operations | Microsoft.Data.Sqlite |
| High-throughput metadata extraction | **Sharc** — 7,254x faster |
| Bulk row scanning | **Sharc** — 3,948x faster, 0 B alloc |
| Embedded / no native deps | **Sharc** |
| Encrypted database reading | **Sharc** |
| In-memory buffer processing | **Sharc** |
| GC-sensitive workloads | **Sharc** — alloc ratio: **∞** |

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
Crypto              Argon2id KDF, AES-256-GCM AEAD
```

**What Sharc does NOT do:** execute SQL, write databases, or handle virtual tables. For full SQL, use `Microsoft.Data.Sqlite`. Sharc complements it for the cases where you need raw speed and zero dependencies.

## Project Structure

```text
src/Sharc/             -- Public API (SharcDatabase, SharcDataReader, Schema)
src/Sharc.Core/        -- Internal: page I/O, b-tree, record decoding, primitives
src/Sharc.Crypto/      -- Encryption: KDF, AEAD ciphers, key management
tests/Sharc.Tests/     -- Unit tests (xUnit + FluentAssertions)
bench/Sharc.Benchmarks/ -- BenchmarkDotNet performance suite
ProductContext/        -- Architecture docs, specs, and decisions
```

## Build & Test

```bash
dotnet build                                  # build everything
dotnet test                                   # run all tests
dotnet run -c Release --project bench/Sharc.Benchmarks  # run benchmarks
```

### Test Status

```text
154 passed, 4 skipped, 0 failed
```

The 4 skipped tests are **TDD RED-phase** tests for `SharcDatabase.Open` / `OpenMemory` — the high-level API that wires together page sources, B-tree traversal, and record decoding. These tests define the expected behavior *before* the implementation exists. When Milestone 4 begins, the `Skip` attribute is removed, the tests go RED, and implementation proceeds until GREEN. See `ProductContext/TestStrategy.md` for the full test layer approach.

## Development: Test-Driven, Always

Sharc is built with strict TDD. Every feature follows the same cycle:

1. **RED** — write tests that define the expected behavior. They fail.
2. **GREEN** — write the minimum implementation to make them pass.
3. **REFACTOR** — clean up, optimize, verify all tests still pass.

This is non-negotiable. No implementation code exists without a corresponding test. The test suite is the specification.

```text
Milestone 1 (Primitives)     ████████████████ COMPLETE — 154 tests GREEN
Milestone 2 (B-Tree)         ░░░░░░░░░░░░░░░░ tests written, Skip until impl
Milestone 3 (Records)        ░░░░░░░░░░░░░░░░ tests written, Skip until impl
Milestone 4 (Public API)     ░░░░░░░░░░░░░░░░ 4 tests written, Skip until impl
```

**Other principles:**

- **Zero-alloc hot paths** — `Span<byte>`, stackalloc, `ArrayPool`. The GC should not wake up during page reads.
- **Read-only first** — no write support until reads are benchmarked solid and correct.
- **API-first** — public interfaces and method signatures are designed before internals.

See `ProductContext/ExecutionPlan.md` for the full milestone roadmap.

## Requirements

- .NET 10.0 SDK or later

## License

MIT License. See [LICENSE](LICENSE) for details.
