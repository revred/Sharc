# Benchmark Protocol — Run, Analyze, Optimize

## Overview

This document defines the iterative protocol for running Sharc benchmarks, analyzing hot zones, identifying optimization opportunities, and deciding whether to act on them. Each benchmark run is not just a measurement — it's an investigation.

## Phase 1: Run Benchmarks

### Quick Iteration (Development)

Use `--job short` (3 iterations, 1 launch, 3 warmup) for fast feedback during active development:

```bash
# Standard comparative benchmarks (~15 min)
dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative* --job short --memory --exporters json

# Graph benchmarks (~3 min)
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph* --job short --memory --exporters json

# Single benchmark class (~1-2 min)
dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *TableScan* --job short --memory
```

### Full Measurement (Pre-release)

Use DefaultJob for publishable numbers:

```bash
dotnet run -c Release --project bench/Sharc.Benchmarks -- --filter *Comparative* --memory --exporters json
dotnet run -c Release --project bench/Sharc.Comparisons -- --filter *Graph* --memory --exporters json
```

### Results Location

Reports are written to `BenchmarkDotNet.Artifacts/results/` in the working directory.
Key files: `*-report-github.md` (markdown tables), `*-report-full-compressed.json` (raw data).

---

## Phase 2: Analyze Each Result Category

For each benchmark category, apply this analysis checklist:

### 2.1 Allocation Analysis

| Question | Where to Look |
|----------|--------------|
| What is the total Allocated per operation? | Report table `Allocated` column |
| Is Gen0/Gen1/Gen2 triggering? | Report table `Gen0`, `Gen1`, `Gen2` columns |
| Does Sharc allocate more than SQLite? | Compare `Allocated` columns |
| Is the allocation per-row or one-time? | Multiply: if 10K rows and 40 KB total → ~4 B/row (acceptable) |

**Red flags:**
- Gen1 or Gen2 collections in hot loops → memory pressure problem
- Sharc allocating 10x+ more than SQLite → allocation leak
- Allocation growing linearly with row count for non-string columns → buffer not reused

### 2.2 Timing Analysis

| Question | Where to Look |
|----------|--------------|
| What is the speedup ratio? | `Sharc_Mean / SQLite_Mean` |
| Is speedup < 2x for scan operations? | May indicate unnecessary work |
| Is speedup < 10x for seek operations? | Seek should be dramatically faster (no SQL overhead) |
| Does StdDev exceed 10% of Mean? | Noisy benchmark — may need more iterations |

### 2.3 Hot Zone Identification

For each Sharc benchmark method, trace the call chain and identify:

1. **CPU hot zones** — where most time is spent
2. **Allocation hot zones** — where most heap allocation occurs
3. **I/O hot zones** — where page reads dominate

**Common hot zones in Sharc:**

| Layer | Hot Zone | What to Check |
|-------|----------|--------------|
| RecordDecoder | `DecodeVariableLength()` | `data.ToArray()` for TEXT/BLOB copies |
| RecordDecoder | `DecodeRecord()` | `new ColumnValue[count]` array allocation |
| BTreeCursor | `AssembleOverflowPayload()` | `ArrayPool.Rent()` for overflow pages |
| SchemaReader | `ReadSchema()` | String allocations for SQL text, ColumnInfo lists |
| SharcDataReader | `GetString()` | `UTF8.GetString()` string materialization |
| CellParser | `ParseTableLeafCell()` | Varint decoding loops |
| FilePageSource | `GetPage()` | File I/O syscalls |

---

## Phase 3: Decide Whether to Optimize

Apply this decision framework for each identified hot zone:

### 3.1 Is it worth optimizing?

```
                     ┌─────────────────────────┐
                     │ Is allocation > 1 KB/row │
                     │ for non-string columns?  │
                     └──────────┬──────────────┘
                           Yes  │  No
                    ┌───────────┘  └──────→ ACCEPT (inherent cost)
                    ▼
          ┌─────────────────────┐
          │ Can we avoid the    │
          │ allocation entirely │
          │ (pool, stackalloc,  │
          │ reuse buffer)?      │
          └────────┬────────────┘
              Yes  │  No
       ┌──────────┘  └──────→ ACCEPT (document as known cost)
       ▼
  ┌──────────────────┐
  │ Will the fix add  │
  │ significant code  │
  │ complexity?       │
  └────────┬─────────┘
      Yes  │  No
  ┌────────┘  └──────→ IMPLEMENT (easy win)
  ▼
  DEFER (track in optimization backlog)
```

### 3.2 String allocations are expected

String materialization (`AsString()`) ALWAYS allocates. This is by design:
- UTF-8 bytes must be decoded to .NET `string` (UTF-16)
- Each string is a new managed object
- The zero-alloc alternative is `AsBytes()` returning `ReadOnlyMemory<byte>`

**Do not optimize string allocation away** — instead, ensure users can avoid it:
- Column projection (skip unwanted TEXT/BLOB columns)
- `AsBytes()` for zero-alloc text access
- Lazy decode (only materialize on access)

### 3.3 Schema allocation is one-time

The ~40 KB schema allocation happens once per `SharcDatabase.Open()`. It includes:
- SQL statement strings (~10-15 KB)
- ColumnInfo/TableInfo objects (~5-8 KB)
- String parsing intermediates (~5-8 KB)

**Do not optimize schema allocation** unless it exceeds 100 KB. It's amortized over millions of row reads.

---

## Phase 4: Apply Optimizations

If Phase 3 identifies an actionable optimization:

1. **Write a micro-benchmark** that isolates the hot zone
2. **Run baseline** with `--job short` to get before numbers
3. **Implement the fix** following TDD (test first)
4. **Run after** with the same `--job short` parameters
5. **Compare**: if < 5% improvement on a micro-benchmark, revert (noise margin)
6. **Run full suite** to verify no regressions

### Optimization Patterns

| Pattern | When to Use | Example |
|---------|------------|---------|
| Buffer reuse | Array allocated per row | `ColumnValue[]` reusable buffer in SharcDataReader |
| stackalloc | Small fixed-size temp arrays | `stackalloc long[64]` for serial types |
| ArrayPool | Large temporary buffers | Overflow page assembly |
| Lazy decode | Columns accessed selectively | Skip TEXT/BLOB decode until `Get*()` called |
| Generation counter | Invalidation without clearing | `_generationId` in SharcDataReader instead of `Array.Clear()` |
| Span slicing | Avoid copying byte ranges | `ReadOnlySpan<byte>.Slice()` for inline payloads |

---

## Phase 5: Update Documentation

After each benchmark cycle:

1. **Update README.md** benchmark tables with fresh numbers
2. **Note environment**: ShortRun vs DefaultJob, machine specs
3. **Update speedup claims** in the comparison table
4. **Add/update Current Limitations** if new constraints discovered
5. **Log decisions** in `PRC/DecisionLog.md` for any optimization choices

---

## Current Hot Zone Registry

### Active Optimizations (Already Implemented)

| Hot Zone | Optimization | Impact |
|----------|-------------|--------|
| ColumnValue[] per row | Reusable buffer in SharcDataReader | 0 B per-row array allocation |
| Serial type header | `stackalloc long[64]` | 0 B heap for typical tables |
| Overflow pages | `ArrayPool<byte>.Shared` | Pooled buffer reuse |
| Cell pointers | On-demand read (no pre-allocation) | 41 KB for 100K row scan |
| Row invalidation | Generation counter pattern | O(1) instead of O(N) clear |
| Lazy decode | Projection skips unwanted columns | 885 KB vs 33.9 MB |
| Page cache buffers | Demand-driven allocation (ADR-015) | 0 B at open; pays only for pages accessed |

### Known Costs (Accepted)

| Cost | Amount | Why Accepted |
|------|--------|-------------|
| Schema allocation | ~40 KB per open | ~~One-time, amortized over all reads~~ **FIXED:** Lazy schema init — 0 B at open, deferred until first `.Schema` access |
| String materialization | Variable per string | Inherent: UTF-8 → UTF-16 conversion |
| TEXT/BLOB data.ToArray() | Per TEXT/BLOB column | Required: page spans are transient |
| Graph seek allocation | 1,840 B per seek | Includes schema scan — will improve with cached schema in ConceptStore |
| Graph scan node allocation | ~320 B per node | Object construction for GraphRecord/RecordId |

### Optimization Backlog (Deferred)

| Hot Zone | Potential Optimization | Estimated Impact | Complexity |
|----------|----------------------|-----------------|-----------|
| ConceptStore.Get() | Cache schema root page, avoid re-scan | ~500 B/seek reduction | Low |
| RecordDecoder TEXT/BLOB | ReadOnlyMemory<byte> backed by page cache | Eliminate per-column copy | High (lifetime) |
| Graph scan RecordId | Pool or struct-ify RecordId | ~100 B/node reduction | Medium |
| ~~Schema SQL parsing~~ | ~~Lazy parse on first column access~~ | ~~~20 KB reduction~~ | **DONE** — `_schema ??=` lazy init implemented |
| BTreeCursor Stack<T> | Use fixed-size array (max depth 20) | ~56 B/cursor reduction | Low |
