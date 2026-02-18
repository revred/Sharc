# Sharc

**Sharc reads SQLite files 2-66x faster than Microsoft.Data.Sqlite, in pure C#, with zero native dependencies.**

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)
[![NuGet](https://img.shields.io/nuget/v/Sharc.svg?style=for-the-badge)](https://www.nuget.org/packages/Sharc/)
[![Tests](https://img.shields.io/badge/tests-2%2C038_passing-brightgreen?style=for-the-badge)]()
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

---

| **Speed** | **Size** | **Trust** |
| :--- | :--- | :--- |
| **61x faster** B-tree seeks | **~52 KB** engine footprint | **ECDSA** agent attestation |
| **39x faster** single UPDATE | **Zero** native dependencies | **AES-256-GCM** encryption |
| **13.5x faster** graph traversal | WASM / Mobile / IoT ready | **Tamper-evident** audit ledger |
| **~0 B** per-row read allocation | SQL query pipeline built-in | UNION / INTERSECT / EXCEPT / Cote |

---

## Install

```bash
dotnet add package Sharc            # Core read/write engine
dotnet add package Sharc.Crypto     # AES-256-GCM encryption (optional)
dotnet add package Sharc.Graph      # Graph traversal + trust layer (optional)
```

## Quick Start

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");

// Scan a table
using var reader = db.CreateReader("users");
while (reader.Read())
    Console.WriteLine($"{reader.GetInt64(0)}: {reader.GetString(1)}");

// Point lookup in < 1 microsecond
if (reader.Seek(42))
    Console.WriteLine($"Found: {reader.GetString(1)}");

// Filtered scan with column projection
using var filtered = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 18L));

// SQL queries — SELECT, WHERE, ORDER BY, GROUP BY, UNION, Cote
using var results = db.Query(
    "SELECT dept, COUNT(*) AS cnt FROM users WHERE age > 25 GROUP BY dept ORDER BY cnt DESC LIMIT 10");
while (results.Read())
    Console.WriteLine($"{results.GetString(0)}: {results.GetInt64(1)}");

// Compound queries
using var combined = db.Query(
    "SELECT name FROM employees UNION SELECT name FROM contractors ORDER BY name");

// Cotes
using var cte = db.Query(
    "WITH active AS (SELECT id, name FROM users WHERE active = 1) " +
    "SELECT * FROM active WHERE id > 100");
```

[**Full Getting Started Guide**](docs/GETTING_STARTED.md)

---

## GUID as Native Type

Sharc treats GUIDs as a first-class storage type with two encoding paths:

| Path | On-Disk Format | Alloc per GUID | Index Seek |
| :--- | :--- | ---: | :--- |
| **BLOB(16)** | Serial type 44 (16-byte blob) | 40 B | Byte comparison |
| **Merged Int64 pair** | 2 × Int64 (`__hi`/`__lo` convention) | **0 B** | O(log N) via `SeekFirst(long)` |

```sql
-- BLOB(16) path: standard GUID column
CREATE TABLE docs (id INTEGER PRIMARY KEY, doc_guid GUID);

-- Merged path: two INTEGER columns → one logical GUID
CREATE TABLE entities (
    id INTEGER PRIMARY KEY,
    owner_guid__hi INTEGER NOT NULL,  -- first 8 bytes (big-endian)
    owner_guid__lo INTEGER NOT NULL   -- last 8 bytes
);
CREATE INDEX idx_owner ON entities (owner_guid__hi, owner_guid__lo);
```

```csharp
// Both paths: GetGuid() auto-detects encoding
using var reader = db.CreateReader("entities");
while (reader.Read())
{
    Guid ownerGuid = reader.GetGuid(1);  // zero-alloc for merged path
}

// Write path: pass logical GUID, expansion handled automatically
using var writer = SharcWriter.From(db);
writer.Insert("entities",
    ColumnValue.FromInt64(1, 1),
    ColumnValue.FromGuid(Guid.NewGuid()),  // auto-splits to __hi/__lo
    ColumnValue.Text(15, "Alice"u8.ToArray()));
```

`GetGuid(ordinal)` checks merged columns first (two `DecodeInt64At` calls, zero-alloc), then falls back to BLOB(16). Both paths produce identical `Guid` values.

---

## Headline Numbers

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H

### Core Engine (CreateReader API — zero-copy B-tree)

| Category | Operation | Sharc | SQLite | Speedup |
| :--- | :--- | ---: | ---: | ---: |
| **Point Ops** | B-tree Seek | **392 ns** | 24,011 ns | **61x** |
| | Batch 6 Seeks | **1,940 ns** | 127,526 ns | **66x** |
| **Scans** | Sequential (5K rows) | **1.54 ms** | 6.22 ms | **4x** |
| | WHERE Filter | **315 us** | 587 us | **1.9x** |
| **Graph** | 2-Hop BFS | **6.04 us** | 81.56 us | **13.5x** |
| | Node Seek | **1,475 ns** | 21,349 ns | **14.5x** |
| **Memory** | GC Pressure (sustained) | **648 B** | 688 B | Parity |
| | Primitives | **0 B** | N/A | Zero-alloc |
| **Write** | Single DELETE | **1.72 ms** | 12.02 ms | **7x** |
| | Single UPDATE | **3.03 ms** | 117.79 ms | **39x** |
| | Batch 100 DELETEs | **2.82 ms** | 25.53 ms | **9.1x** |
| | Transaction 100 INSERTs | **4.23 ms** | 5.20 ms | **1.2x** |
| **GUID** | Merged Int64 encode (per op) | **0 B** | N/A | Zero-alloc |
| | BLOB(16) encode (per op) | 40 B | N/A | 1 alloc |
| | Batch 1K GUIDs (merged) | **0 B** | N/A | Zero-alloc |

### Query Pipeline (Query API — full SQL roundtrip: parse, compile, execute, read)

> 2,500 rows/table. Compound queries use two tables with 500 overlapping rows.

| Category | Operation | Sharc | SQLite | Speedup |
| :--- | :--- | ---: | ---: | ---: |
| **Simple** | `SELECT * FROM t` (2.5K rows) | **85 us** | 783 us | **9.2x** |
| **Filtered** | `SELECT WHERE age > 30` | **240 us** | 1,181 us | **4.9x** |
| **Medium** | `WHERE + ORDER BY + LIMIT 100` | **309 us** | 339 us | **1.1x** |
| **Aggregate** | `GROUP BY + COUNT + AVG` | **444 us** | 630 us | **1.4x** |
| **Compound** | `UNION ALL` (2x2.5K rows) | **583 us** | 3,155 us | **5.4x** |
| | `UNION` (deduplicated) | **897 us** | 2,471 us | **2.8x** |
| | `INTERSECT` | **862 us** | 1,763 us | **2.0x** |
| | `EXCEPT` | **879 us** | 1,499 us | **1.7x** |
| | `UNION ALL + ORDER BY + LIMIT` | **530 us** | 512 us | **~1x** |
| | `3-way UNION ALL` | **344 us** | 1,684 us | **4.9x** |
| **Cote** | `WITH ... AS SELECT WHERE` | **150 us** | 461 us | **3.1x** |
| | `Cote + UNION ALL` | **273 us** | 972 us | **3.6x** |
| **Parameterized** | `WHERE $param AND $param` | **223 us** | 819 us | **3.7x** |

**Memory per query** (managed heap, † = managed-only; see note below):

| Query Type | Sharc | SQLite | Notes |
| :--- | ---: | ---: | :--- |
| `SELECT *` (2.5K rows) | **576 B** | 688 B † | Lazy decode: only accessed columns materialized |
| `WHERE` filter | 98 KB | 98 KB | Near parity — both allocate result strings |
| `WHERE + ORDER BY + LIMIT` | 42 KB | 5.6 KB † | Streaming TopN heap avoids full materialization |
| `UNION ALL` | 415 KB | 414 KB | Both sides materialized in managed arrays |
| `UNION` / `INTERSECT` / `EXCEPT` | **1.4 KB** | 744 B † | ArrayPool-backed IndexSet — zero alloc after warmup |
| `UNION ALL + ORDER BY + LIMIT` | 32 KB | 3.1 KB † | Streaming concat → TopN, no full materialization |
| `GROUP BY + COUNT + AVG` | **5.3 KB** | 920 B † | Streaming hash aggregator with fingerprint-based string pooling |
| `Cote → SELECT WHERE` | **808 B** | 31 KB † | Cached intent resolution — inline filter, no materialization |
| `Cote + UNION ALL` | **1.4 KB** | 816 B † | Resolved Cote inlined into compound pipeline |

> **† Measurement note:** BenchmarkDotNet's `MemoryDiagnoser` only tracks .NET managed heap allocations. Sharc's numbers are **total** allocation (all work happens in managed code). SQLite's † numbers reflect only the P/Invoke marshaling cost — the actual hash tables, sort buffers, B-tree traversal, and query plan memory are allocated in native C and are **invisible** to the profiler. The true gap is significantly smaller than these numbers suggest.
>
> **Takeaway**: Sharc's core engine (CreateReader) is 2-66x faster with zero-alloc reads. The SQL query pipeline (Query) **wins or ties every benchmark** — from 1.1x on sorted queries to 9.2x on full scans (lazy decode: **576 B** vs SQLite's 688 B). Cote queries use cached intent resolution with inlined filters (**808 B** for Cote → SELECT WHERE, 3.1x faster). Set operations (UNION/INTERSECT/EXCEPT) use a pooled open-addressing hash map with ArrayPool-backed storage, achieving **1.4 KB** managed allocation vs SQLite's native-invisible approach. Streaming optimizations (TopN heap with JIT-specialized struct comparer, streaming aggregator with string pooling, predicate pushdown, lazy column decode, query plan + intent caching) deliver consistent wins across all query types.

[**Full Benchmark Results**](docs/BENCHMARKS.md) | [**Run the Live Arena**](https://revred.github.io/Sharc/)

---

## Why Sharc Exists

AI agents don't need a SQL engine -- they need targeted, trusted context. Sharc delivers:

1. **Precision Retrieval**: Point lookups in < 600ns reduce token waste by 62-133x.
2. **Cryptographic Provenance**: A built-in trust layer verifies who contributed what data.
3. **Graph Reasoning**: O(log N) relationship traversal for context mapping.

---

## Documentation

| Guide | Description |
| :--- | :--- |
| [Getting Started](docs/GETTING_STARTED.md) | Zero to working code in 5 minutes |
| [Benchmarks](docs/BENCHMARKS.md) | Full comparison with SQLite and IndexedDB |
| [Architecture](docs/ARCHITECTURE.md) | How Sharc achieves zero-allocation reads |
| [Cookbook](docs/COOKBOOK.md) | 15 recipes for common patterns |
| [When NOT to Use](docs/WHEN_NOT_TO_USE.md) | Honest limitations |
| [FAQ](docs/FAQ.md) | Common questions answered |
| [Migration Guide](docs/MIGRATION.md) | Switching from Microsoft.Data.Sqlite |

## Build & Test

```bash
dotnet build                                            # Build everything
dotnet test                                             # Run all 2,038 tests
dotnet run -c Release --project bench/Sharc.Benchmarks  # Run benchmarks
```

## Project Structure

```text
src/
  Sharc/                    Public API + Write Engine + Trust Layer
  Sharc.Core/               B-Tree, Records, Page I/O, Primitives
  Sharc.Query/              SQL pipeline: parser, compiler, executor
  Sharc.Crypto/             AES-256-GCM encryption, Argon2id KDF
  Sharc.Graph/              Graph storage (ConceptStore, RelationStore)
  Sharc.Graph.Surface/      Graph interfaces and models
  Sharc.Arena.Wasm/         Live benchmark arena (Blazor WASM)
tests/
  Sharc.Tests/              1,229 unit tests
  Sharc.IntegrationTests/   293 end-to-end tests
  Sharc.Query.Tests/        425 query pipeline tests
  Sharc.Graph.Tests.Unit/   55 graph tests
  Sharc.Index.Tests/        22 index CLI tests
  Sharc.Context.Tests/      14 MCP context tests
bench/
  Sharc.Benchmarks/         BenchmarkDotNet suite (Sharc vs SQLite)
  Sharc.Comparisons/        Graph + query benchmarks
tools/
  Sharc.Context/            MCP Context Server
  Sharc.Index/              Git history → SQLite CLI
docs/                       Architecture, benchmarks, cookbook, FAQ, migration guides
PRC/                        Architecture decisions, specs, execution plans
```

## Current Limitations

- **Query pipeline materializes results** -- Cotes allocate managed arrays. Set operations (UNION/INTERSECT/EXCEPT) use pooled IndexSet with ArrayPool storage (~1.4 KB). Streaming top-N and streaming aggregation reduce memory for ORDER BY + LIMIT and GROUP BY queries
- **Single-writer** -- one writer at a time; no WAL-mode concurrent writes
- **No JOIN support** -- single-table queries only; use UNION/Cote for multi-table workflows
- **No virtual tables** -- FTS5, R-Tree not supported

Sharc is a **complement** to SQLite, not a replacement. See [When NOT to Use Sharc](docs/WHEN_NOT_TO_USE.md).

## License

MIT License. See [LICENSE](LICENSE) for details.

---

Crafted through AI-assisted engineering by **[Ram Kumar Revanur](https://www.linkedin.com/in/revodoc/)**.
