# Sharc

**Sharc reads SQLite files 2-56x faster than Microsoft.Data.Sqlite, in pure C#, with zero native dependencies.**

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)
[![NuGet](https://img.shields.io/nuget/v/Sharc.svg?style=for-the-badge)](https://www.nuget.org/packages/Sharc/)
[![Tests](https://img.shields.io/badge/tests-1%2C593_passing-brightgreen?style=for-the-badge)]()
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

---

| **Speed** | **Size** | **Trust** |
| :--- | :--- | :--- |
| **61x faster** B-tree seeks | **~250 KB** engine footprint | **ECDSA** agent attestation |
| **13.5x faster** graph traversal | **Zero** native dependencies | **AES-256-GCM** encryption |
| **2.6x faster** UNION ALL queries | WASM / Mobile / IoT ready | **Tamper-evident** audit ledger |
| **~0 B** per-row allocation | SQL query pipeline built-in | UNION / INTERSECT / EXCEPT / CTE |

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

// SQL queries — SELECT, WHERE, ORDER BY, GROUP BY, UNION, CTE
using var results = db.Query(
    "SELECT dept, COUNT(*) AS cnt FROM users WHERE age > 25 GROUP BY dept ORDER BY cnt DESC LIMIT 10");
while (results.Read())
    Console.WriteLine($"{results.GetString(0)}: {results.GetInt64(1)}");

// Compound queries
using var combined = db.Query(
    "SELECT name FROM employees UNION SELECT name FROM contractors ORDER BY name");

// CTEs
using var cte = db.Query(
    "WITH active AS (SELECT id, name FROM users WHERE active = 1) " +
    "SELECT * FROM active WHERE id > 100");
```

[**Full Getting Started Guide**](docs/GETTING_STARTED.md)

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

### Query Pipeline (Query API — full SQL roundtrip: parse, compile, execute, read)

> 2,500 rows/table. Compound queries use two tables with 500 overlapping rows.

| Category | Operation | Sharc | SQLite | Speedup |
| :--- | :--- | ---: | ---: | ---: |
| **Simple** | `SELECT * FROM t` (2.5K rows) | **461 us** | 603 us | **1.3x** |
| **Filtered** | `SELECT WHERE age > 30` | 1,122 us | 850 us | 0.8x |
| **Medium** | `WHERE + ORDER BY + LIMIT 100` | 2,054 us | 277 us | 0.1x |
| **Aggregate** | `GROUP BY + COUNT + AVG` | 913 us | 477 us | 0.5x |
| **Compound** | `UNION ALL` (2x2.5K rows) | **930 us** | 2,419 us | **2.6x** |
| | `UNION` (deduplicated) | 2,384 us | 1,925 us | 0.8x |
| | `INTERSECT` | 1,606 us | 1,374 us | 0.9x |
| | `EXCEPT` | 1,544 us | 1,074 us | 0.7x |
| | `UNION ALL + ORDER BY + LIMIT` | 1,786 us | 428 us | 0.2x |
| | `3-way UNION ALL` | **1,096 us** | 1,313 us | **1.2x** |
| **CTE** | `WITH ... AS SELECT WHERE` | 1,268 us | 367 us | 0.3x |
| | `CTE + UNION ALL` | 1,365 us | 697 us | 0.5x |
| **Parameterized** | `WHERE $param AND $param` | 1,679 us | 605 us | 0.4x |

**Memory per query** (managed heap):

| Query Type | Sharc | SQLite | Notes |
| :--- | ---: | ---: | :--- |
| `SELECT *` (2.5K rows) | 414 KB | 688 B | Sharc materializes all column values |
| `WHERE` filter | 105 KB | 96 KB | Near parity — both allocate result strings |
| `UNION ALL` | 1,323 KB | 404 KB | Both sides materialized in managed arrays |
| `UNION` / `INTERSECT` / `EXCEPT` | 1.3–1.6 MB | 744 B | SQLite does set ops in native C |
| `CTE` | 282 KB | 31 KB | CTE rows materialized then re-scanned |

> **Takeaway**: Sharc's core engine (CreateReader) is 2-66x faster with zero-alloc reads. The SQL query pipeline (Query) wins on raw scans and UNION ALL where in-process materialization avoids interop overhead, but trails on ORDER BY + LIMIT and set deduplication where SQLite's native query optimizer excels. Memory allocation in compound queries is the primary optimization target.

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
dotnet test                                             # Run all 1,593 tests
dotnet run -c Release --project bench/Sharc.Benchmarks  # Run benchmarks
```

## Current Limitations

- **Query pipeline materializes results** -- compound queries (UNION/INTERSECT/EXCEPT) and CTEs allocate managed arrays; no streaming top-N optimization yet
- **Write support** -- INSERT with B-tree splits. UPDATE/DELETE planned
- **No JOIN support** -- single-table queries only; use UNION/CTE for multi-table workflows
- **No virtual tables** -- FTS5, R-Tree not supported

Sharc is a **complement** to SQLite, not a replacement. See [When NOT to Use Sharc](docs/WHEN_NOT_TO_USE.md).

## License

MIT License. See [LICENSE](LICENSE) for details.

---

Crafted through AI-assisted engineering by **[Ram Kumar Revanur](https://www.linkedin.com/in/revodoc/)**.
