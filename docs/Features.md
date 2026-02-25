# Features.md — Sharc Context Engine: What to Build and Why

> **North Star:** Sharc is the premier AI context store for browser, edge, WASM, and agent-native environments.
> Every feature decision must serve that mission: faster retrieval, tighter memory budgets, verified provenance, and zero native dependencies.

---

## The Right Mental Model

Sharc's access pattern is fundamentally different from a general-purpose database:

- **Reads dominate** — agents pull context constantly (point lookups, ranked retrieval, graph hops)
- **Writes are bursty, not concurrent** — a context snapshot is written by one agent, not fifty parallel clients
- **Single-process** — WASM sandboxes, browser tabs, and mobile runtimes have no multi-process contention
- **Durability tolerance is high** — losing 200 ms of cached context on a crash is acceptable; re-fetching is cheap
- **Storage is scarce** — browser OPFS quotas, mobile flash, and IoT RAM all demand ruthless compactness

This profile rules out features like WAL-mode concurrent writes (a multi-process server problem) and favors features like semantic TTL, vector search, and compressed context pages.

---

## Priority 1 — Close the Platform Gap (Ship First)

These remove the most critical gaps against Sharc's own stated limitations. Ship these before anything else.

### 1.1 RIGHT OUTER JOIN and FULL OUTER JOIN

**Status:** All five JOIN types are implemented: INNER, LEFT, RIGHT, FULL OUTER, and CROSS. FULL OUTER uses tiered zero-allocation execution (Zero.Hash).

**Why it matters:** AI agent queries routinely join a context table against a knowledge base or graph node table. A missing JOIN type forces callers to restructure queries in application code, leaking logic that belongs in the query layer. RIGHT JOIN is algebraically equivalent to a swapped LEFT JOIN. FULL OUTER is LEFT UNION RIGHT minus the intersection. The existing hash-join infrastructure handles both without new I/O primitives.

**Acceptance:** All existing join benchmarks pass unchanged. `RIGHT JOIN` and `FULL OUTER JOIN` parse, plan, and execute correctly. `Sharc.Query.Tests` green.

---

### 1.2 Streaming Query Results (Lazy Materialization)

**Status:** The `Query()` API materializes entire result sets into managed arrays before returning the first row.

**Why it matters:** Context retrieval queries often SELECT across large embedding tables or conversation histories and then LIMIT to the top 10 rows. Materializing 50,000 rows to return 10 wastes GC budget — the opposite of Sharc's zero-alloc philosophy. The `CreateReader` API already streams lazily from the B-tree cursor. `Query()` must match that discipline.

**Implementation:** Replace result list accumulation with a lazy `IEnumerable<RowResult>` backed by a cursor. `ORDER BY + LIMIT` already uses a streaming TopN heap — extend that pattern to all query paths.

**Acceptance:** `WHERE + ORDER BY + LIMIT` managed allocation drops from ~42 KB to <6 KB. No regression on existing query benchmarks.

---

### 1.3 Configurable Write Buffer (Batched Flush, not WAL)

**Status:** Each write flushes synchronously to the page file.

**Why it matters:** Agent context snapshots arrive in bursts — a reasoning step produces 20–50 INSERTs in rapid succession. Flushing after each one serializes I/O that could be batched. A simple in-memory write buffer that flushes on `Commit()`, buffer-full, or explicit `FlushAsync()` gives 80% of WAL's write throughput benefit with none of WAL's multi-file, shared-memory complexity that is incompatible with WASM and OPFS.

**API addition:**
```csharp
var opts = new SharcWriteOptions { FlushPolicy = FlushPolicy.Batched, MaxBufferedPages = 64 };
using var writer = SharcWriter.From(db, opts);
```

**Acceptance:** 100-INSERT transaction throughput improves by ≥2x over current synchronous path. Single-file guarantee maintained. WASM build unaffected.

---

## Priority 2 — Context-Native Features (The Moat)

These are features no general-purpose embedded database offers out of the box. They make Sharc irreplaceable in agent workloads.

### 2.1 Semantic TTL (Time-to-Live per Row)

**Why it matters:** AI context is time-scoped. Session memories, tool-call results, and retrieved chunks all have natural expiry. Without TTL, callers manage expiry in application code — fragile and inconsistent across agents. Sharc should own this.

**Design:** Add an optional `_sharc_ttl` system column (Unix timestamp, INTEGER) to any table via a pragma. A background sweep cursor (triggered lazily on read or explicitly via `db.PurgeExpired()`) deletes expired rows using the existing write engine. The trust ledger logs each purge event with the agent ID that triggered it.

**API:**
```csharp
db.EnableTtl("context_chunks");                       // one-time setup
writer.Insert("context_chunks", ..., ttl: TimeSpan.FromMinutes(30));
db.PurgeExpired("context_chunks");                    // explicit sweep
```

**Acceptance:** Rows past TTL are invisible to `CreateReader` and `Query()`. Purge is logged in the audit ledger. Zero allocation on read paths that skip non-expired rows.

---

### 2.2 Ranked Top-K Retrieval (Native Priority Queue API)

**Why it matters:** Context retrieval is a ranking problem. The canonical pattern is "give me the 10 most relevant chunks." Today, callers express this as `ORDER BY score DESC LIMIT 10` in SQL, which forces the query pipeline to parse, compile, plan, and execute a full query round-trip. For hot retrieval paths called thousands of times per agent session, a native `Seek(topK, scoreColumn)` that bypasses the SQL layer and reads directly from the B-tree cursor with an inline min-heap is significantly faster.

**API:**
```csharp
using var reader = db.CreateRankedReader("context_chunks", scoreColumn: "relevance", topK: 10);
while (reader.Read()) { /* rows in descending score order */ }
```

**Acceptance:** Top-10 retrieval from a 10,000-row table completes in <50 μs with zero intermediate materialization. Benchmark added to `Sharc.Benchmarks`.

---

### 2.3 Page-Level Compression (LZ4 / Brotli per Table)

**Why it matters:** Browser OPFS quotas, mobile storage, and IoT flash are scarce. Context chunks — summaries, embeddings as base64, conversation history — are highly compressible (3–5x typical ratios). Compression at the page level is transparent to the query layer: the `IPageSource` pipeline already supports `IPageTransform` (used for AES-256-GCM decryption). Compression is another transform in that chain.

**Design:** Add `CompressionCodec.None | LZ4 | Brotli` to `SharcOpenOptions`. Compressed pages carry a one-byte codec tag in their header. The `IPageTransform` chain decompresses on read, compresses on write. Encryption and compression compose cleanly (compress-then-encrypt is the correct order).

**Acceptance:** A 10,000-row text context table compresses to <35% of original size with LZ4. Decompression latency <5 μs per page. All existing tests pass with compression enabled.

---

### 2.4 Vector Embedding Column Type with ANN Search

**Why it matters:** Every AI agent pipeline produces and consumes vector embeddings (1536-float arrays for OpenAI, 768-float for smaller models). SQLite has no native vector type; callers store raw BLOB and do full-table scans. Sharc can be the first .NET embedded database with a first-class `ColumnType.Float32Array` and approximate nearest-neighbor (ANN) search built in.

**Design:**
- Add `ColumnType.Float32Array(dimensions: 1536)` as a native Sharc type (stored as packed IEEE 754 floats, not base64 blob).
- For small datasets (<50,000 vectors): flat linear scan with SIMD dot-product via `System.Runtime.Intrinsics` (AVX2 / NEON).
- For larger datasets: optional HNSW (Hierarchical Navigable Small World) secondary index stored in a sibling table (`_sharc_hnsw_{tablename}`).

**API:**
```csharp
using var reader = db.CreateVectorReader(
    "embeddings",
    queryVector: float[] { ... },
    topK: 5,
    metric: DistanceMetric.CosineSimilarity);
```

**Acceptance:** Top-5 cosine search over 10,000 × 1536-float vectors completes in <10 ms on flat scan. HNSW index build and query correctness verified against brute-force baseline (recall@5 ≥ 0.95).

**Package:** Ships as `Sharc.Vector` (optional, mirrors the `Sharc.Crypto` and `Sharc.Graph` pattern).

---

### 2.5 OPFS First-Class Storage Backend

**Why it matters:** The browser's Origin Private File System (OPFS) is the only durable, high-performance storage available to WASM workloads. Sharc's WASM build currently relies on in-memory or generic stream-based I/O. A dedicated `OPFSPageSource` using the synchronous WASM-thread OPFS access API gives Sharc native browser persistence with performance that beats IndexedDB by 10–100x for structured reads.

**Design:** Implement `IPageSource` over the OPFS `FileSystemSyncAccessHandle` API (available in dedicated workers). The Blazor WASM / JavaScript interop layer wraps this for C# callers.

**API:**
```csharp
// In Blazor WASM
using var db = await SharcDatabase.OpenOpfsAsync("agent-context.db");
```

**Acceptance:** Sharc WASM reads 1,000 context rows from OPFS in <5 ms. Write-and-read round-trip for a 100-row context snapshot completes in <20 ms. Verified in Chrome 122+ and Firefox 119+.

**Package:** Ships as `Sharc.Wasm`.

---

### 2.6 Context Diff and Merge API

**Why it matters:** Multi-agent pipelines split a task across sub-agents, each building its own context snapshot. Reconciling those snapshots is currently left entirely to the caller — a complex, error-prone operation. Sharc's trust layer already tracks which agent wrote which row (via the ECDSA ledger). A `SharcMerge` API that combines two context databases with configurable conflict resolution is a natural extension of that provenance model.

**Design:**
```csharp
SharcMerge.Merge(
    source: db1,
    target: db2,
    table: "context_chunks",
    conflict: ConflictPolicy.PreferHigherTrust   // or: LastWrite, KeepBoth, ThrowOnConflict
);
```

Conflict detection uses the row's ledger entry (agent ID, timestamp, trust score). `PreferHigherTrust` is the default — the row attested by the higher-reputation agent wins.

**Acceptance:** Merge of two 1,000-row context databases completes in <50 ms. Conflict resolution is deterministic and logged in the ledger. `Sharc.IntegrationTests` covers all four conflict policies.

---

## Priority 3 — Performance Ceiling Breakers (The Long Game)

These require deeper investment but deliver capabilities no competing embedded database can match.

### 3.1 SIMD-Accelerated Column Scanning

**Why:** The parsing layer already mentions SIMD intent. Extend vectorized evaluation to predicate filtering — scanning 16 INT64 values in a single AVX2 instruction rather than one at a time. For filter-heavy context retrieval (e.g., filtering 100,000 embedding rows by agent ID before vector scoring), this delivers 4–16x improvement over scalar loops.

**Scope:** `System.Runtime.Intrinsics.X86.Avx2` for x64, `System.Runtime.Intrinsics.Arm.AdvSimd` for ARM/mobile. Falls back gracefully to scalar on unsupported platforms.

### 3.2 Columnar Read Path for Wide Tables

**Why:** Embedding tables are extremely wide (1,536 float columns) but queries typically access only 2–3 columns (id, vector, score). A row-oriented B-tree reads all 1,536 floats even when only 3 are needed. An optional columnar layout (each column stored as a contiguous byte run) makes column-projection I/O proportional to the columns accessed, not the total row width. This is the single biggest memory and I/O win for embedding-heavy workloads.

**Scope:** Opt-in via `CREATE TABLE ... USING columnar` hint. The existing `IPageSource` abstraction accommodates a `ColumnarPageSource` without changes to the query layer.

### 3.3 Full-Text Search (Sharc.Search)

**Why:** AI agents retrieve context by semantic similarity (vector search) and by keyword (full-text search). Without FTS, callers fall back to SQLite's FTS5 virtual table — losing all of Sharc's speed and provenance guarantees for text queries. An inverted posting list stored alongside the B-tree, with BM25 scoring, closes this gap.

**Scope:** Ships as `Sharc.Search`. Integrates with the trust layer: each indexed document carries its contributor's agent ID, enabling trust-filtered text search ("find all chunks mentioning 'memory consolidation' contributed by agents with reputation ≥ 0.8").

### 3.4 Adaptive Query Statistics

**Why:** Sharc's query planner currently uses static heuristics. After each query, recording actual vs. estimated row counts and storing them in a compact `_sharc_stats` table enables the planner to improve over time for a given database's actual data distribution. This is something Postgres does globally; Sharc does it per-file, per-agent-session, making it uniquely suited to personalized context stores.

---

## What We Are Deliberately NOT Building

| Feature | Reason |
|---|---|
| WAL-mode concurrent writes | Requires shared memory — incompatible with WASM sandbox and OPFS. Single-writer is correct for the target environment. |
| Network file system support | Sharc is an embedded, single-machine context store by design. |
| Full PostgreSQL-compatible SQL | Sharq (the query language) is intentionally minimal. Complexity is the enemy of a 52 KB engine footprint. |
| Multi-tenant server mode | Out of scope. Sharc is a library, not a server. For server workloads, use PostgreSQL. |
| ~~RIGHT/FULL OUTER JOIN~~ | ✅ **Shipped** — all five JOIN types (INNER, LEFT, RIGHT, FULL OUTER, CROSS) are implemented with tiered zero-allocation execution. |

---

## Feature Delivery Sequence

```
Phase 1 (Close the gaps)
  ├── 1.2 Streaming query results
  ├── 1.3 Batched write buffer
  └── 1.1 RIGHT / FULL OUTER JOIN

Phase 2 (Context-native primitives)
  ├── 2.1 Semantic TTL
  ├── 2.2 Ranked Top-K retrieval
  └── 2.3 Page-level compression

Phase 3 (Platform expansion)
  ├── 2.5 OPFS backend (Sharc.Wasm)
  └── 2.6 Context diff / merge

Phase 4 (AI-native differentiation)
  └── 2.4 Vector embedding + ANN (Sharc.Vector)

Phase 5 (Performance ceiling)
  ├── 3.1 SIMD column scanning
  ├── 3.2 Columnar read path
  ├── 3.3 Full-text search (Sharc.Search)
  └── 3.4 Adaptive query statistics
```

Each phase ships independently. Every feature must have tests before implementation (see CLAUDE.md TDD workflow) and a corresponding benchmark in `Sharc.Benchmarks` before it is considered complete.

---

## How to Read the Acceptance Criteria

Every feature above specifies measurable acceptance criteria. These are not aspirational — they are the definition of done. If the criterion is not met, the feature is not shipped. Benchmark methodology follows `PRC/BenchmarkWorkflow.md`. Allocation budgets follow the tier classification in `CLAUDE.md`.

---

*Last updated: 2026-02-19 | Crafted through AI-assisted engineering by Ram Kumar Revanur*
