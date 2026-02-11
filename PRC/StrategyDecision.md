# Strategy Decision — Sharc v0.1

## Options Evaluated

### Option A — Pure Managed Reader (SELECTED ✓)
A ground-up C# implementation that parses SQLite format 3 database files directly.
No native dependencies. No P/Invoke. No embedded SQLite.

### Option B — Hybrid (P/Invoke Wrapper)
Embed the SQLite C amalgamation, use P/Invoke for reads, wrap in OO C# API.

### Option C — Full Port
Port significant portions of SQLite C to C#.

## Decision: Option A — Pure Managed Reader

### Justification

1. **Zero native dependencies**: Sharc runs on any .NET 8+ platform without native
   library packaging headaches (linux-x64, linux-arm64, win-x64, osx-arm64, WASM, etc.).

2. **In-memory buffer support**: A pure managed reader trivially accepts
   `ReadOnlyMemory<byte>` as a page source. With P/Invoke, this requires marshaling
   memory into the C layer — fragile and defeats the purpose.

3. **Encryption integration**: Page-level encryption/decryption slots naturally into
   a managed `IPageTransform` pipeline. No need to modify SQLite C internals or
   maintain a custom SEE-like fork.

4. **Performance ceiling is adequate**: The SQLite read path is I/O-bound for
   disk-backed databases and memory-bandwidth-bound for in-memory ones. Managed
   code with `Span<byte>`, `BinaryPrimitives`, and stack-based parsing can achieve
   within 1.5–3× of native read throughput — acceptable for the target use cases.

5. **Scope is tractable**: The read-only subset (header → pager → b-tree → records)
   is well-documented and bounded. The SQLite file format is stable and versioned.
   We're parsing a data format, not reimplementing a query engine.

6. **Testability**: Every layer is a C# interface backed by pure functions operating
   on byte spans — ideal for TDD, fuzz testing, and property-based testing.

### Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Format edge cases | Extensive integration tests against real SQLite DBs |
| Performance gaps | BenchmarkDotNet profiling; optimize hot paths |
| WAL complexity | Deferred to v0.2; isolated behind `IPageSource` |
| Feature creep | Strict milestone gates; read-only until benchmarked |

### What Sharc Will NOT Do (v0.1)

- Execute SQL (no VM, no VDBE, no query planner)
- Write, modify, or compact databases
- Handle virtual tables (FTS, R-Tree)
- Parse WAL files
- Support custom collation sequences

### Boundary Definition

Sharc is a **file-format reader**, not a database engine. Users either:
1. Enumerate schema and scan tables directly (primary API)
2. Optionally: use a minimal expression evaluator for row filtering (v0.3+)

For full SQL, users should use `Microsoft.Data.Sqlite` or `SQLitePCLRaw`.
Sharc complements these by offering zero-dependency, encryption-aware, in-memory reading.
