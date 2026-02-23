# Performance Baseline — Full Allocation & Throughput Analysis

**Date:** 2026-02-22
**Branch:** Arena.Fix
**Hardware:** 11th Gen Intel Core i7-11800H 2.30GHz, 8P cores / 16 logical
**Runtime:** .NET 10.0.2 (RyuJIT x86-64-v4), Concurrent Workstation GC
**Dataset:** 5,000 users (9 columns), 5 departments
**Change:** Added `IPageSource.DataVersion` + `IBTreeCursor.IsStale` for multi-agent coordination (+8 B cursor construction)

---

## Allocation Tier List

### Tier 0 — Zero GC (cursor construction only, 664–912 B)

| Benchmark | Mean | Allocated | GC | Notes |
|-----------|------|-----------|-----|-------|
| Sharc_PointLookup | 272 ns | 664 B | 0 | B-tree seek, single row |
| Sharc_TypeDecode | 176 us | 688 B | 0 | Full scan, type decode only |
| Sharc_GcPressure | 175 us | 688 B | 0 | Full scan, designed for zero GC |
| Sharc_NullScan | 175 us | 688 B | 0 | Full scan with NULL handling |
| Sharc_FilterStar | 304 us | 800 B | 0 | Closure-composed predicate scan |
| Sharc_WhereFilter | 298 us | 912 B | 0 | SQL WHERE filter |
| DirectTable_SequentialScan | 220 us | 672 B | 0 | Raw CreateReader |
| SELECT * (no filter) | 105 us | 672 B | 0 | Simplest SQL query |

**Key insight:** All core read operations are flat 664–912 B (+8 B vs previous baseline from `_snapshotVersion` field for multi-agent staleness detection). This is cursor/reader construction only — the hot path (MoveNext + accessor) is truly zero-allocation.

### Tier 0.5 — Index Seek (1,352–1,456 B)

| Benchmark | Mean | Allocated | GC | Notes |
|-----------|------|-----------|-----|-------|
| Sharc_Where_IndexSeek (int) | 1.25 us | 1,456 B | 0 | O(log N) SeekFirst + table Seek, 3 rows |
| Sharc_Where_FullScan (baseline) | 506 us | 816 B | 0 | Sequential scan, same query |
| Sharc_WhereText_IndexSeek | 185 us | 1,352 B | 0 | UTF-8 byte-span comparison, ~1K rows |
| Sharc_WhereText_FullScan | 224 us | 672 B | 0 | Sequential scan, same query |
| SQLite_Where_PointLookup | 35.4 us | 872 B | 0 | SQLite indexed point lookup |

**Key insight:** Integer index seek is **450x faster than full scan** and **28x faster than SQLite**. Text index seek is 1.2x faster than full scan at 20% selectivity — benefit grows with selectivity. Zero-allocation hot path: byte-span `SequenceEqual` for text, `DecodeInt64Direct` for integers. Both paths use `ArrayPool<long>.Shared` for serial type buffer.

### Tier 1 — Minimal Overhead (+96–296 B per feature)

| Benchmark | Mean | Allocated | Notes |
|-----------|------|-----------|-------|
| RegisteredView scan | 178 us | 776 B | +96 B over direct table (2% overhead) |
| Subview depth 1 | 179 us | 1,072 B | +296 B per subview level |
| Subview depth 2 | 210 us | 1,360 B | Linear overhead, ~16 us/level |
| CrossType filter | 291 us | 856 B | Zero overhead vs same-type |
| SameType filter | 292 us | 856 B | Baseline for filter comparison |

**Key insight:** View cursor wrapping costs ~96 B and ~16 us per level. Cross-type filter promotion is free.

### Tier 2 — Streaming Operations (1.7–5.4 KB)

| Benchmark | Mean | Allocated | Notes |
|-----------|------|-----------|-------|
| UNION (streaming set op) | 726 us | 1,952 B | Fingerprint-based dedup |
| INTERSECT | 699 us | 1,680 B | Streaming set intersection |
| EXCEPT | 700 us | 1,680 B | Streaming set difference |
| 3-way UNION ALL | 287 us | 2,712 B | No dedup needed |
| GROUP BY (5 groups) | 352 us | 5,424 B | Streaming hash aggregation |

**Key insight:** Streaming set operations are excellent at 1.7–2.7 KB. GROUP BY uses O(G) memory where G = number of groups; 5,424 B for 5 groups is well-optimized (see analysis below).

### Tier 3 — Moderate Materialization (31–98 KB)

| Benchmark | Mean | Allocated | Notes |
|-----------|------|-----------|-------|
| Cote SELECT | 139 us | 31,608 B | Pre-materialized common table expression |
| ORDER BY + LIMIT 50 | 389 us | 32,672 B | Top-N heap, streams top 50 from 5K |
| WHERE (full) | 242 us | 97,928 B | Full result materialization |

**Key insight:** ORDER BY + LIMIT uses a top-N heap (not full sort), but still allocates 32 KB for the heap structure. SQLite achieves 3,120 B for the same query — the gap is 10.5x, primarily from QueryValue[] array overhead.

### Tier 4 — Heavy Materialization (400 KB+)

| Benchmark | Mean | Allocated | Notes |
|-----------|------|-----------|-------|
| UNION ALL (5K) | 467 us | 414,920 B | Must materialize all rows for append |
| Filtered view (Func) | 1,161 us | 797 KB | Func predicate forces full pre-materialization |

**Key insight:** UNION ALL materializes both sides into QueryValue[] rows — unavoidable without a streaming iterator model. Filtered views (Func predicates) cannot push down to B-tree.

### Tier 5 — Join (1.2–6.2 MB, Gen2 collections)

| Benchmark | Mean | Allocated | Gen0 | Gen1 | Gen2 | Notes |
|-----------|------|-----------|------|------|------|-------|
| JOIN 1K rows | 889 us | 1,203 KB | 98 | 23 | 0 | Hash join, both sides materialized |
| JOIN 1K + filter | 505 us | 358 KB | 28 | 4 | 0 | Filter reduces materialized set |
| JOIN 5K rows | 8.6 ms | 6,181 KB | 578 | 375 | 125 | Heaviest allocator in the system |
| JOIN 5K + filter | 3.3 ms | 1,457 KB | 129 | 125 | 23 | Filter halves allocation |

**Key insight:** Joins are the heaviest allocator. The hash-join builds a `Dictionary<string, List<QueryValue[]>>` for the probe side, requiring full materialization of both tables. Gen2 collections at 5K rows indicate large-object-heap pressure from the dictionary's internal arrays.

---

## Sharc vs SQLite Comparison

| Operation | Sharc | SQLite | Sharc/SQLite | Notes |
|-----------|-------|--------|-------------|-------|
| Point lookup | 272 ns / 664 B | 25.9 us / 728 B | **0.01x time** | Sharc 95x faster (direct B-tree seek, generic specialization) |
| Sequential scan 5K | 1,251 us / 1.4 MB | 5,895 us / 1.4 MB | **0.21x time** | Sharc 4.7x faster, same allocation |
| SELECT * (3 cols) | 105 us / 672 B | 637 us / 688 B | **0.16x time** | 6x faster, comparable allocation |
| WHERE filter | 298 us / 912 B | 560 us / 720 B | **0.53x time** | 1.9x faster, similar allocation |
| GROUP BY | 366 us / 5,416 B | 538 us / 920 B | **0.68x time, 5.9x alloc** | See analysis below |
| ORDER BY+LIMIT | 505 us / 32 KB | 426 us / 3.1 KB | **1.19x time, 10.5x alloc** | SQLite wins — native sort is cheaper |
| Insert 1 row | 4.30 ms / 16.3 KB | 4.87 ms / 11.8 KB | **0.88x time, 1.4x alloc** | Sharc 12% faster, fsync-dominated |
| Insert 100 rows | 4.32 ms / 20.0 KB | 5.44 ms / 71.2 KB | **0.79x time, 0.28x alloc** | Sharc 3.6x less allocation |
| Transaction 100 rows | 4.70 ms / 18.5 KB | 5.95 ms / 71.2 KB | **0.79x time, 0.26x alloc** | Sharc 3.9x less allocation |
| Insert+Read 100 rows | 4.78 ms / 39.6 KB | 5.72 ms / 71.9 KB | **0.84x time, 0.55x alloc** | Read-back adds ~20 KB |
| Graph BFS 2-hop | 2.4 us / 960 B | 80.8 us / 2,808 B | **0.03x time** | Sharc 34x faster |
| Graph scan edges | 442 us / 640 B | 2,396 us / 696 B | **0.18x time** | Sharc 5.4x faster |

**Note on SQLite allocation numbers:** SQLite's reported allocations only measure .NET GC-tracked objects (SqliteCommand/SqliteDataReader wrappers). SQLite's actual internal memory usage (native C hash tables, sort buffers) is invisible to BenchmarkDotNet's MemoryDiagnoser.

---

## Deep Dive: GROUP BY Allocation (5,424 B)

**Query:** `SELECT dept, COUNT(*), AVG(score) FROM users GROUP BY dept`
**Path:** `QueryPostProcessor` → `StreamingAggregateProcessor` → `StreamingAggregator`

The streaming path correctly uses O(G) memory, not O(N). Text-pool fingerprinting avoids 5,000 string allocations (only 5 unique dept strings pooled).

### Breakdown

| Component | Bytes | Location |
|-----------|-------|----------|
| Source reader construction | ~680 | SharcDataReader baseline |
| Query parse + intent objects | ~400 | Parser → QueryIntent |
| AggregateProjection rewrite | ~200 | Column resolution |
| StreamingAggregator setup | ~256 | _groupOrdinals, _lookupKeyBuffer, _aggSourceOrdinals, _outputColumns |
| Dictionary<GroupKey,GroupAccumulator> | ~528 | Empty dict + resize from 3→7 entries |
| Text string pool + 5 pooled strings | ~360 | Fingerprint128 dict + GetString() per unique dept |
| **`new AggState[2]` × 5 groups** | **~1,040** | **Dominant: 208 B per group** |
| `storedValues = new QueryValue[1]` × 5 | ~200 | Group key copies |
| Output rows (`new QueryValue[3]` × 5) | ~440 | Finalize() results |
| Output SharcDataReader | ~200 | Final reader wrapper |
| Other (isTextGroupCol, buffer, etc.) | ~120 | One-time setup arrays |
| **Total estimate** | **~4,416** | Remaining ~1,000 in intent/column resolution |

### Optimization opportunity

The `new AggState[2]` per group is the largest single allocator (1,040 B / 19%). For queries with 1–2 aggregates (the common case), inlining `AggState Agg0`/`Agg1` fields directly into `GroupAccumulator` would eliminate these array allocations entirely.

---

## View Query Performance

| Benchmark | Mean (us) | Ratio | Allocated | Notes |
|-----------|-----------|-------|-----------|-------|
| DirectTable_SequentialScan | 174.9 | 1.00 (baseline) | 680 B | Raw `CreateReader("users", ...)` |
| RegisteredView_SequentialScan | 177.6 | 1.02x | 776 B | `OpenView("v_users")` — 2% overhead |
| SqlQuery_DirectTable | 196.9 | 1.13x | 856 B | `Query("SELECT name, age, score FROM users")` |
| SqlQuery_RegisteredView | 201.2 | 1.15x | 896 B | `Query("SELECT * FROM v_users")` — Cote resolution |

## Subview Chain Depth

| Benchmark | Mean (us) | Ratio | Allocated | Per-level overhead |
|-----------|-----------|-------|-----------|-------------------|
| Depth 0 (direct view) | 162.6 | 0.93x | 776 B | — |
| Depth 1 (1 subview) | 179.3 | 1.03x | 1,072 B | +16.7 us, +296 B |
| Depth 2 (2 subviews) | 209.5 | 1.20x | 1,360 B | +30.2 us, +288 B |

## Filtered View (Func Predicate)

| Benchmark | Mean (us) | Ratio | Allocated |
|-----------|-----------|-------|-----------|
| SqlQuery_DirectTableWithWhere | 283.4 | 1.62x | 856 B |
| SqlQuery_FilteredView | 1,161.1 | 6.64x | 797 KB |

---

## Zero-Allocation Verification

All non-filtered view operations show **zero Gen0/Gen1 collections** per 1,000 operations. The per-operation allocations (640–1,360 B) are cursor/reader construction costs, not per-row costs. The hot-path (MoveNext + accessor methods) remains zero-allocation as designed.

---

## Execution Tier Comparison (DIRECT vs CACHED vs JIT) — v2 Optimized

**Dataset:** 2,500 rows × 8 columns (id, name, email, age, score, active, dept, created)
**Optimization:** QueryIntent-keyed caches (reference equality) + per-intent JIT entries + deferred ComputeParamKey

### Filtered Scan — `WHERE age > 30` (~2,000 matching rows)

| Method | Mean | Ratio | Allocated |
|---|---|---|---|
| DIRECT: `Query(sql)` | 123.20 us | 1.00 | 704 B |
| CACHED: `Query(hint sql)` | 118.01 us | **0.96** | 664 B |
| JIT: `Query(hint sql)` | 116.50 us | **0.95** | 664 B |
| Manual `Prepare().Execute()` | 117.67 us | 0.96 | 664 B |
| Manual `Jit().Query()` | 107.72 us | 0.88 | 712 B |

### Parameterized Filter — `WHERE age > $minAge`

| Method | Mean | Ratio | Allocated |
|---|---|---|---|
| DIRECT: parameterized | 118.46 us | 1.00 | 760 B |
| CACHED: parameterized | 116.27 us | **0.98** | 720 B |
| Manual `Prepare().Execute(params)` | 107.12 us | 0.91 | 720 B |

### Full Scan — `SELECT *` (no filter)

| Method | Mean | Ratio | Allocated |
|---|---|---|---|
| DIRECT: `SELECT *` | 80.24 us | 1.00 | 704 B |
| CACHED: `SELECT *` | 102.02 us | 1.27 | 664 B |
| JIT: `SELECT *` | 109.98 us | 1.37 | 664 B |

**Key insight:** After the QueryIntent-keyed cache optimization, CACHED and JIT hints **beat DIRECT** for filtered/parameterized queries (0.95-0.98x). Manual handles remain 4-12% faster by eliminating all routing. FullScan remains inherently slower for hints (no filter to optimize). Full report: `PRC/ExecutionTierReport.md`.

---

## Recommendations

### Immediate wins

1. **Prefer SQL WHERE over Func filters** — native filter path is 3.7x faster, 940x less allocation
2. **Cross-type filters are free** — no need to manually cast values to match column types
3. **Subview depth < 5** is safe with <60 us overhead

### Optimization targets (by impact)

1. **JOIN materialization** (Tier 5) — largest allocator, explore streaming hash-join or probe-side-only materialization
2. **ORDER BY + LIMIT heap** (Tier 3) — 10.5x more allocation than SQLite, investigate QueryValue[] pooling
3. **GROUP BY AggState arrays** (Tier 2) — inline first 1–2 aggregates to save ~1 KB per query
4. **Cote pre-materialization** (Tier 3) — skip double-materialization for non-filtered Cotes
5. **View SQL query path** — 24% overhead from Cote resolution, cacheable for repeated queries
