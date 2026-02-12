# Decision Log — Sharc

Architecture Decision Records (ADRs) documenting key choices. Newest first.

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

**Full rationale**: See `ProductContext/StrategyDecision.md`

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
