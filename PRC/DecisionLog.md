# Decision Log — Sharc

Architecture Decision Records (ADRs) documenting key choices. Newest first.

---

## ADR-018: O(K²) → O(K) Column Offset Precomputation

**Date**: 2026-02-18
**Status**: Accepted
**Context**: Arena WASM query benchmarks showed Sharc losing 10 of 13 queries to SQLite. Profiling revealed the root cause: every column access in `SharcDataReader` called `RecordDecoder.ComputeColumnOffset()`, which iterated serial types from index 0 to the target column — an O(K) loop per column, yielding O(K²) per row. For the Arena's 9-column `users` table, this meant 36 `GetContentSize` calls per row instead of 9. Combined with single-iteration benchmarks susceptible to WASM JIT noise, the results were unreliable.

**Decision**: Two fixes:

1. **Column offset precomputation**: Added `ComputeColumnOffsets()` to `IRecordDecoder` — a single O(K) pass that fills an `ArrayPool<int>`-rented `_columnOffsets` buffer once per row in `DecodeCurrentRow()`. Added O(1) offset-aware decode methods (`DecodeColumnAt`, `DecodeInt64At`, `DecodeDoubleAt`, `DecodeStringAt`) that bypass the per-column loop. All hot paths in `SharcDataReader` (`GetColumnValue`, `GetInt64`, `GetDouble`, `GetString`, `GetGuid`, `GetColumnFingerprint`, `GetCursorRowFingerprint`) updated to use precomputed offsets.

2. **Benchmark methodology**: Updated `QueryPipelineEngine` from 1 warmup + 1 measured to 3 warmups + 5 interleaved measured iterations with median selection. Interleaving levels GC pressure between Sharc and SQLite; median resists JIT/GC spikes.

**Files modified**: `IRecordDecoder.cs` (5 new methods), `RecordDecoder.cs` (5 implementations), `SharcDataReader.cs` (precompute + all access paths), `QueryPipelineEngine.cs` (benchmark methodology), `WithoutRowIdCursorAdapterTests.cs` and `SchemaReaderTests.cs` (test stub updates).

**Results**:

- Column decode cost: O(K²) → O(K) per row (4x fewer `GetContentSize` calls for 9-column table)
- Benchmark noise: eliminated single-sample WASM JIT variance
- All 2,038 tests pass (0 warnings, 0 errors)

**Consequences**:

- `_columnOffsets` adds one `ArrayPool<int>` rent/return per reader lifetime — negligible vs the O(K²) savings
- Old `ComputeColumnOffset` and `Decode*Direct` methods retained for backward compatibility but no longer on hot paths
- Fixed latent `_physicalColumnCount` bug: columns after merged GUID pairs (physical ordinals > merged pair indices) were not counted, causing uninitialized offsets

---

## ADR-017: Write Architecture Technical Debt Reduction

**Date**: 2026-02-18
**Status**: Accepted — Amends ADR-016
**Context**: ADR-016 introduced 4 tiers of allocation optimization across 6 files. The changes achieved excellent benchmark results but introduced complexity debt: a correctness bug in BTreeMutator pooling, signature bloat in BuildLeafPage/BuildInteriorPage (7–8 parameters), code duplication in Reset/Dispose/ClearShadow, mixed paradigms (DefragmentPage allocated a local heap array while neighboring methods used pooled `_cellRefBuffer`), and branching complexity in Transaction for `_ownsResources`.

**Decision**: Six targeted fixes:

1. **Stop pooling BTreeMutator across transactions** (correctness fix): BTreeMutator holds `_freePageAllocator`/`_freePageCallback` delegates bound to a `FreelistManager` from a specific transaction. When pooled, stale delegates could reuse already-committed pages. Fix: only pool `ShadowPageSource`; create a fresh `BTreeMutator` per transaction. Renamed `_ownsResources` → `_ownsShadow`, removed `_cachedMutator` from `SharcWriter`, renamed `CapturePooledResources` → `CapturePooledShadow`.

2. **Simplify BuildLeafPage/BuildInteriorPage signatures**: Replaced `CellRef[] refs, int refCount, int from, int to` (4 params for a slice) with `ReadOnlySpan<CellRef> cells` (1 param). BuildLeafPage 7→4 params, BuildInteriorPage 8→5 params.

3. **GatherCellsWithInsertion return type cleanup**: Changed from `(byte[] Buffer, CellRef[] Refs, int RefCount)` to `(byte[] cellBuf, int refCount)`. Caller reads `_cellRefBuffer[..refCount]` directly.

4. **Extract helpers to eliminate duplication**: `ReturnRentedBuffers()` shared by Reset/Dispose, `EnsureCellRefCapacity(int)` shared by 3 cell-gathering methods. Also fixed `Reset()` missing `_nextAllocPage = 0`.

5. **Unify DefragmentPage**: Uses `EnsureCellRefCapacity()` + `_cellRefBuffer` instead of allocating a local `(int, int)[]` on the heap.

6. **Extract ClearInternal() in ShadowPageSource**: Shared by `ClearShadow()` and `Reset()` — eliminates 3 duplicated lines.

**Files modified**: `BTreeMutator.cs` (Fixes 2–5), `Transaction.cs` (Fix 1), `SharcWriter.cs` (Fix 1), `SharcDatabase.cs` (Fix 1), `ShadowPageSource.cs` (Fix 6).

**Results**:

- Batch 100 UPDATE allocation: 105.39 KB → **34.02 KB** (68% reduction, now matches SQLite's 32.68 KB)
- Single INSERT allocation: +3.6 KB (expected cost of Fix 1 correctness fix — fresh BTreeMutator per transaction)
- All other benchmark numbers unchanged (deterministic allocation counts identical)
- All 2,038 tests pass

**Consequences**:

- BTreeMutator pooling removed — eliminates stale FreelistManager delegate risk entirely
- BuildLeafPage/BuildInteriorPage signatures are now idiomatic (span slicing at call site)
- Zero code duplication in Reset/Dispose/ClearShadow paths
- DefragmentPage consistent with all other cell-gathering methods
- Transaction lifecycle simplified: mutator always owned, only shadow has conditional pooling

---

## ADR-016: Zero-Alloc Write Architecture

**Date**: 2026-02-17
**Status**: Accepted
**Context**: After ADR-015 (demand-driven page cache), individual write operations still allocated 8–18× more than SQLite. Every auto-commit operation created a fresh Transaction → ShadowPageSource → BTreeMutator → FreelistManager chain. Each of these allocated Dictionary/List instances used once and discarded. Additionally, `FindTableRootPage` scanned sqlite_master on every operation, and `BTreeMutator.Insert()` allocated a new `List<(uint, int)>` for the path on every call.

**Decision**: Four layered strategies, each independently shippable:

1. **Table Root Cache**: `SharcWriter` maintains `Dictionary<string, uint>` populated on first access per table, invalidated on root split. Eliminates ~700 B per operation from sqlite_master re-scan.

2. **ShadowPageSource Pooling with Reset()**: `ShadowPageSource.Reset()` clears data but preserves Dictionary/List internal capacity. SharcWriter caches the shadow across auto-commit operations. Transaction distinguishes owned vs pooled shadow via `_ownsShadow` flag. (Note: BTreeMutator pooling was removed in ADR-017 due to stale delegate correctness bug.)

3. **Stack-Based Path Tracking**: `BTreeMutator.Insert()` uses `stackalloc (uint, int)[20]` instead of `new List<...>()`. Cell ref arrays use a pooled `CellRef[]` field that grows via `EnsureCellRefCapacity()`.

4. **Page Buffer Arena**: `PageArena` — contiguous ArrayPool buffer sub-divided by bump pointer. `ShadowPageSource` stores `Dictionary<uint, int>` (slot indices) instead of `Dictionary<uint, byte[]>`. One Rent/Return per transaction instead of N.

All APIs are .NET 10 BCL, cross-platform. No NativeMemory, no unsafe, no P/Invoke.

**Rationale**:

- Anti-fragmentation principle: prefer contiguous reusable buffers over many small short-lived objects
- Reset over dispose+new: Dictionary.Clear() preserves internal bucket arrays (~200 B each)
- stackalloc for bounded paths: B-tree depth ≤ 20, no heap allocation needed
- Arena for dirty pages: sequential memory layout improves cache behavior during commit flush

**Results** (batch/transactional operations, where pooling is active — updated after ADR-017 debt reduction):

| Operation | Before (ADR-015) | After (ADR-016+017) | Reduction | vs SQLite |
| :--- | ---: | ---: | ---: | ---: |
| Batch 100 DELETEs | 201 KB | **17.27 KB** | **11.6×** | Sharc beats SQLite (32.62 KB) |
| Batch 100 UPDATEs | 290 KB | **34.02 KB** | **8.5×** | Sharc beats SQLite (32.68 KB) |
| Transaction 100 INSERTs | 108.7 KB | **22.78 KB** | **4.8×** | Sharc beats SQLite (71.23 KB) |
| Batch 1K INSERTs | 940 KB | **43.32 KB** | **21.7×** | Sharc beats SQLite (605.56 KB) |

**Consequences**:

- Batch 100 DELETEs now allocate less than SQLite (17 KB vs 33 KB)
- Batch 100 UPDATEs dropped from 105 KB to 34 KB after ADR-017 (now matches SQLite)
- Transaction 100 INSERTs allocate 3.1× less than SQLite
- Single INSERT allocation increased ~3.6 KB (expected cost of ADR-017 correctness fix)
- Single-operation cold-start numbers unchanged (dominated by database open/schema parse)
- 7 new PageArena tests, 9 updated pooling tests; all 2,038 tests pass

---

## ADR-015: Demand-Driven Page Cache Allocation

**Date**: 2026-02-17
**Status**: Accepted
**Context**: `SharcWriter.Open()` eagerly allocated ~8 MB (2000 × 4096-byte buffers from ArrayPool) at construction. A single-row DELETE benchmarked at 6,113 KB allocated — nearly all from this upfront cache reservation. The write path only touches 3–10 pages and already has two independent caches (BTreeMutator._pageCache, ShadowPageSource._dirtyPages) that make the 2000-page LRU largely redundant for writes.

**Decision**: Two changes:

1. **CachedPageSource**: demand-driven buffer allocation — capacity is a maximum, not a reservation. Slot metadata is pre-allocated (cheap); byte[] page buffers are rented from ArrayPool on first use in AllocateSlot().
2. **SharcWriter.Open()**: pass `PageCacheSize = 16` instead of inheriting the default 2000. The LRU only serves cross-transaction reads (schema + root pages); BTreeMutator._pageCache handles all intra-transaction page caching.

**Rationale**:

- Follows the universal pattern in modern embedded databases (SQLite pcache1, RocksDB block cache, DuckDB buffer manager): cache capacity = ceiling, not reservation
- Three-layer write cache redundancy means the LRU is exercised once per page per transaction, then bypassed
- 16 pages × 4096 = 64 KB maximum for writes vs 8 MB before — 125× reduction
- Read path benefits too: a scan touching 500 of 6,700 pages allocates 500 buffers, not 2000

**Alternatives considered**:

- Lazy allocation (delay same N rentals): changes timing but not the conceptual model; doesn't right-size the write default
- Remove CachedPageSource from write path entirely: too aggressive — schema/root page caching across transactions is still valuable
- Separate write-only page source: unnecessary complexity when demand-driven + right-sized default achieves the same result

**Consequences**:

- SharcWriter.Open() allocation drops from ~8 MB to ~24 KB (metadata only)
- Single-row DELETE allocation drops from 6,113 KB to < 200 KB
- Read path unchanged in behavior — pays only for pages actually accessed
- Write speed unchanged (same execution path post-construction)

Full design: [docs/WriteMemPageAlloc.md](../docs/WriteMemPageAlloc.md)

---

## ADR-014: Benchmark Execution via MCP Service

**Date**: 2026-02-12
**Status**: Accepted
**Context**: Benchmarks were previously run ad-hoc via direct `dotnet run` commands, leading to inconsistent parameters (missing `--memory`, wrong job type), concurrent run conflicts (BenchmarkDotNet file locks), and no structured way to retrieve or compare results. As the project grows with both Core and Graph benchmark suites, a consistent execution policy is needed.

**Decision**: All benchmarks — both standard (Sharc vs SQLite) and Graph-related (Sharc.Comparisons) — must be run through the MCP Service (`tools/Sharc.Context`).

**Execution Policy**:

1. **Standard comparative benchmarks**: `RunBenchmarks(filter="*Comparative*", job="short")` — runs all Sharc vs SQLite comparisons across 6 categories (TableScan, TypeDecode, RealisticWorkload, GcPressure, SchemaMetadata, DatabaseOpen)
2. **Graph benchmarks**: `RunBenchmarks(filter="*Graph*", job="short")` — runs graph storage and traversal benchmarks in Sharc.Comparisons
3. **Targeted runs**: Use specific filters like `RunBenchmarks(filter="*TableScan*")` for focused analysis
4. **Results retrieval**: `ReadBenchmarkResults(className)` or `ListBenchmarkResults()` for structured markdown output
5. **Sequential execution only**: Never run multiple benchmark processes concurrently — BenchmarkDotNet uses shared artifact files that cause lock conflicts

**MCP Tool Parameters**:

| Parameter  | Default          | Purpose                                                          |
| ---------- | ---------------- | ---------------------------------------------------------------- |
| `filter`   | `*Comparative*`  | BDN filter pattern                                               |
| `job`      | `short`          | `short` (3 iter, fast), `medium` (standard), `dry` (validate)    |
| `--memory` | always           | Memory diagnoser for allocation tracking                         |

**Rationale**:

- **Consistency**: MCP Service always adds `--memory` flag, uses Release config, and applies correct timeout (30 min)
- **Streaming**: Incremental output avoids the "black box" problem of long-running benchmarks
- **Structured results**: Markdown reports in `BenchmarkDotNet.Artifacts/results/` enable automated README updates
- **Sequential enforcement**: Single process model prevents the file-locking conflicts observed during concurrent runs
- **Unified interface**: Same invocation pattern works for both Core benchmarks and Graph comparisons
- **Discoverability**: `ListBenchmarkResults()` makes it trivial to find and compare past runs

**Alternatives Considered**:

1. Direct `dotnet run` commands — rejected: no parameter consistency, easy to forget `--memory` or use Debug config
2. Shell scripts — rejected: platform-specific, no streaming, no structured result access
3. CI-only benchmarks — rejected: too slow for iterative development; local runs needed for optimization work

**Consequences**:

- All benchmark documentation references MCP Service commands
- Graph benchmarks (Sharc.Comparisons) to be expanded with actual BenchmarkDotNet classes
- README benchmark tables updated from MCP `ReadBenchmarkResults()` output
- `CLAUDE.md` updated with MCP benchmark invocation examples

---

## ADR-013: Test Assertion Conventions

**Date**: 2026-02-12
**Status**: Accepted
**Context**: Need consistent assertion patterns across all test files.

**Decision**: Use plain xUnit `Assert` methods exclusively. No third-party assertion libraries.

**Rules**:

- `Assert.Equal(expected, actual)` for value equality
- `Assert.True(condition)` / `Assert.False(condition)` for boolean checks
- `Assert.Null(value)` / `Assert.NotNull(value)` for null checks
- `Assert.Throws<T>(() => ...)` for expected exceptions (returns the exception for further inspection)
- `Assert.IsType<T>(value)` for type checks
- `Assert.Contains(item, collection)` / `Assert.Empty(collection)` for collections
- `Assert.InRange(actual, low, high)` for numeric bounds
- For floating-point: `Assert.Equal(expected, actual, precision)` where precision is decimal places

**Rationale**:

- xUnit `Assert` is included with xUnit — zero additional dependencies
- One fewer NuGet package reduces supply chain surface and version churn
- Plain assertions are unambiguous — no fluent chains to misread
- Consistent with the project's minimal-dependency philosophy (ADR-003, DependencyPolicy)

**Consequences**:

- Remove `FluentAssertions` from `Sharc.Tests.csproj` and `Sharc.IntegrationTests.csproj`
- Migrate all existing `.Should()` calls to `Assert.*`
- All new tests use `Assert.*` only

---

## ADR-012: Drop FluentAssertions Dependency

**Date**: 2026-02-12
**Status**: Accepted — Supersedes ADR-010 (test framework selection)
**Context**: ADR-010 selected FluentAssertions for readable assertion syntax. In practice, it adds a dependency without sufficient benefit for a library project.

**Decision**: Remove `FluentAssertions` from all test projects. Use xUnit's built-in `Assert` class.

**Rationale**:

- FluentAssertions is a transitive dependency chain (FluentAssertions → System.Configuration.ConfigurationManager → etc.)
- xUnit's `Assert` provides every assertion we need
- Eliminates version pinning burden for a test-only package
- Aligns with DependencyPolicy: "Is there a built-in alternative? → Use it"

**Alternatives considered**:

1. Keep FluentAssertions — rejected: unnecessary dependency
2. Switch to Shouldly — rejected: same problem, different package

**Consequences**:

- All 11 existing test files need migration from `.Should().Be()` to `Assert.Equal()`
- ADR-010 updated: xUnit remains; FluentAssertions removed

---

## ADR-011: Memory-Mapped Files for File-Backed Page Access

**Date**: 2026-02-11
**Status**: Accepted
**Context**: The original DependencyPolicy deferred memory-mapped files ("initially"), defaulting to `FileStream` / `RandomAccess`. Benchmarks showed `File.ReadAllBytes` costs ~77 µs for file open vs SQLite's ~160 ns lazy open — a 480x penalty caused entirely by upfront I/O, not by Sharc's parsing.

**Decision**: Add `MemoryMappedPageSource` as the default file-backed `IPageSource` implementation.

**Design**: Uses the BCL `MemoryManager<byte>` pattern (same as ASP.NET Core / Kestrel) to expose the OS-mapped region as safe `Memory<byte>` / `Span<byte>`. A single `unsafe` class (`UnmanagedMemoryManager`, ~15 lines) acquires the pointer; all downstream code is fully safe.

```text
UnmanagedMemoryManager      ← only unsafe code (BCL MemoryManager<byte> pattern)
    ↓ wraps pointer
MemoryMappedPageSource      ← safe class, uses Memory<byte>.Span
    ↓ implements
IPageSource                 ← safe interface, consumers unaware of backing
```

**Rationale**:

- File open becomes near-instant (OS creates mapping, no data read)
- Only accessed pages fault into physical memory (lazy loading, like SQLite)
- Zero managed heap pressure for file data (lives in OS virtual memory)
- `GetPage(uint)` returns a span slice into mapped memory — zero-copy, zero-alloc
- Files up to 2 GiB supported (Span length limit); larger files fall back to streaming
- The `unsafe` is minimal, well-isolated, and uses a standard BCL abstraction

**Alternatives Considered**:

1. `File.ReadAllBytes` → `MemoryPageSource` — simple but 77 µs open cost, doubles memory usage
2. `FileStream` with per-page `Read` — syscall per page, no zero-copy
3. `RandomAccess.Read` — modern API but still a syscall per read, no memory mapping

**Consequences**:

- `AllowUnsafeBlocks` already enabled in Sharc.Core.csproj
- DependencyPolicy updated: memory-mapped files now approved for file-backed access
- Three `IPageSource` implementations: `MemoryPageSource` (in-memory), `MemoryMappedPageSource` (mmap), `CachedPageSource` (LRU wrapper, future)

---

## ADR-010: Test Framework Selection

**Date**: v0.1 initial
**Status**: Amended by ADR-012, ADR-013
**Context**: Need a test framework for TDD development.

**Decision**: xUnit with built-in `Assert`. ~~FluentAssertions~~ removed per ADR-012.

**Rationale**:

- xUnit is the most widely used .NET test framework with excellent parallel execution
- `[Theory]` + `[InlineData]` is ideal for parameterized primitive tests (varints, serial types)
- ~~FluentAssertions provides readable assertion syntax~~ — replaced by plain `Assert` (ADR-012)

**Alternatives Rejected**:

- NUnit: viable but less common in modern .NET; `[TestCase]` is equivalent to `[InlineData]`
- MSTest: too verbose for rapid TDD cycles
- Shouldly: less feature-rich, same dependency problem

---

## ADR-009: Encryption Header Size (128 bytes)

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Sharc-encrypted files need a header to identify encryption parameters.

**Decision**: 128-byte fixed header prepended to the encrypted database.

**Rationale**:
- 128 bytes is large enough for: magic (8), KDF params (16), salt (32), verification (32), metadata (8), reserved (32)
- Power-of-2 alignment simplifies page offset calculations
- Reserved space allows future fields without format version bump
- Small enough to read in a single I/O operation

**Consequences**:
- Encrypted page offsets are shifted by 128 bytes: `page_offset = 128 + (pageNumber - 1) * encrypted_page_size`
- Detection: check first 6 bytes for `SHARC\x00` before checking for SQLite magic

---

## ADR-008: AES-256-GCM as Default Cipher

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Need AEAD cipher for page-level encryption.

**Decision**: AES-256-GCM as default, XChaCha20-Poly1305 as future option.

**Rationale**:
- AES-256-GCM is available in `System.Security.Cryptography` — zero external dependencies
- Hardware-accelerated (AES-NI) on all modern x86 and ARM processors
- Well-understood security properties; NIST-approved
- 12-byte nonce is adequate for deterministic page-number-derived nonces
- XChaCha20-Poly1305 deferred because it requires libsodium or custom implementation

**Trade-offs**:
- AES-GCM's 12-byte nonce limits to ~2^32 encryptions with same key before nonce reuse risk
  - For read-only use, this is irrelevant (nonces are derived, not random)
  - For future write support, nonce derivation scheme handles this

---

## ADR-007: Reserved Serial Types 10,11 Treated as Errors

**Date**: v0.1 initial
**Status**: Accepted
**Context**: SQLite serial types 10 and 11 are "reserved for future use."

**Decision**: Throw `ArgumentOutOfRangeException` when encountering types 10 or 11.

**Alternatives considered**:
1. Treat as NULL (silent degradation)
2. Throw exception (fail fast)
3. Return a special "unknown" storage class

**Rationale**: These types indicate a database created by a future SQLite version with features Sharc can't interpret. Silent degradation would produce incorrect results. Failing fast with a clear error lets the consumer know their database needs a newer reader.

---

## ADR-006: ColumnValue as readonly struct (Not class, not record)

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Need to represent decoded column values with minimal allocation.

**Decision**: `ColumnValue` is a `readonly struct` with inline storage for int/float and `ReadOnlyMemory<byte>` for text/blob.

**Rationale**:
- Created per-column per-row — must be stack-allocatable
- Integer and float values stored directly in struct fields (0 allocations)
- Text and blob reference the underlying page buffer via `ReadOnlyMemory<byte>` (0 copy)
- `AsString()` allocates a `string` only when called — lazy materialization
- Struct size is ~40 bytes (long + double + ReadOnlyMemory<byte> + long serialType + enum) — within struct optimization threshold

**Consequences**:
- `ColumnValue[]` for decoded records does allocate the array, but column values themselves are inline
- For future optimization: return `Span<ColumnValue>` from stack-allocated buffer

---

## ADR-005: No IDataReader Implementation

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Should `SharcDataReader` implement `System.Data.IDataReader`?

**Decision**: No. `SharcDataReader` has a similar method surface but does not implement `IDataReader`.

**Rationale**:
- `IDataReader` requires: `GetSchemaTable()`, `NextResult()`, `Depth`, `RecordsAffected`, `IsClosed`, `GetOrdinal()`, `GetFieldType()`, and 20+ other members
- Most are meaningless for a file-format reader (no result sets, no affected rows, no schema table)
- Implementing dead methods clutters the API and confuses consumers
- If ADO.NET compat is needed later, a wrapper adapter is trivial to add

---

## ADR-004: Static Factory Methods over Constructors

**Date**: v0.1 initial
**Status**: Accepted
**Context**: How should consumers create `SharcDatabase` instances?

**Decision**: `SharcDatabase.Open()` and `SharcDatabase.OpenMemory()` static factory methods.

**Rationale**:
- Construction involves I/O (file open) and validation (header parse) — too much for a constructor
- Two distinct creation modes (file vs memory) map naturally to named factory methods
- Factory methods can evolve to return different internal implementations without API breakage
- `Open` is a familiar verb (cf. `File.Open`, `SqlConnection.Open`)

---

## ADR-003: Pure Managed Reader (Option A)

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Three strategies evaluated for reading SQLite files.

**Decision**: Option A — Pure managed C# reader, no native SQLite, no P/Invoke.

**Full rationale**: See `PRC/StrategyDecision.md`

**Summary**: Zero native dependencies enables cross-platform deployment, trivial in-memory support, clean encryption integration, and full testability. Performance target of 1.5–3× native is acceptable.

---

## ADR-002: Read-Only First

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Should Sharc support writes from the start?

**Decision**: Read-only until Milestone 10 (benchmarks complete). No writes, no WAL writes, no VACUUM.

**Rationale**:
- Write support requires transaction logic, journal management, crash recovery, locking protocol
- This triples the scope and risk without delivering the primary use case (fast reads)
- Read-only code is simpler to verify, test, and optimize
- Write support can be added incrementally via `IPageSource` write extensions

---

## ADR-001: Argon2id as Default KDF

**Date**: v0.1 initial
**Status**: Accepted
**Context**: Need password-based key derivation for encryption.

**Decision**: Argon2id with defaults: 3 iterations, 64 MiB memory, parallelism 4.

**Rationale**:
- Argon2id won the Password Hashing Competition (2015)
- Provides both side-channel resistance (Argon2i) and GPU resistance (Argon2d)
- Memory-hard: 64 MiB per derivation makes GPU/ASIC attacks expensive
- Standardized in RFC 9106
- scrypt is a reasonable alternative but Argon2id is strictly newer and more configurable

**Implementation note**: Will need `Konscious.Security.Cryptography` NuGet package or custom implementation, as .NET does not include Argon2 natively.

---

## Template for New Decisions

```
## ADR-NNN: [Title]

**Date**: [date]
**Status**: Proposed | Accepted | Deprecated | Superseded by ADR-NNN
**Context**: [What is the issue?]

**Decision**: [What was decided]

**Rationale**: [Why this choice]

**Alternatives considered**: [What else was evaluated]

**Consequences**: [What changes as a result]
```
