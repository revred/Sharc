# CLAUDE.md — Sharc Project Instructions

## Project Overview

Sharc is a high-performance, pure managed C# library that reads and writes SQLite database files (format 3) from disk and in-memory buffers, with optional password-based encryption. It includes a cryptographic agent trust layer for AI multi-agent coordination.

## Build & Test Commands

```bash
# Build everything
dotnet build

# Run unit tests
dotnet test tests/Sharc.Tests

# Run integration tests
dotnet test tests/Sharc.IntegrationTests

# Run all tests
dotnet test

# Run benchmarks (Release only)
dotnet run -c Release --project bench/Sharc.Benchmarks

# Run a specific test class
dotnet test tests/Sharc.Tests --filter "FullyQualifiedName~VarintDecoderTests"

# Run tests with verbose output
dotnet test --verbosity normal
```

## Architecture — Read This First

Sharc is a **layered file-format reader**, not a database engine.

```
┌────────────────────────────────────────────────┐
│  Public API (Sharc/)                           │
│  SharcDatabase → SharcDataReader               │
│  SharcSchema → TableInfo, ColumnInfo           │
├────────────────────────────────────────────────┤
│  Trust Layer (Sharc/Trust/)                    │
│  AgentRegistry: ECDSA self-attestation         │
│  LedgerManager: hash-chain audit log           │
│  ReputationEngine, Co-Signatures, Governance   │
├────────────────────────────────────────────────┤
│  Write Layer (Sharc/Write/, Sharc.Core/Write/) │
│  SharcWriter → WriteEngine → BTreeMutator      │
│  RecordEncoder, CellBuilder, PageManager       │
│  RollbackJournal, Transaction (ACID)           │
├────────────────────────────────────────────────┤
│  Graph Layer (Sharc.Graph/)                    │
│  ConceptStore, RelationStore, SeekFirst        │
├────────────────────────────────────────────────┤
│  Schema Layer (Sharc.Core/Schema/)             │
│  SchemaReader: parses sqlite_schema table      │
├────────────────────────────────────────────────┤
│  Record Layer (Sharc.Core/Records/)            │
│  RecordDecoder: varint + serial type → values  │
├────────────────────────────────────────────────┤
│  B-Tree Layer (Sharc.Core/BTree/)              │
│  BTreeReader → BTreeCursor → CellParser        │
├────────────────────────────────────────────────┤
│  Page I/O Layer (Sharc.Core/IO/)               │
│  IPageSource: File | Memory | Mmap | Cached    │
│  IPageTransform: Identity | Decrypting         │
├────────────────────────────────────────────────┤
│  Primitives (Sharc.Core/Primitives/)           │
│  VarintDecoder, SerialTypeCodec                │
├────────────────────────────────────────────────┤
│  Crypto (Sharc.Crypto/)                        │
│  KDF (Argon2id), AEAD (AES-256-GCM)           │
└────────────────────────────────────────────────┘
```

## Key Conventions

### TDD Workflow — Non-Negotiable

Every feature starts with tests. The cycle is:
1. Write failing test(s) that define behavior
2. Run → RED
3. Write minimum implementation to pass
4. Run → GREEN
5. Refactor
6. Run all tests → still GREEN
7. Commit

Never write implementation code without a corresponding test. If you're unsure what to test, check `PRC/TestStrategy.md`.

### Test Naming

```
[MethodUnderTest]_[Scenario]_[ExpectedResult]
```

Examples: `DecodeVarint_SingleByteZero_ReturnsZero`, `Parse_InvalidMagic_ThrowsInvalidDatabaseException`

### Code Style

- **Prefer `ReadOnlySpan<byte>` and `Span<byte>`** over `byte[]` in all internal APIs
- **Zero-allocation hot paths**: no LINQ, no boxing, no string interpolation in tight loops
- **`[MethodImpl(MethodImplOptions.AggressiveInlining)]`** on tiny primitive methods (varint decode, serial type lookup)
- **Big-endian reads**: use `BinaryPrimitives.ReadUInt16BigEndian()` etc., never manual bit shifts
- **Structs for parsed headers**: `DatabaseHeader`, `BTreePageHeader`, `ColumnValue` are `readonly struct`
- **Classes for stateful objects**: `SharcDatabase`, `SharcDataReader`, page sources, cursors
- **`sealed`** on all classes unless designed for inheritance (almost none are)
- **`required` properties** with `init` for immutable data objects
- **No heavy dependencies**: only xUnit, BenchmarkDotNet, Microsoft.Data.Sqlite (tools only), ModelContextProtocol (MCP server). No FluentAssertions — use plain `Assert.*`. No Newtonsoft, no EF, no DI container
- **XML doc comments** on all public API members
- **Nullable reference types** enabled everywhere (`<Nullable>enable</Nullable>`)
- **`using` declarations** (not `using` blocks) for disposables in short-lived scopes

### Namespace Conventions

```
Sharc                        — Public API (SharcDatabase, SharcDataReader, options, enums)
Sharc.Schema                 — Public schema models (TableInfo, ColumnInfo, etc.)
Sharc.Trust                  — Trust layer (AgentRegistry, LedgerManager, ReputationEngine)
Sharc.Exceptions             — Public exception types
Sharc.Core                   — Internal interfaces (IPageSource, IBTreeReader, etc.)
Sharc.Core.Primitives        — Varint, serial type codecs
Sharc.Core.Format            — File/page header parsers
Sharc.Core.IO                — Page sources, caching, rollback journal
Sharc.Core.BTree             — B-tree traversal and mutation
Sharc.Core.Records           — Record decoding and encoding
Sharc.Core.Schema            — Internal schema reader
Sharc.Core.Trust             — Trust models (AgentInfo, AgentClass, TrustPayload, LedgerEntry)
Sharc.Core.Write             — Write engine internals (PageManager, CellBuilder)
Sharc.Crypto                 — Encryption (KDF, ciphers, key handles)
```

### Error Handling

- Throw `InvalidDatabaseException` for file-format violations (bad magic, invalid header)
- Throw `CorruptPageException` for page-level corruption (bad page type, pointer out of bounds)
- Throw `SharcCryptoException` for encryption errors (wrong password, tampered data)
- Throw `UnsupportedFeatureException` for valid-but-unsupported SQLite features
- Throw `ArgumentException` / `ArgumentOutOfRangeException` for API misuse
- Never catch and swallow exceptions in library code
- Use `ThrowHelper` pattern for hot paths to keep method bodies JIT-friendly

### Performance Rules

- All page reads go through `IPageSource` — never open files directly from upper layers
- Page cache is LRU with configurable capacity (default 2000 pages)
- Record decoding operates directly on page spans — no intermediate buffer copies
- Column projection: when a reader requests specific columns, skip decoding unwanted columns
- Overflow page assembly: use `ArrayPool<byte>.Shared` for temporary buffers, return after use

## Project Structure

```
sharc/
├── CLAUDE.md                          ← You are here
├── README.md                          ← User-facing docs
├── Sharc.sln                          ← Solution file
├── docs/                              ← Reference docs (format analysis, trust architecture)
├── PRC/                               ← Architecture docs & decisions (ADRs, specs)
├── secrets/                           ← Competitive analysis, internal strategy
├── src/
│   ├── Sharc/                         ← Public API + Trust Layer + Write Engine
│   ├── Sharc.Core/                    ← Internal engine (B-Tree, Records, IO, Write, Trust models)
│   ├── Sharc.Crypto/                  ← Encryption (KDF, AEAD ciphers, key management)
│   ├── Sharc.Graph/                   ← Graph storage (ConceptStore, RelationStore)
│   ├── Sharc.Graph.Surface/           ← Graph models (NodeKey, GraphEdge, RecordId)
│   └── Sharc.Scene/                   ← Trust Playground (agent simulation & visualization)
├── tests/
│   ├── Sharc.Tests/                   ← Unit tests (832 tests: core + trust + write + crypto)
│   ├── Sharc.IntegrationTests/        ← End-to-end tests (146 tests)
│   ├── Sharc.Graph.Tests.Unit/        ← Graph model tests (50 tests)
│   ├── Sharc.Context.Tests/           ← MCP context query tests (14 tests)
│   └── Sharc.Index.Tests/             ← Index CLI tests (22 tests)
├── bench/
│   ├── Sharc.Benchmarks/              ← Core BenchmarkDotNet suite (Sharc vs SQLite)
│   └── Sharc.Comparisons/             ← Graph + core benchmarks
└── tools/
    ├── Sharc.Context/                 ← MCP Context Server (queries, benchmarks, tests)
    └── Sharc.Index/                   ← GCD CLI (git history → SQLite)
```

## Current Status

**1,067 tests passing** across 5 test projects (832 unit + 146 integration + 53 graph + 22 index + 14 context).

All layers implemented and benchmarked: Primitives, Page I/O (File, Memory, Mmap), B-Tree (with Seek + Index reads), Records, Schema, Table Scans, Graph Storage (SeekFirst O(log N)), WHERE Filtering (SharcFilter + FilterStar JIT), WAL Read Support, AES-256-GCM Encryption (Argon2id KDF), Write Engine (INSERT with B-tree splits, ACID transactions), Agent Trust Layer (ECDSA attestation, hash-chain ledger, co-signatures, governance, reputation scoring). See README.md for benchmark results.

## What NOT To Do

- **Do not add SQL parsing or execution** — Sharc reads/writes raw B-tree pages, not a SQL engine
- **Do not add dependencies** without checking `PRC/DependencyPolicy.md`
- **Do not use `unsafe` code** unless profiling proves it's necessary and the gain is >20%
- **Do not allocate in hot paths** — use spans, stackalloc, ArrayPool
- **Do not break the public API surface** without updating all docs and tests
- **Do not merge without all tests green**
- **Do not bypass the Trust layer** — all agent operations must go through `AgentRegistry` and `LedgerManager`

## Key Files to Understand the System

| To understand... | Read... |
|-----------------|---------|
| What Sharc does | `README.md` |
| SQLite file format | `docs/SQLiteAnalysis.md` and `docs/FileFormatQuickRef.md` |
| Why pure managed | `PRC/StrategyDecision.md` |
| Architecture layers | `PRC/ArchitectureOverview.md` |
| Public API design | `PRC/APIDesign.md` |
| Encryption format | `PRC/EncryptionSpec.md` |
| Trust architecture | `docs/DistributedTrustArchitecture.md` |
| Ledger features | `PRC/LedgerFeatures.md` |
| What to build next | `PRC/ExecutionPlan.md` |
| How to test | `PRC/TestStrategy.md` |
| All decisions made | `PRC/DecisionLog.md` |

## Asking Questions

If you encounter ambiguity:
1. List 2–3 reasonable interpretations
2. Pick the one that's simplest, most testable, and closest to SQLite's behavior
3. Document the choice in `PRC/DecisionLog.md`
4. Add a `// DECISION:` comment at the relevant code site
