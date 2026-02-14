# Sharc

**Sharc reads SQLite files 2-56x faster than Microsoft.Data.Sqlite, in pure C#, with zero native dependencies.**

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)
[![NuGet](https://img.shields.io/nuget/v/Sharc.svg?style=for-the-badge)](https://www.nuget.org/packages/Sharc/)
[![Tests](https://img.shields.io/badge/tests-1%2C067_passing-brightgreen?style=for-the-badge)]()
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

---

| **Speed** | **Size** | **Trust** |
| :--- | :--- | :--- |
| **61x faster** B-tree seeks | **~250 KB** engine footprint | **ECDSA** agent attestation |
| **13.5x faster** graph traversal | **Zero** native dependencies | **AES-256-GCM** encryption |
| **~0 B** per-row allocation | WASM / Mobile / IoT ready | **Tamper-evident** audit ledger |

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
```

[**Full Getting Started Guide**](docs/GETTING_STARTED.md)

---

## Headline Numbers

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H
> Wins 15/16 benchmarks on speed. Honest about the one outlier (Engine Init cold start).

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
dotnet test                                             # Run all 1,067 tests
dotnet run -c Release --project bench/Sharc.Benchmarks  # Run benchmarks
```

## Current Limitations

- **No SQL execution** -- reads/writes raw B-tree pages; no parser, no joins, no aggregates
- **Write support** -- INSERT with B-tree splits. UPDATE/DELETE planned.
- **No virtual tables** -- FTS5, R-Tree not supported

Sharc is a **complement** to SQLite, not a replacement. See [When NOT to Use Sharc](docs/WHEN_NOT_TO_USE.md).

## License

MIT License. See [LICENSE](LICENSE) for details.

---

Crafted through AI-assisted engineering by **[Ram Kumar Revanur](https://www.linkedin.com/in/revodoc/)**.
