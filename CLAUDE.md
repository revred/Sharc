# CLAUDE.md — Sharc Project Instructions

## Project Overview

Sharc is a high-performance, pure managed C# library that reads SQLite database files (format 3) from disk and in-memory buffers, with optional password-based encryption. It is **read-only** — no writes, no SQL VM, no query planner.

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
│  IPageSource: File | Memory | Mmap | Cached     │
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
Sharc.Exceptions             — Public exception types
Sharc.Core                   — Internal interfaces (IPageSource, IBTreeReader, etc.)
Sharc.Core.Primitives        — Varint, serial type codecs
Sharc.Core.Format            — File/page header parsers
Sharc.Core.IO                — Page sources, caching
Sharc.Core.BTree             — B-tree traversal
Sharc.Core.Records           — Record decoding
Sharc.Core.Schema            — Internal schema reader
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
├── docs/                              ← Reference docs (format analysis, coding standards)
├── PRC/                               ← Architecture docs & decisions (ADRs, specs)
├── src/
│   ├── Sharc/                         ← Public API (SharcDatabase, SharcDataReader, Schema)
│   ├── Sharc.Core/                    ← Internal engine (B-Tree, Records, IO, Primitives)
│   ├── Sharc.Crypto/                  ← Encryption (KDF, AEAD ciphers, key management)
│   ├── Sharc.Graph/                   ← Graph storage (ConceptStore, RelationStore)
│   └── Sharc.Graph.Surface/           ← Graph models (NodeKey, GraphEdge, RecordId)
├── tests/
│   ├── Sharc.Tests/                   ← Unit tests (463 tests)
│   ├── Sharc.IntegrationTests/        ← End-to-end tests (61 tests)
│   ├── Sharc.Graph.Tests.Unit/        ← Graph model tests (49 tests)
│   ├── Sharc.Graph.Tests.Performance/ ← Graph performance tests
│   ├── Sharc.Context.Tests/           ← MCP context query tests (14 tests)
│   └── Sharc.Index.Tests/            ← Index CLI tests (22 tests)
├── bench/
│   ├── Sharc.Benchmarks/             ← Core BenchmarkDotNet suite (Sharc vs SQLite)
│   └── Sharc.Comparisons/            ← Graph benchmark suite
└── tools/
    ├── Sharc.Context/                ← MCP Context Server (queries, benchmarks, tests)
    └── Sharc.Index/                  ← GCD CLI (git history → SQLite)
```

## Current Status

**Milestones 1–10 ALL COMPLETE**: 696 tests passing (463 unit + 61 integration + 49 graph + 22 index + 14 context + 87 crypto/filtering/WAL).

All core layers implemented and benchmarked: Primitives, Page I/O (File, Memory, Mmap), B-Tree (with Seek + Index reads), Records, Schema, Table Scans, Graph Storage, WHERE Filtering (SharcFilter, 6 operators), WAL Read Support, AES-256-GCM Encryption (Argon2id/PBKDF2-SHA512 KDF). See README.md for benchmark results.

## What NOT To Do

- **Do not add SQL parsing or execution** — Sharc is a format reader, not a database engine
- **Do not add write support** until read-only is solid and benchmarked (Milestone 10 gate)
- **Do not add dependencies** without checking `PRC/DependencyPolicy.md`
- **Do not use `unsafe` code** unless profiling proves it's necessary and the gain is >20%
- **Do not allocate in hot paths** — use spans, stackalloc, ArrayPool
- **Do not break the public API surface** without updating all docs and tests
- **Do not merge without all tests green**

## Key Files to Understand the System

| To understand... | Read... |
|-----------------|---------|
| What Sharc does | `README.md` |
| SQLite file format | `docs/SQLiteC_Analysis.md` and `docs/FileFormatQuickRef.md` |
| Why pure managed | `PRC/StrategyDecision.md` |
| Architecture layers | `PRC/ArchitectureOverview.md` |
| Public API design | `PRC/APIDesign.md` |
| Encryption format | `PRC/EncryptionSpec.md` |
| What to build next | `PRC/ExecutionPlan.md` |
| How to test | `PRC/TestStrategy.md` |
| All decisions made | `PRC/DecisionLog.md` |

## Asking Questions

If you encounter ambiguity:
1. List 2–3 reasonable interpretations
2. Pick the one that's simplest, most testable, and closest to SQLite's behavior
3. Document the choice in `PRC/DecisionLog.md`
4. Add a `// DECISION:` comment at the relevant code site
