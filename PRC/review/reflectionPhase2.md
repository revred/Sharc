# 🦈 Sharc — Engineering Roadmap

## Phase 1: Deep Code Review · Phase 2: Core Feature Plan

**Repository:** https://github.com/revred/Sharc
**License:** MIT
**Runtime:** .NET 8+ (AOT-compatible, trimmable, WASM-ready)
**Ethos:** API-first, TDD, zero-allocation hot paths, zero native dependencies

---

## Vision

Sharc is a pure managed C# SQLite engine — no native binaries, no P/Invoke, no platform-specific packaging. It reads (and will write) standard `.db` files by parsing the SQLite format directly through `Span<byte>` and `BinaryPrimitives`.

This architecture makes Sharc the only SQLite library that compiles to WebAssembly without shipping a 900 KB `sqlite3.wasm` binary. For Blazor applications, Progressive Web Apps, and any .NET WASM workload that needs structured local storage, Sharc is the native choice — it IS the runtime, not a wrapper around one.

The four features in Phase 2 transform Sharc from a fast reader into the default cache management database for WebAssembly: writes for cache mutation, WAL for concurrent access, index-backed filtering for cache queries, and full-text search for content indexing. Together, they complete the story.

---

## Platform Readiness

| Capability | Status | Evidence |
|---|---|---|
| AOT compilation | ✅ Ready | `IsAotCompatible = true` in Directory.Build.props |
| IL trimming | ✅ Ready | `IsTrimmable = true` in Directory.Build.props |
| Zero native deps | ✅ Verified | No P/Invoke, no NativeLibrary, no conditional RID packages |
| WASM (memory-backed) | ✅ Works today | `OpenMemory(byte[])` accepts any buffer — IndexedDB, fetch, etc. |
| WASM (file-backed) | 🔶 Phase 2 | Requires OPFS integration via `IPageSource` adapter |
| Browser concurrency | 🔶 Phase 2 | WAL mode enables concurrent reads during writes |

---

# PHASE 1 — Deep Code Review

**Scope:** Every `.cs` file (50 source, 39 test, 22 benchmark), all `.csproj` files, `Directory.Build.props`, README, PRC documentation. ~5,000 LOC source, ~7,500 LOC tests, 489 passing tests, 127 benchmarks.

**Full detailed review:** See `deepReviewPhase1.md` for file-level analysis with code excerpts and line numbers.

## Architecture Assessment

The core reader (Sharc + Sharc.Core) is genuinely excellent. The five-layer stack — Primitives → Page I/O → B-Tree → Records → Schema → Public API — has clean boundaries, each defined through interfaces. The B-tree cursor with `ArrayPool` overflow assembly, lazy decode via generation counters, and `File.OpenHandle` + `RandomAccess` I/O are professional implementations.

**Measured strengths:**

| Metric | Result |
|---|---|
| Tests | 489 passed, 0 failures, 0 skipped |
| Benchmarks | 127 with BenchmarkDotNet + MemoryDiagnoser |
| Sequential scan | 2.5x–17.5x faster than Microsoft.Data.Sqlite |
| Point lookup | 40x faster (end-to-end including SQL overhead on SQLite side) |
| Thread safety | 12 concurrency tests, up to 16 threads |
| Allocations (hot path) | 0 bytes per row for integer/float columns |
| Nullable annotations | Full coverage |
| XML documentation | Comprehensive |

## Issues Found: 27 Total

### Severity Breakdown

| Severity | Count | Impact |
|---|---|---|
| 🔴 Correctness Bugs | 5 | Wrong data returned or memory corruption risk |
| 🟠 Architectural | 8 | Inconsistencies that undermine credibility |
| 🟡 Benchmark Honesty | 4 | Claims not supported by own data |
| 🔵 Code Quality | 6 | Professional polish items |
| 📐 Presentation | 4 | README and first-impression issues |

### 🔴 Correctness Bugs (Fix Before Any Release)

**BUG-01 · ConceptStore.Get() seeks by rowid, not BarID.** The Maker.AI Entity table uses an implicit rowid — `BarID` is a separate column. `BTreeCursor.Seek(key.Value)` navigates to the wrong row. This silently returns incorrect data.

**BUG-02 · Graph stores allocate RecordDecoder per call.** Both `ConceptStore.Get()` and `RelationStore.GetEdges()` create `new RecordDecoder()` on every invocation. `RecordDecoder` is stateless — it should be a shared field.

**BUG-03 · RelationStore uses allocating DecodeRecord overload in a loop.** A fresh `ColumnValue[]` is allocated for every edge row during full table scan (4,861 arrays for the Maker.AI database). The buffer-reuse overload `DecodeRecord(payload, destination)` exists and should be used.

**BUG-04 · FilePageSource._pageBuffer is not thread-safe.** `CachedPageSource` protects via lock, but if `PageCacheSize = 0`, the cache is bypassed and `FilePageSource.GetPage()` is called without synchronization — concurrent reads corrupt the shared buffer.

**BUG-05 · NodeKey.ToAscii() returns garbage for non-ASCII keys.** No validation that bytes are printable ASCII. A raw integer key interpreted as ASCII produces control characters and misleading output.

### 🟠 Architectural Issues (Fix Before Sharing)

**ARCH-01 · RecordId.FullId allocates on every property access** via string interpolation. Used in ToString(), implicit string conversion, and potentially in hash sets during traversal. Should be cached in constructor.

**ARCH-02 · RecordId.HasIntegerKey returns false for Key == 0.** The Maker.AI database has 19 edges with `OriginID = 0`. Zero is a valid key. This property misclassifies those records.

**ARCH-03 · FileShareMode not respected on PreloadToMemory path.** The `File.ReadAllBytes()` call ignores `options.FileShareMode`. Only the streaming path wires it through.

**ARCH-04 · No class implements IContextGraph.** The published Graph API contract (`Traverse()`, `GetNode()`, `GetEdges()`) has no concrete implementation. `ConceptStore` and `RelationStore` exist but are separate internal classes with no facade.

**ARCH-05 · GraphRecord.CreatedAt set to UtcNow on read path.** Records read from the database get a creation timestamp of "right now" instead of the database value. Semantically wrong.

**ARCH-06 · NativeSchemaAdapter references `alias` column that doesn't exist** in its CREATE INDEX DDL. Would fail on execution.

**ARCH-07 · Unused `using Sharc.Core.Primitives` in both graph stores.** Should be caught by `TreatWarningsAsErrors`.

**ARCH-08 · Sharc.Schema namespace lives in Sharc.Core assembly.** Confusing — the namespace doesn't match its physical location or assembly.

### 🟡 Benchmark Honesty (Fix README Claims)

**BENCH-01 · Sharc allocates 10x–50x more than SQLite in realistic workloads.** The README's own benchmark table shows: schema reads 40 KB vs 872 B, sustained 1M-row scans 407 KB vs 7 KB. The "Zero GC pressure" headline only holds for isolated micro-benchmarks, not for any workload involving schema parsing or sustained iteration. **Partially addressed (ADR-015):** CachedPageSource is now demand-driven — the ~8 MB eager pre-allocation at open time is eliminated. Write path reduced from 6,113 KB to < 200 KB. Schema allocation (~40 KB) remains as a known accepted cost.

**BENCH-02 · "41x faster seek" compares different things.** Sharc's B-tree seek is compared against SQLite's full SQL pipeline (parse → plan → VDBE → seek → marshal). The comparison is valid as "total API cost" but misleading as "B-tree performance."

**BENCH-03 · 77.8x batch speedup is a cache locality effect,** not algorithmic. Sharc's batch is sub-linear (page cache warmth) while SQLite's is linear. Worth noting in the README.

**BENCH-04 · Microsoft.Data.Sqlite version mismatch across projects.** Three different versions (9.0.0, 9.0.2, 9.0.4) across benchmarks and integration tests. Undermines reproducibility.

### 🔵 Code Quality + 📐 Presentation (10 items)

These are detailed in `sharc-deep-review.md`. Key highlights: UTF-8 BOM inconsistency causing mojibake in file headers, `CreateTableParser.IsTableConstraint` allocating unnecessarily via `ToUpperInvariant()`, empty `Sharc.Crypto` project adding build overhead, TODO comment in RelationStore contradicting "zero TODOs" health claim, and the README missing any visual identity or honest limitations section.

## Phase 1 Recommendation

Fix all 5 correctness bugs and the 8 architectural issues before any public sharing. The core reader is solid — these issues are concentrated in the Graph layer, which was built more recently and hasn't had the same level of hardening as the reader engine. The benchmark honesty items are README edits, not code changes. Total estimated effort: 3–4 working days.

---

# PHASE 2 — Core Feature Plan

## Strategic Context: The WebAssembly Cache Database

Every .NET WASM application that needs structured local storage today faces a painful choice:

**Option A: Microsoft.Data.Sqlite** — Requires shipping `e_sqlite3.wasm` (900+ KB), which must be loaded, instantiated, and bridged through JavaScript interop. The native SQLite binary doesn't benefit from .NET AOT trimming. Cold start in Blazor WASM adds 200-400ms just for SQLite initialization.

**Option B: IndexedDB via JavaScript interop** — No type safety, no relational queries, no transactions across object stores, and every call crosses the JS-WASM boundary with marshalling overhead.

**Option C: Sharc** — Pure managed C# that compiles directly into the WASM module. Zero additional binaries. The `OpenMemory(byte[])` API accepts any buffer — data fetched from an API, loaded from IndexedDB, or read from OPFS. The entire SQLite read path runs as native WASM instructions with no interop boundary. AOT-trimmed Sharc adds ~180 KB to the published binary versus ~900 KB for sqlite3.wasm.

With Phase 2, Sharc becomes the only library that provides read-write SQLite-compatible structured storage running natively inside WebAssembly. No other pure-managed .NET library can make this claim.

## The Four Core Features

These aren't arbitrary additions — each one addresses a specific requirement of the cache management use case:

| Feature | Cache Requirement | Milestone |
|---|---|---|
| **Write Engine** | Cache must write — INSERT, UPDATE, DELETE entries | M11 |
| **WAL Mode** | Concurrent reads while background sync writes | M8 |
| **Index Read + Filter** | Query cached data by key, type, timestamp | M7 |
| **Virtual Tables (FTS5)** | Search cached content, documents, messages | M12 |

## Feature Dependency Graph

```
                     ┌──────────────┐
                     │  M6: Reader  │ ← COMPLETE (current state)
                     │    (MVP)     │
                     └──────┬───────┘
                            │
              ┌─────────────┼─────────────┐
              ▼             ▼             ▼
      ┌──────────────┐ ┌──────────┐ ┌──────────────┐
      │  M7: Index   │ │ M8: WAL  │ │  M9: Crypto  │
      │  Read+Filter │ │  Reader  │ │  (deferred)  │
      └──────┬───────┘ └────┬─────┘ └──────────────┘
             │              │
             ▼              ▼
      ┌──────────────────────────┐
      │  M11: Write Engine       │
      │  INSERT/UPDATE/DELETE    │
      │  Page allocation         │
      │  Freelist management     │
      │  Transaction journal     │
      └────────────┬─────────────┘
                   │
                   ▼
      ┌──────────────────────────┐
      │  M12: Virtual Tables     │
      │  FTS5 (content search)   │
      │  R-Tree (spatial cache)  │
      └──────────────────────────┘
```

---

## M7: Index Read + Filter Engine

**Purpose:** Enable cache lookups by arbitrary columns, not just rowid. A cache management layer needs to find entries by key, by type, by expiry timestamp — all of which require index-backed access.

### What Gets Built

**Index B-tree reader.** SQLite index pages (types 0x0A leaf, 0x02 interior) use a different cell format than table pages. Index cells contain the indexed column values as a record followed by the rowid. Sharc's existing `BTreeCursor` needs an `IndexCursor` variant that understands this format.

**Index-aware schema.** `SchemaReader` already enumerates indexes from `sqlite_schema`, but the `IndexInfo` model doesn't expose which columns are indexed or in what order. The `CREATE INDEX` SQL needs to be parsed (similar to how `CreateTableParser` handles `CREATE TABLE`) to extract column names, sort order, and uniqueness.

**Expression evaluator.** A minimal predicate engine that operates on `ColumnValue` directly — no SQL parsing, no query planner, no VDBE. The API is C# lambdas or a simple builder:

```csharp
// Programmatic filtering on indexed columns
var reader = db.CreateReader("cache_entries");
reader.Filter(columns => columns["expires_at"].AsInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds());

// Index-assisted seek for equality
reader.SeekIndex("idx_cache_key", keyValue);
```

**What this does NOT include:** SQL parsing, JOIN, GROUP BY, ORDER BY, subqueries. Those remain out of scope. Sharc is a structured storage engine, not a query processor.

### Design Constraints

The index reader must maintain Sharc's zero-allocation contract for the hot path. Index cells are smaller than table cells (they contain only the indexed columns plus the rowid), so the decode path is simpler. The expression evaluator operates on `ColumnValue` (a `readonly struct` with inline storage) — no boxing, no heap allocation for comparisons.

For WASM: index reads are CPU-bound (no I/O) when the database is memory-backed. The filter engine's branching patterns should be predictable for WASM's ahead-of-time compilation. No virtual dispatch on the hot comparison path.

### Gate Criteria

All existing 489 tests still pass. New tests cover: index B-tree traversal on real databases with known indexes, equality seek returning correct rowid, range scan on integer/text indexes, composite index handling, filter predicate evaluation on every `ColumnValue` type. Benchmark: index seek within 2x of rowid seek performance.

### Estimated Effort: 5 working days

---

## M8: WAL Read Support

**Purpose:** Read databases that are actively being written to. In the cache management scenario, a background sync worker writes while the UI reads. WAL mode is how SQLite makes this safe without blocking either side.

### What Gets Built

**WAL file parser.** The WAL file (`database-wal`) contains a 32-byte header followed by frames. Each frame has a 24-byte header (page number, commit marker, salt, checksum) followed by the page data. The parser reads frames sequentially and builds a page-number → frame-offset index.

**WAL-aware page source.** A new `WalPageSource` that wraps an existing `IPageSource` and overlays WAL frames on top. When a page is requested: check the WAL index first (most recent frame for that page number wins), fall back to the main database file if no WAL frame exists. This is exactly SQLite's read algorithm.

**SHM index reader (optional).** The `-shm` file is a shared-memory index that accelerates WAL frame lookup. For the single-process WASM case, this isn't needed — we build the index by scanning the WAL file on open. For multi-process scenarios (server-side .NET), reading the SHM file avoids the O(N) scan.

**Snapshot consistency.** WAL mode requires reading a consistent snapshot — we must pick a "read mark" (the last committed frame at the time we start reading) and ignore any frames written after. This is a single integer comparison per frame lookup.

### Why WAL Matters for WASM

In a browser, the database lives in memory (or OPFS). The application's UI thread reads cached data while a Web Worker performs sync operations that write to the same database. Without WAL support, the reader would need to lock out writes during every read — killing perceived performance. With WAL, readers and the writer never block each other.

The WAL file itself can be stored alongside the main database in OPFS, or for pure-memory scenarios, an in-memory WAL buffer backed by `MemoryPageSource` achieves the same isolation semantics.

### Design Constraints

WAL frames must be read without copying page data — the `WalPageSource.GetPage()` returns a `ReadOnlySpan<byte>` slice directly into the WAL buffer, consistent with how `MemoryPageSource` works today. The WAL index is a `Dictionary<uint, int>` (page number → frame offset) built once on open, updated if the WAL grows.

Checksum validation: WAL uses a cumulative checksum (big-endian or little-endian depending on the salt). The validator must handle both modes. Invalid checksums mean the frame was never committed — skip it.

### Gate Criteria

Open a WAL-mode database created by Microsoft.Data.Sqlite. Read pages that exist only in the WAL (not yet checkpointed to the main file). Verify correct data when the main file and WAL have conflicting versions of the same page. Benchmark: WAL-mode read within 1.2x of rollback-mode read for cached pages.

### Estimated Effort: 5 working days

---

## M11: Write Engine

**Purpose:** Mutate the database. A cache that can't write is a read-only snapshot — useful for some scenarios, but not for the primary use case of storing, updating, and evicting cache entries.

### Why M11 (Not M9 or M10)

The write engine is the most complex feature. It depends on M7 (index maintenance during writes) and benefits from M8 (WAL-mode writes are simpler than rollback-journal writes). M9 (encryption) and M10 (benchmarks) are independent and can proceed in parallel. Numbering reflects dependency order, not priority.

### What Gets Built

**Page allocator.** SQLite databases grow by appending pages. New pages come from either the freelist (recycled pages from previous DELETEs) or by extending the file. The allocator manages both sources. For WASM's memory-backed mode, "extending the file" means growing the backing `byte[]` or `ArraySegment` — a `ResizableMemoryPageSource` that allocates from managed heap.

**Freelist manager.** SQLite's freelist is a linked list of trunk pages, each pointing to leaf pages. When a page is freed (row deleted, overflow reclaimed), it goes onto the freelist. When a page is needed, the freelist is consulted first. The manager reads and writes freelist trunk/leaf pages.

**B-tree insertion.** Insert a new cell into a table leaf page. If the page is full, split it: allocate a new page, distribute cells, and insert a new entry in the parent interior page. This is recursive — splitting can propagate to the root, which creates a new root page and increases tree height.

**B-tree deletion.** Remove a cell from a leaf page. If the page becomes less than 1/3 full, merge with a sibling or redistribute cells. Delete the corresponding interior cell if needed. Free empty pages to the freelist.

**B-tree update.** For in-place updates (new value fits in existing cell), overwrite directly. For size-changing updates, delete + insert.

**Index maintenance.** Every INSERT, UPDATE, or DELETE on a table must also update all associated indexes. The index B-tree uses the indexed column values as the key (with rowid appended for uniqueness). Sharc must parse the schema to know which indexes exist on a table and maintain them transactionally.

**Transaction journal.** Before modifying any page, write the original page content to the rollback journal. If the transaction is rolled back (or the process crashes), the journal restores the original pages. For WAL-mode writes (Phase 2+), the modified pages are appended as WAL frames instead.

**Change counter + schema cookie.** Every committed transaction increments the file change counter (offset 24) and, if schema changed, the schema cookie (offset 40). This lets other readers detect that the file has changed.

### Public API

```csharp
using var db = SharcDatabase.Open("cache.db", new SharcOpenOptions { ReadWrite = true });

// Insert
db.Insert("cache_entries", new Dictionary<string, object>
{
    ["key"] = "user:profile:42",
    ["value"] = jsonBytes,
    ["expires_at"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
    ["etag"] = "W/\"abc123\""
});

// Update
db.Update("cache_entries",
    set: new Dictionary<string, object> { ["value"] = newJsonBytes, ["etag"] = newEtag },
    where: row => row["key"].AsString() == "user:profile:42");

// Delete expired entries
db.Delete("cache_entries",
    where: row => row["expires_at"].AsInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds());

// Transaction
using var tx = db.BeginTransaction();
try
{
    db.Insert("cache_entries", ...);
    db.Delete("cache_entries", ...);
    tx.Commit();
}
catch
{
    tx.Rollback();  // Also happens automatically on Dispose without Commit
}
```

### WASM-Specific Considerations

In the browser, there is no file system (unless OPFS is available). Writes modify the in-memory buffer. Persistence requires the application to flush the buffer to IndexedDB or OPFS after each transaction. Sharc provides the `GetDatabaseBytes()` method to extract the current state as a `byte[]` for storage.

For OPFS (Origin Private File System, available in modern browsers), a `OpfsPageSource` could provide true file-backed storage with synchronous access from Web Workers. This is a natural extension point — the `IPageSource` interface already supports it.

Page allocation in memory-backed mode must handle buffer resizing. The `ResizableMemoryPageSource` starts with the initial buffer and grows by doubling (or by a configurable growth factor) when new pages are needed. Since WASM memory is contiguous, this maps well to the browser's memory model.

### Gate Criteria

Round-trip test: create a database with Microsoft.Data.Sqlite, write rows with Sharc, read them back with Microsoft.Data.Sqlite — data must match exactly. Insert 10K rows, verify B-tree structure is valid (no orphan pages, correct parent pointers, sorted keys). Delete 50% of rows, verify freelist is correct, re-insert and verify freelist pages are reused. Index maintenance: insert rows into indexed table, verify index B-tree contains correct entries. Crash recovery: simulate crash mid-transaction, verify journal restores original state.

### Estimated Effort: 12 working days

This is the largest single feature. The B-tree split/merge logic is the most complex part — it requires handling multiple edge cases (root splits, underflow merges, overflow chain management during cell moves). The recommended approach is to implement insertion first (with page splitting), validate thoroughly with round-trip tests, then add deletion (with merging) as a follow-up within the same milestone.

---

## M12: Virtual Table Support (FTS5 + R-Tree)

**Purpose:** Full-text search over cached content. When Sharc manages a browser's local cache, users need to search message bodies, document text, product descriptions — content that lives in the cache. FTS5 is the standard solution.

### What Gets Built

**Shadow table reader.** SQLite virtual tables store their data in "shadow tables" — regular tables with names like `{vtab}_content`, `{vtab}_data`, `{vtab}_idx`, etc. Sharc can already read these as regular tables. The virtual table layer interprets their contents.

**FTS5 content reader.** FTS5 stores an inverted index across several shadow tables. The `_data` table contains a single-column table where each row is a binary blob encoding a segment of the inverted index (using a custom binary format documented in the SQLite source). Sharc needs a `Fts5SegmentReader` that parses these blobs to extract term → docid mappings.

**FTS5 query evaluator.** Given a search term (or simple boolean expression: AND, OR, NOT), look up matching document IDs in the inverted index, then join back to the content table to return results. This is not SQL — it's a direct inverted index lookup.

**R-Tree reader (optional).** R-Tree stores bounding boxes for spatial data. The shadow table `{vtab}_node` contains the R-Tree nodes as binary blobs. Parsing the node format enables spatial range queries — useful for location-based cache entries (e.g., "show cached places within this viewport").

### Why This Matters for WASM Cache

A browser-side cache of emails, messages, documents, or product data is only useful if it's searchable. Without FTS, the application must download results from the server for every search. With FTS backed by Sharc, searches run locally against the cached data — instant results, zero network latency, works offline.

The FTS5 index is stored in standard SQLite tables, so it survives the same persistence path as the rest of the database (IndexedDB / OPFS flush). A server-side process can build the FTS index and ship the entire database to the client as a single blob.

### Gate Criteria

Read an FTS5-indexed database created by SQLite. Search for a term and return matching rows with correct ranking. Boolean query (AND/OR) returns correct intersection/union. Performance: search 100K-document corpus in under 50ms on WASM.

### Estimated Effort: 8 working days

---

## Milestone Summary

| Milestone | Feature | Depends On | Days | Cumulative |
|---|---|---|---|---|
| **M7** | Index Read + Filter | M6 (done) | 5 | 5 |
| **M8** | WAL Read Support | M6 (done) | 5 | 10 |
| **M11** | Write Engine | M7, M8 | 12 | 22 |
| **M12** | Virtual Tables (FTS5) | M11 | 8 | 30 |

**Parallelizable:** M7 and M8 have no dependency on each other — they can be developed concurrently by separate contributors, bringing the critical path to 22 working days (M7 ∥ M8 → M11 → M12) rather than 30 sequential days.

---

## Maturity Indicators

The table below tracks the signals that matter to adopters evaluating whether Sharc is production-ready. Each indicator either exists today or has a clear completion milestone.

| Indicator | Current State | After Phase 2 |
|---|---|---|
| **Test coverage** | 489 tests, 0 failures | ~800 tests (write engine adds ~200, FTS adds ~100) |
| **Benchmark suite** | 127 benchmarks with MemoryDiagnoser | +40 write benchmarks, +20 FTS benchmarks |
| **Real-world validation** | Maker.AI production database (3,579 nodes, 4,861 edges) | + round-trip compatibility tests against Microsoft.Data.Sqlite |
| **Platform matrix** | .NET 8/9/10 on Windows/Linux/macOS + WASM (read-only) | WASM read-write with OPFS persistence |
| **API stability** | Public API unchanged since M6 | Write API is additive (new methods, no breaking changes) |
| **Documentation** | README + 9 PRC architecture docs + XML docs | + Write Engine design doc, WAL spec, FTS spec |
| **Thread safety** | 12 concurrency tests, CachedPageSource locking | + write-path locking, WAL concurrent reader tests |
| **NuGet packaging** | Not yet published | Publish after M11 gate passes (read-write MVP) |
| **SQLite format compliance** | Format 3, all serial types, overflow, rollback journal modes | + WAL format, index B-trees, freelist management |

### What "No Red Flags" Means

An adopter evaluating Sharc should find:

**No silent data corruption.** Every correctness bug from Phase 1 is fixed before Phase 2 begins. The write engine includes round-trip validation against Microsoft.Data.Sqlite — if Sharc writes it, SQLite must read it identically.

**No misleading performance claims.** The README benchmark section accurately describes what's being measured. Speed advantages are real (2x–40x on sequential reads) and are framed as "end-to-end API cost including SQL overhead on the SQLite side" rather than implying SQLite's engine is slower.

**No hidden scope limitations.** The README has a prominent "Current Limitations" section that honestly states what Sharc does and doesn't do. Features under development are listed with their milestone number, not hidden.

**No abandoned surface area.** The empty `Sharc.Crypto` project either contains a placeholder README explaining it's reserved for M9, or is removed from the solution until work begins. The Graph layer's `IContextGraph` interface either has a concrete implementation or is removed.

**No dependency surprises.** `Microsoft.Data.Sqlite` versions are pinned to a single version in `Directory.Packages.props`. All test frameworks standardized. Build artifacts (`obj/` directories) are `.gitignored`.

---

## Phase 2 Quality Gates

Every milestone must pass these gates before merging:

1. **All existing tests pass.** Zero regressions. The 489 existing tests are the safety net — if a write-path change breaks a read-path test, the write implementation is wrong.

2. **New feature tests at 90%+ branch coverage.** Measured by Coverlet. Edge cases matter most: page splits at every tree level, freelist exhaustion, WAL checksum validation failures, FTS5 with empty segments.

3. **Round-trip compatibility.** Databases written by Sharc must be readable by `sqlite3` CLI and `Microsoft.Data.Sqlite`. Databases written by SQLite must be readable by Sharc. This is the non-negotiable format compliance test.

4. **Benchmark regression check.** Read-path performance must not degrade by more than 5% after write-engine changes. The write engine adds code to `Sharc.Core` (page allocation, freelist) — it must not slow down the existing read path through added virtual dispatch or larger struct sizes.

5. **WASM smoke test.** A Blazor WASM test project opens a database from a `byte[]`, performs the feature's operations, and verifies results. This catches WASM-specific issues (no file I/O, no threading, limited stack size) early.

6. **Memory audit.** `MemoryDiagnoser` results for new operations. Write operations will allocate (journal pages, new cells) — the goal is to keep allocations proportional to the data written, with no per-operation overhead beyond the actual page modifications.

---

## Recommended Execution Order

### Week 1–2: Stabilize (Phase 1 Fixes)

Fix all 5 correctness bugs. Fix the 8 architectural inconsistencies. Update README with 🦈 branding, `catch.png` integration, honest benchmark framing, and limitations section. Pin package versions. Clean up build artifacts. Total: 3–4 days.

### Week 2–3: M7 (Index Read + Filter) + M8 (WAL Reader) — Parallel

These two milestones share no code and can proceed concurrently. M7 adds index B-tree parsing and the expression evaluator. M8 adds WAL file parsing and the overlay page source. Both extend `Sharc.Core` without modifying existing code. Total: 5 days (parallel).

### Week 4–6: M11 (Write Engine)

The largest and most critical milestone. Recommended internal breakdown:

| Sub-task | Days | Deliverable |
|---|---|---|
| Page allocator + freelist | 2 | Allocate/free pages, grow database |
| B-tree insertion + page split | 4 | Insert rows, handle splits at all levels |
| B-tree deletion + merge | 3 | Delete rows, handle underflow merges |
| Index maintenance | 2 | Maintain all indexes on insert/delete/update |
| Transaction journal | 1 | Rollback journal for crash recovery |

### Week 7–8: M12 (Virtual Tables / FTS5)

FTS5 shadow table parsing, inverted index reader, term lookup, boolean query evaluation. R-Tree is optional within this milestone — add it if time permits, defer otherwise.

### Week 9: Polish + Package

Final benchmarks, NuGet package preparation, documentation review, WASM integration guide.

---

## WASM Integration Guide (Preview)

For Blazor WASM applications, the integration pattern after Phase 2 completion:

```csharp
// In a Blazor WASM component or service

// 1. Load database from IndexedDB (via JS interop) or fetch from server
byte[] dbBytes = await LoadFromIndexedDb("my-cache.db")
                 ?? await Http.GetByteArrayAsync("/seed-data/cache.db");

// 2. Open with Sharc — no native binary needed
using var db = SharcDatabase.OpenMemory(dbBytes, new SharcOpenOptions
{
    ReadWrite = true,
    PageCacheSize = 500  // Tune for available memory
});

// 3. Read cached data
using var reader = db.CreateReader("products");
reader.Filter(r => r["category"].AsString() == "electronics");
while (reader.MoveNext())
{
    var product = DeserializeProduct(reader);
    // ...
}

// 4. Write to cache
db.Insert("products", new Dictionary<string, object>
{
    ["id"] = product.Id,
    ["category"] = product.Category,
    ["data"] = JsonSerializer.SerializeToUtf8Bytes(product),
    ["cached_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
});

// 5. Persist back to IndexedDB
byte[] updatedBytes = db.GetDatabaseBytes();
await SaveToIndexedDb("my-cache.db", updatedBytes);
```

**Published binary impact:** Sharc adds ~180 KB to a trimmed Blazor WASM publish (compared to ~900 KB for `e_sqlite3.wasm`). The AOT compilation produces efficient WASM instructions with no JavaScript interop on the data path.

---

*Phase 1 completed February 12, 2026. Phase 2 development begins upon Phase 1 stabilization.*
