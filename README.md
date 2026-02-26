# Sharc

**Sharc reads SQLite files 2-609x faster than Managed Sqlite, in pure C#, with zero native dependencies.**

[![Live Arena](https://img.shields.io/badge/Live_Arena-Run_Benchmarks-blue?style=for-the-badge)](https://revred.github.io/Sharc/)
[![NuGet](https://img.shields.io/nuget/v/Sharc.svg?style=for-the-badge)](https://www.nuget.org/packages/Sharc/)
[![Tests](https://img.shields.io/badge/tests-3%2C467_passing-brightgreen?style=for-the-badge)]()
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)

---

| **Speed** | **Size** | **Trust** | **Graph & AI** |
| :--- | :--- | :--- | :--- |
| **387x faster** indexed WHERE | **~52 KB** engine footprint | **ECDSA** agent attestation | **Cypher** query language |
| **609x faster** B-tree seeks | **Zero** native dependencies | **AES-256-GCM** encryption | **PageRank** / centrality / topo-sort |
| **13.8x faster** graph seeks | WASM / Mobile / IoT ready | **Tamper-evident** audit ledger | **Cross-arc** distributed sync |
| **~0 B** per-row read allocation | SQL query pipeline built-in | JOIN / UNION / INTERSECT / EXCEPT / Cote | **GraphWriter** — full read/write graph |

---

## When to Use Sharc

| Your Problem | Solution |
| :--- | :--- |
| Need to read/write SQLite **without native DLLs** | `dotnet add package Sharc` — pure managed C# |
| SQLite P/Invoke is **too slow** for point lookups | Sharc: **38ns** vs 23,227ns (**609x** faster) |
| Need an embedded DB for **Blazor WASM** | Sharc: **~40KB**, no Emscripten, no special headers |
| Need **AI agent memory** with audit trail | Built-in ECDSA attestation + hash-chain ledger |
| Need **graph traversal** over relational data | 2-hop BFS: **4.5x** faster than SQLite recursive CTE |
| Need **vector similarity search** for RAG | SIMD-accelerated cosine/euclidean, zero-copy, metadata pre-filter |
| Need **zero GC pressure** on hot read paths | 0 B per-row allocation via `Span<T>` |

**Not a fit?** See [When NOT to Use Sharc](docs/WHEN_NOT_TO_USE.md) — we're honest about limitations.

---

## Install

```bash
dotnet add package Sharc            # Core read/write engine
dotnet add package Sharc.Crypto     # AES-256-GCM encryption (optional)
dotnet add package Sharc.Graph      # Graph + Cypher + algorithms (optional)
dotnet add package Sharc.Vector     # Vector similarity search (optional)
dotnet add package Sharc.Arc        # Cross-arc diff, sync, distributed fragments (optional)
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

## What's New — Capabilities Added Since v1.1

These features are production-ready and fully tested:

### Cypher Query Language

Full tokenizer → parser → compiler → executor pipeline for graph queries:

```csharp
using var cypher = graph.PrepareCypher(
    "MATCH (a:Person)-[:KNOWS]->(b:Person) WHERE a.name = 'Alice' RETURN b.name");
using var results = cypher.Execute();
while (results.Read())
    Console.WriteLine(results.GetString(0));
```

### Graph Algorithms

| Algorithm | Class | Use Case |
| :--- | :--- | :--- |
| **PageRank** | `PageRankComputer` | Identify influential nodes |
| **Degree Centrality** | `DegreeCentralityComputer` | Find most connected nodes |
| **Topological Sort** | `TopologicalSortComputer` | Dependency ordering |
| **Shortest Path** | `ShortestPathComputer` | Bidirectional BFS with depth/weight/kind |

```csharp
var ranks = PageRankComputer.Compute(graph, iterations: 20, dampingFactor: 0.85);
foreach (var (nodeId, rank) in ranks.OrderByDescending(r => r.Value).Take(10))
    Console.WriteLine($"Node {nodeId}: rank {rank:F4}");
```

### GraphWriter — Full Read/Write Graph

| Method | Description |
| :--- | :--- |
| `Intern()` | Create or find a node by kind + name |
| `Link()` | Create a typed, weighted, directional edge |
| `Remove()` | Delete a node and its edges |
| `Unlink()` | Delete a specific edge |

```csharp
using var writer = new GraphWriter(db);
long alice = writer.Intern(ConceptKind.Person, "Alice");
long bob   = writer.Intern(ConceptKind.Person, "Bob");
writer.Link(alice, bob, RelationKind.Knows, weight: 1.0);
```

### Cross-Arc Distributed Sync (Sharc.Arc)

`.arc` files are portable, self-contained database fragments that work anywhere — **local disk, Dropbox, Google Drive, shared URLs, or any cloud storage**. Share a `.arc` file like you share a document; the hash-chain ledger ensures integrity no matter how it travels.

| Component | Purpose |
| :--- | :--- |
| `ArcUri` | Address any node across fragments (`arc://authority/path/table/row`) |
| `IArcLocator` | Pluggable backend — `local`, `https`, `dropbox`, `gdrive`, custom |
| `ArcResolver` | Resolve cross-fragment references across any backend |
| `ArcDiffer` | Compute schema + row-level diffs between any two `.arc` files |
| `FragmentSyncProtocol` | Delta export/import with hash-chain verification |

```csharp
// Resolve a fragment from any source — local, cloud, or URL
var resolver = new ArcResolver();
resolver.Register(new LocalArcLocator("/data/arcs"));
resolver.Register(new HttpArcLocator());  // shared links, CDN, S3

var handle = resolver.Resolve("arc://dropbox/factory-floor/sensors");
```

**Why this matters:** A Kerala health worker's tablet and a West Midlands factory terminal can each hold a `.arc` fragment. When connectivity returns, `FragmentSyncProtocol` merges deltas and the ledger proves nothing was tampered with. No central server required.

### Row-Level Entitlements

Agent-scoped access control enforced at the query layer — zero cost when not opted in:

```csharp
// Create an agent with restricted read scope
var agent = new AgentInfo("analyst", AgentClass.User, publicKey, authorityCeiling: 0,
    writeScope: "reports.*",
    readScope: "users.name,users.email",  // column-level restriction
    validityStart: 0, validityEnd: 0, parentAgent: "", coSignRequired: false, signature, algorithm);

// Entitled query succeeds
using var reader = db.Query("SELECT name, email FROM users", agent);

// Denied query throws UnauthorizedAccessException
db.Query("SELECT * FROM users", agent);  // SELECT * denied — scope restricts to specific columns
db.Query("SELECT salary FROM users", agent);  // salary not in scope
```

Entitlements cover table-level, column-level, wildcard (`SELECT *`), JOIN cross-table, WHERE/ORDER BY column references, CACHED/JIT hint paths, and view-based escalation. All enforcement is pre-query — no data leaks through side channels.

### Multi-Arc Fusion (Sharc.Arc)

Query across multiple `.arc` fragments with source provenance:

```csharp
var fused = new FusedArcContext();
fused.Mount(ArcHandle.OpenLocal("conversations.arc"), "conversations");
fused.Mount(ArcHandle.OpenLocal("codebase.arc"), "codebase");

// Every result carries its source arc
var rows = fused.Query("commits");
foreach (var row in rows)
    Console.WriteLine($"Row {row.RowId} from {row.SourceArc}");

// Discover what tables exist across all fragments
var tables = fused.DiscoverTables();
```

### Data Ingestion — CSV to Arc

```csharp
// Import CSV data into a portable .arc file
var handle = CsvArcImporter.Import(csvText, new CsvImportOptions
{
    TableName = "patients",
    HasHeader = true,
    ArcName = "health-data.arc"
});
```

### Change Event Bus

```csharp
var bus = new ChangeEventBus();
bus.Subscribe(ConceptKind.Person, change =>
    Console.WriteLine($"{change.Kind}: {change.Name}"));
```

### Tools

| Tool | Description |
| :--- | :--- |
| `Sharc.Archive` | Conversation archiver — schema, reader, writer, CLI, sync protocol |
| `Sharc.Repo` | AI agent repository — annotations, decisions, MCP tools |
| `Sharc.Context` | MCP Context Server for AI agent memory |
| `Sharc.Index` | Git history → SQLite indexer |

---

## GUID/UUID as Native Type

Sharc treats declared `GUID`/`UUID` columns as a first-class 128-bit identifier type with two encoding paths:

| Path | On-Disk Format | Alloc per value | Index Seek |
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
// Both paths: declared GUID/UUID columns are supported
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

`GetGuid(ordinal)` is strict: the column must be declared as `GUID` or `UUID`.
For merged columns, it reads two Int64 halves (`__hi`/`__lo`) with zero-allocation decode. For BLOB-backed GUID columns, it decodes the 16-byte payload.

## FIX128 / DECIMAL128 (28-29 Digit Precision)

Sharc supports exact fixed-point decimal values backed by .NET `decimal` precision (28-29 significant digits).

| Declared Type | Path | On-Disk Format | Precision | Accessor |
| :--- | :--- | :--- | :--- | :--- |
| `FIX128`, `DECIMAL128`, `DECIMAL` | Canonical payload | 16-byte BLOB | 28-29 digits | `GetDecimal()` |
| merged decimal pair | `__dhi`/`__dlo` convention | 2 x Int64 | 28-29 digits | `GetDecimal()` |

```sql
-- Canonical decimal payload
CREATE TABLE prices (
    id INTEGER PRIMARY KEY,
    amount FIX128 NOT NULL
);

-- Merged decimal FIX128 path
CREATE TABLE ticks (
    id INTEGER PRIMARY KEY,
    px__dhi INTEGER NOT NULL,
    px__dlo INTEGER NOT NULL
);
```

```csharp
using var writer = SharcWriter.From(db);

writer.Insert("prices",
    ColumnValue.FromInt64(1, 1),
    ColumnValue.FromDecimal(12345678901234567890.12345678m));

using var reader = db.CreateReader("prices");
while (reader.Read())
{
    decimal amount = reader.GetDecimal(1);
}
```

`GetDecimal(ordinal)` is strict: the column must be declared as `FIX128`/`DECIMAL128`/`DECIMAL` (or a merged decimal logical column).

---

## Headline Numbers

> BenchmarkDotNet v0.15.8 | .NET 10.0.2 | Windows 11 | Intel i7-11800H
> Latest full comparative run: February 25, 2026. Latest focused micro run: February 25, 2026.

### Core Engine (CreateReader API - zero-copy B-tree)

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Point lookup (prepared) | **38.14 ns** | 23,226.52 ns | **609x** | **0 B** | 728 B |
| Batch 6 lookups | **626.14 ns** | 122,401.32 ns | **195x** | **0 B** | 3,712 B |
| Random lookup | **217.71 ns** | 23,415.67 ns | **108x** | **0 B** | 832 B |
| Engine load | **192.76 ns** | 22,663.42 ns | **118x** | 1,592 B | 1,160 B |
| Schema read | **2,199.88 ns** | 25,058.57 ns | **11.4x** | 5,032 B | 2,536 B |
| Sequential scan (5K rows) | **875.95 us** | 5,630.27 us | **6.4x** | 1,411,576 B | 1,412,320 B |
| WHERE filter | **261.73 us** | 541.54 us | **2.1x** | **0 B** | 720 B |
| NULL scan | **148.75 us** | 727.66 us | **4.9x** | **0 B** | 688 B |
| GC pressure scan | **156.31 us** | 766.46 us | **4.9x** | **0 B** | 688 B |
| Int index seek | **1.036 us** | 31.589 us | **30.5x** | 1,272 B | 872 B |
| Graph seek (single) | **7.071 us** | 70.553 us | **10.0x** | 888 B | 648 B |
| Graph seek (batch 6) | **14.767 us** | 203.713 us | **13.8x** | 3,224 B | 3,312 B |
| Graph BFS 2-hop | **45.59 us** | 205.67 us | **4.5x** | 800 B | 2,952 B |

### Query Pipeline (Query API - full SQL roundtrip)

> 2,500 rows/table. Compound queries use two tables with 500 overlapping rows.

| Operation | Sharc | SQLite | Speedup | Sharc Alloc | SQLite Alloc |
| :--- | ---: | ---: | ---: | ---: | ---: |
| `SELECT * FROM users_a` | **595.3 us** | 730.2 us | **1.23x** | **416 B** | 736 B |
| `SELECT WHERE age > 30` | **900.4 us** | 1,085.8 us | **1.21x** | **97,720 B** | 98,016 B |
| `UNION ALL (2x2500 rows)` | **2,431.0 us** | 2,873.7 us | **1.18x** | 414,432 B | 414,032 B |
| `UNION (dedup, 500 overlap)` | 1,942.2 us | **1,940.6 us** | ~1.00x | 1,504 B | 792 B |
| `GROUP BY + COUNT + AVG` | 1,706.0 us | **553.1 us** | 0.32x | 4,576 B | 968 B |
| `WHERE + ORDER BY + LIMIT 100` | 1,012.8 us | **343.2 us** | 0.34x | 42,240 B | 5,656 B |
| `UNION ALL + ORDER BY + LIMIT 50` | 1,899.7 us | **460.0 us** | 0.24x | 32,224 B | 3,168 B |
| `INTERSECT` | 3,316.1 us | **1,317.7 us** | 0.40x | 1,192 B | 792 B |
| `EXCEPT` | 3,413.6 us | **1,193.0 us** | 0.35x | 1,192 B | 792 B |
| `3-way UNION ALL` | 1,930.6 us | **1,494.0 us** | 0.77x | 2,000 B | 824 B |
| `CTE -> SELECT WHERE` | 726.7 us | **502.8 us** | 0.69x | **31,400 B** | 31,768 B |
| `CTE + UNION ALL` | 1,210.5 us | **809.1 us** | 0.67x | 1,248 B | 864 B |
| `parameterized WHERE` | 915.3 us | **830.8 us** | 0.91x | **81,512 B** | 81,944 B |

> Query pipeline results are mixed in the latest side-by-side run: Sharc is faster on 3 of 13 measured queries, near tie on `UNION`, and slower on sort-heavy and set-heavy query shapes.
>
> Allocation note: BenchmarkDotNet `MemoryDiagnoser` reports managed allocations only. SQLite native allocations are not included in its reported heap numbers.

### Latest Focused Perf Results (2026-02-25)

Command:
`dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *FocusedPerfBenchmarks*`

Artifacts:
- `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.FocusedPerfBenchmarks-report-github.md`
- `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.FocusedPerfBenchmarks-report.csv`
- `artifacts/benchmarks/comparisons/results/Sharc.Comparisons.FocusedPerfBenchmarks-report.html`

`CandidateSpan` (`ToArray().AsSpan()` -> `CollectionsMarshal.AsSpan()`):

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :--- | ---: | ---: | ---: | ---: |
| 1 | 5.1912 / 32 B | 0.5796 / 0 B | **-88.83%** | **-100%** |
| 8 | 7.2545 / 88 B | 0.9640 / 0 B | **-86.71%** | **-100%** |
| 32 | 14.3232 / 280 B | 0.5796 / 0 B | **-95.95%** | **-100%** |
| 128 | 40.3487 / 1,048 B | 0.5719 / 0 B | **-98.58%** | **-100%** |

`ParamKeyHash` (`List+Sort+Indexer` -> pooled pair-sort):

| Count | Legacy (ns / alloc) | Optimized (ns / alloc) | Time Delta | Alloc Delta |
| :--- | ---: | ---: | ---: | ---: |
| 1 | 22.6216 / 64 B | 11.9880 / 0 B | **-47.01%** | **-100%** |
| 8 | 171.6415 / 184 B | 164.4888 / 64 B | **-4.17%** | **-65.22%** |
| 32 | 727.5258 / 376 B | 711.7530 / 64 B | **-2.17%** | **-82.98%** |
| 128 | 3,391.4931 / 1,144 B | 3,305.2322 / 64 B | **-2.54%** | **-94.41%** |

ShortRun micro-benchmark note: sub-ns means are environment-sensitive; allocation deltas are the stable signal.

> **Takeaway:** Core engine read paths remain strong (2.1x to 609x faster, with hot paths at 0 B managed allocation). Query pipeline still has clear optimization targets (`GROUP BY`, `INTERSECT/EXCEPT`, `ORDER BY + LIMIT`, and CTE composition), while focused micro-optimizations materially reduced allocation and GC pressure.

[**Full Benchmark Results**](docs/BENCHMARKS.md) | [**Run the Live Arena**](https://revred.github.io/Sharc/)

---

## Why Sharc Exists

AI agents don't need a SQL engine -- they need targeted, trusted context. Sharc delivers:

1. **Precision Retrieval**: Point lookups in 38ns (609x faster) reduce token waste.
2. **Cryptographic Provenance**: A built-in trust layer verifies who contributed what data.
3. **Graph Reasoning**: O(log N) relationship traversal for context mapping.

---

## Documentation

| Guide | Description |
| :--- | :--- |
| [Getting Started](docs/GETTING_STARTED.md) | Zero to working code in 5 minutes |
| [API Quick Reference](docs/API_QUICK_REFERENCE.md) | The 10 operations you'll use most |
| [Integration Recipes](docs/INTEGRATION_RECIPES.md) | Copy-paste patterns for Blazor, AI agents, graph, encryption |
| [Benchmarks](docs/BENCHMARKS.md) | Full comparison with SQLite plus execution-tier breakdowns |
| [Architecture](docs/ARCHITECTURE.md) | How Sharc achieves zero-allocation reads |
| [Cookbook](docs/COOKBOOK.md) | 15 recipes for common patterns |
| [Alternatives](docs/ALTERNATIVES.md) | Sharc vs SQLite vs LiteDB vs DuckDB |
| [Graph DB Comparison](docs/GRAPH_DB_COMPARISON.md) | Sharc vs SurrealDB, ArangoDB, Neo4j |
| [JitSQL Cross-Language](docs/JITSQL_CROSS_LANGUAGE.md) | JitSQL for JS/TS/Python/Go developers |
| [Vector Search](docs/VECTOR_SEARCH.md) | Embedding storage, similarity search, RAG patterns |
| [When NOT to Use](docs/WHEN_NOT_TO_USE.md) | Honest limitations |
| [FAQ](docs/FAQ.md) | Common questions answered |
| [Migration Guide](docs/MIGRATION.md) | Switching from Microsoft.Data.Sqlite |
| [API Wiki](wiki/Home.md) | Full API reference with copy-paste patterns |

## Build & Test

```bash
dotnet build                                            # Build everything
dotnet test                                             # Run all tests
dotnet run -c Release --project bench/Sharc.Benchmarks  # Run benchmarks
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *FocusedPerfBenchmarks*  # Run latest focused perf suite
dotnet script ./samples/run-all.csx -- --build-only     # Validate all samples compile (requires dotnet-script)
```

## Release Rule

PRs into `main` are treated as release PRs and must include:

- `README.md` updates for user-facing package/API changes
- `CHANGELOG.md` release notes under `## [1.2.<PR_NUMBER>] - YYYY-MM-DD`
- NuGet package staging in `artifacts/nuget` (ignored folder) before publish

## Project Structure

```text
src/
  Sharc/                    Public API + Write Engine + Trust Layer
  Sharc.Core/             B-Tree, Records, Page I/O, Primitives
  Sharc.Query/              SQL pipeline: parser, compiler, executor
  Sharc.Crypto/             AES-256-GCM encryption, Argon2id KDF, HKDF-SHA256
  Sharc.Graph/              Graph engine: Cypher, PageRank, GraphWriter, algorithms
  Sharc.Graph.Surface/      Graph interfaces and models
  Sharc.Vector/             SIMD-accelerated vector similarity search
  Sharc.Arc/                Cross-arc: ArcUri, ArcResolver, ArcDiffer, fragment sync
  Sharc.Arena.Wasm/         Live benchmark arena (Blazor WASM)
tests/                      3,467 tests across 11 projects
  Sharc.Tests/              Core unit tests
  Sharc.IntegrationTests/   End-to-end tests
  Sharc.Query.Tests/        Query pipeline tests
  Sharc.Graph.Tests.Unit/   Graph + Cypher + algorithm tests
  Sharc.Graph.Tests.Perf/   Graph performance benchmarks
  Sharc.Arc.Tests/          Cross-arc diff + sync tests
  Sharc.Archive.Tests/      Archive tool tests
  Sharc.Vector.Tests/       Vector similarity tests
  Sharc.Repo.Tests/         Repository + MCP tool tests
  Sharc.Index.Tests/        Index CLI tests
  Sharc.Context.Tests/      MCP context tests
bench/
  Sharc.Benchmarks/         BenchmarkDotNet suite (Sharc vs SQLite)
  Sharc.Comparisons/        Graph + query benchmarks
samples/
  ApiComparison/            Sharc vs SQLite end-to-end timing comparison
  BasicRead/                Minimal read example
  BrowserOpfs/              Browser OPFS interop and storage portability patterns
  BulkInsert/               Transactional batch insert
  UpsertDeleteWhere/        Upsert and predicate delete workflows
  FilterAndProject/         Column projection + filtering
  PointLookup/              B-tree Seek performance demo
  VectorSearch/             Embedding storage and nearest-neighbor lookup
  EncryptedRead/            AES-256-GCM encrypted database read
  ContextGraph/             Graph traversal example
  TrustComplex/             Agent trust layer demo
  README.md                 Sample index and run instructions
  run-all.csx               C# script to build/run all samples
tools/
  Sharc.Archive/            Conversation archiver (schema + sync protocol)
  Sharc.Repo/               AI agent repository (annotations + decisions + MCP)
  Sharc.Context/            MCP Context Server
  Sharc.Index/              Git history → SQLite CLI
  Sharc.Debug/              Debug utilities
docs/                       Architecture, benchmarks, cookbook, FAQ, migration guides
PRC/                        Architecture decisions, specs, execution plans
```

## Current Limitations

- **Query pipeline materializes results** -- Cotes allocate managed arrays. Set operations (UNION/INTERSECT/EXCEPT) use pooled IndexSet with ArrayPool storage (~1.4 KB). Streaming top-N and streaming aggregation reduce memory for ORDER BY + LIMIT and GROUP BY queries
- **Single-writer** -- one writer at a time; no WAL-mode concurrent writes
- **JOIN support** -- INNER, LEFT, RIGHT, FULL OUTER, and CROSS joins via hash join strategy with tiered zero-allocation execution and index-accelerated WHERE pushdown
- **No virtual tables** -- FTS5, R-Tree not supported

Sharc is a **complement** to SQLite, not a replacement. See [When NOT to Use Sharc](docs/WHEN_NOT_TO_USE.md).

## License

MIT License. See [LICENSE](LICENSE) for details.

---

Crafted through AI-assisted engineering by **[Ram Kumar Revanur](https://www.linkedin.com/in/revodoc/)**.

