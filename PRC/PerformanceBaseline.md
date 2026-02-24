# Performance Baseline — Full Allocation & Throughput Analysis

**Date:** 2026-02-23 (v5 Leaf Cache + Random Lookup Proof)
**Branch:** local.MultiCache
**Hardware:** 11th Gen Intel Core i7-11800H 2.30GHz, 8P cores / 16 logical
**Runtime:** .NET 10.0.2 (RyuJIT x86-64-v4), Concurrent Workstation GC
**Dataset:** 5,000 users (9 columns), 5 departments
**Change:** v5 B-tree leaf range cache — same-rowid fast path (skip all traversal), same-leaf fast path (skip interior pages), 512-entry circular random buffer for honest benchmarking. Random rowid lookup achieves 276 ns / 0 B (109x vs SQLite). Same-rowid achieves 104 ns / 0 B (257x).
**Previous:** v4 Prepared Pattern — `PreparedReader`/`PreparedQuery` zero-allocation reuse + ThreadStatic pool. PointLookup was 159 ns / 0 B (prepared) before leaf cache.

---

## Allocation Tier List

### Tier 0 — True Zero Allocation (Prepared Pattern, 0 B)

| Benchmark | Hot (Prepared) | Cold (CreateReader) | Allocated (Hot) | Allocated (Cold) | GC | Notes |
|-----------|---------------|---------------------|-----------------|------------------|----|-------|
| Sharc_RandomLookup | 275.8 ns | — | **0 B** | — | 0 | **109x vs SQLite** (honest random rowid, 512-buf) |
| Sharc_PointLookup | 103.7 ns | 82.4 ns (pooled) | **0 B** | **0 B** | 0 | 257x vs SQLite (same-rowid, leaf cache hit) |
| Sharc_TypeDecode | 161.3 us | 151.7 us | **0 B** | 408 B | 0 | Full scan, type decode only |
| Sharc_GcPressure | 168.0 us | 146.0 us | **0 B** | 408 B | 0 | Full scan, zero GC target |
| Sharc_NullScan | 141.4 us | 148.2 us | **0 B** | 408 B | 0 | Full scan with NULL handling |
| Sharc_FilterStar | 229.9 us | 231.2 us | **0 B** | 592 B | 0 | Closure-composed predicate scan |
| Sharc_WhereFilter | 236.1 us | 245.5 us | **0 B** | 680 B | 0 | SQL WHERE filter (PreparedQuery) |
| Sharc_SequentialScan | 908.1 us | 922.7 us | 1.35 MB | 1.35 MB | Gen0 | String-dominated, reader overhead invisible |

**Key insight:** The Prepared Pattern (`PreparedReader`/`PreparedQuery`) achieves **true 0 B allocation** in every benchmark area. The B-tree leaf range cache adds same-rowid (257x) and same-leaf (skip interior pages) fast paths that bring random-rowid lookups to 276 ns / 0 B (109x vs SQLite). The ThreadStatic reader pool also eliminates allocation on the cold `CreateReader()` path for no-projection calls. SequentialScan is string-allocation dominated (5K `GetString()` calls = 1.35 MB) — reader overhead is invisible at that scale.

### Tier 0 (legacy) — Cold Path Only (408–680 B cursor construction)

| Benchmark | Mean | Allocated | GC | Notes |
|-----------|------|-----------|-----|-------|
| DirectTable_SequentialScan | 220 us | 672 B | 0 | Raw CreateReader (no projection) |
| SELECT * (no filter) | 105 us | 672 B | 0 | Simplest SQL query |

**Note:** These benchmarks have not been updated to the prepared pattern yet. They represent the legacy cold-path allocation baseline.

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
| Random lookup (prepared) | 275.8 ns / **0 B** | 30,135 ns / 832 B | **0.009x time** | Sharc **109x faster** (honest random rowid, 512-buf) |
| Point lookup (prepared) | 103.7 ns / **0 B** | 26,738 ns / 408 B | **0.004x time** | Sharc **257x faster** (same-rowid, leaf cache) |
| Point lookup (cold, pooled) | 82.4 ns / **0 B** | 30,229 ns / 728 B | **0.003x time** | Sharc **367x faster** (same-rowid, cold path) |
| 5-row read | 5.5 ns / 0 B | 23.0 us / 944 B | **0.0002x time** | Sharc **4,200x faster** (pre-decoded, zero-alloc) |
| 100-int scan | 123 ns / 0 B | 46.1 us / 384 B | **0.003x time** | Sharc **375x faster** (in-memory varint decode) |
| Sequential scan 5K | 1,251 us / 1.4 MB | 5,895 us / 1.4 MB | **0.21x time** | Sharc 4.7x faster, same allocation |
| SELECT * (3 cols) | 105 us / 672 B | 637 us / 688 B | **0.16x time** | 6x faster, comparable allocation |
| WHERE filter (prepared) | 236.1 us / **0 B** | 550.6 us / 720 B | **0.43x time** | Sharc 2.3x faster, zero alloc |
| WHERE filter (cold) | 245.5 us / 680 B | 550.6 us / 720 B | **0.45x time** | 2.2x faster, similar allocation |
| GROUP BY | 366 us / 5,416 B | 538 us / 920 B | **0.68x time, 5.9x alloc** | See analysis below |
| ORDER BY+LIMIT | 505 us / 32 KB | 426 us / 3.1 KB | **1.19x time, 10.5x alloc** | SQLite wins — native sort is cheaper |
| Insert 1 row | 4.30 ms / 16.3 KB | 4.87 ms / 11.8 KB | **0.88x time, 1.4x alloc** | Sharc 12% faster, fsync-dominated |
| Insert 100 rows | 4.32 ms / 20.0 KB | 5.44 ms / 71.2 KB | **0.79x time, 0.28x alloc** | Sharc 3.6x less allocation |
| Transaction 100 rows | 4.70 ms / 18.5 KB | 5.95 ms / 71.2 KB | **0.79x time, 0.26x alloc** | Sharc 3.9x less allocation |
| Insert+Read 100 rows | 4.78 ms / 39.6 KB | 5.72 ms / 71.9 KB | **0.84x time, 0.55x alloc** | Read-back adds ~20 KB |
| Graph node seek | 421 ns / 1,032 B | 26.0 us / 600 B | **0.016x time** | Sharc **62x faster** |
| Graph batch seek (6) | 1.9 us / 3,368 B | 148.9 us / 3,024 B | **0.012x time** | Sharc **80x faster** |
| Graph scan edges | 488 us / 656 B | 2,639 us / 696 B | **0.18x time** | Sharc **5.4x faster** |
| Graph scan nodes | 556 us / 959 KB | 3,119 us / 959 KB | **0.18x time** | Sharc **5.6x faster**, same allocation |
| 100K int scan | 3.8 ms / 18.7 KB | 25.4 ms / 704 B | **0.15x time** | Sharc **6.7x faster** |
| Metadata parse | 7.8 ns / 0 B | 752 ns / 408 B | **0.01x time** | Sharc **96x faster** (struct parse, zero alloc) |

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

## Execution Tier Comparison (DIRECT vs CACHED vs JIT) — v3 Hot Path Optimized

**Dataset:** 2,500 rows × 8 columns (id, name, email, age, score, active, dept, created)
**Optimizations:** ScanMode jump table dispatch, generic cursor specialization, batch single-byte varint decode, MoveNext branch elimination, zero-copy filter path, cursor reuse (PreparedQuery)

### Filtered Scan — `WHERE age > 30` (~2,000 matching rows)

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| DIRECT: `Query(sql)` | 82.78 us | 1.00 | 720 B | 1.00 |
| CACHED: `Query(hint sql)` | 91.63 us | 1.11 | **0 B** | **0.00** |
| JIT: `Query(hint sql)` | 96.29 us | 1.17 | **0 B** | **0.00** |
| Manual `Prepare().Execute()` | 91.31 us | 1.11 | **0 B** | **0.00** |
| Manual `Jit().Query()` | 87.55 us | 1.06 | 48 B | 0.07 |

### Full Scan — `SELECT *` (5,000 rows, no filter)

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| DIRECT: `SELECT *` | 73.04 us | 1.00 | 720 B | 1.00 |
| CACHED: `SELECT *` | 72.81 us | 1.00 | **0 B** | **0.00** |
| JIT: `SELECT *` | **68.14 us** | **0.93** | **0 B** | **0.00** |

### Narrow Projection — 3 string columns (5,000 rows)

| Method | Mean | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|
| DIRECT: narrow | 138.66 us | 1.00 | 7.57 | 97,968 B | 1.00 |
| CACHED: narrow | 154.81 us | 1.12 | 7.57 | 97,248 B | 0.99 |
| JIT: narrow | 155.13 us | 1.12 | 7.57 | 97,248 B | 0.99 |

### Parameterized Filter — `WHERE age > @p0` (bound parameter)

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| DIRECT: parameterized | 81.47 us | 1.00 | 776 B | 1.00 |
| CACHED: parameterized | 89.77 us | 1.11 | 56 B | 0.07 |
| Manual Prepare: parameterized | 96.69 us | 1.19 | 56 B | 0.07 |

### Key Allocation Achievement

CACHED, JIT, and Manual Prepare achieve **true zero allocation** (0 B) for non-materialized queries — the BenchmarkDotNet MemoryDiagnoser reports no managed allocation at all. Cursor reuse + reader reset eliminates all per-call construction overhead. Only DIRECT (720 B for query parse + reader) and Manual Jit (48 B for column array) allocate on clean queries. Parameterized CACHED/Prepare allocate 56 B for parameter binding.

Narrow projection is **string-allocation dominated** (~97 KB). The 720 B DIRECT overhead is invisible at this scale. CACHED saves 720 B of cursor construction but the 97 KB string materialization dwarfs any dispatch savings. Gen0 collections confirm this is all ephemeral (no Gen1/Gen2 pressure).

### Timing Notes

Absolute times are thermal-sensitive on laptop hardware (±15-20% baseline shift between runs). Ratios are more stable but also fluctuate. Across multiple runs:
- **Full Scan**: JIT consistently achieves **0.93x** vs DIRECT — the clearest win, no filter evaluation noise
- **Filtered Scan**: CACHED ranges **0.83-1.11x**, JIT ranges **0.76-1.17x** — highly thermal-dependent, no consistent winner
- **Narrow Projection**: DIRECT consistently wins (~1.12x advantage) — string allocation cost dominates and DIRECT's simpler call stack has less overhead per materialization call
- **Parameterized**: CACHED at **1.11x** with 93% allocation reduction (776→56 B) — parameter binding adds 56 B but eliminates parse + reader construction
- Manual Prepare matches CACHED timing (identical code path via `PreparedQuery.Execute()`)
- Manual Jit is fastest non-DIRECT path for filtered scans (no SQL parse, no plan cache lookup)

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

---

## Allocation Debt Resolution Log (2026-02-23)

Tracked via ADR-023 in `DecisionLog.md`. Summary of structural allocation gaps and their resolution status.

### Resolved

| Gap | Before | After | How |
| :--- | :--- | :--- | :--- |
| WHERE predicate overhead | 1,089 KB per scan | **0 B** (prepared) / 680 B (cold) | `FilterStar`/`CompileBaked` closure-composed predicates, `ConcreteFilterNode` fast path bypasses `DecodeCurrentRow` |
| GraphEdge per-row allocation | `GraphEdge` struct per row via `GetEdges()` | **0 B per row** via `IEdgeCursor` | `EdgeCursorBase` rents serial types/offsets via `ArrayPool`, reuses cursor via `Reset()`. `GetEdges()` marked `[Obsolete]` |
| OpenMemory() eager schema parse | ~40 KB at `OpenMemory()` | **0 B** at open, deferred | `_schema ??=` lazy init pattern. Verified by `AllocationFixTests.cs` |
| ColumnValue[] per reader | New `ColumnValue[]` per reader | **ArrayPool rental**, reused per row | `ArrayPool<ColumnValue>.Shared.Rent()` + `ArrayPool<long>` (serial types) + `ArrayPool<int>` (offsets). 2-slot `[ThreadStatic]` reader pool. Lazy column decode skips unread columns |
| String materialization in filters | `Encoding.UTF8.GetString()` per filter eval | **0 B** via `RawByteComparer` | `SequenceCompareTo`, `StartsWith`, `EndsWith`, `IndexOf` on raw UTF-8 spans. `GetUtf8Span()` public API for callers |

### Remaining

| Gap | Current State | Path Forward |
| :--- | :--- | :--- |
| `Utf8SetContains()` IN/NotIn | Allocates one `string` per row for set membership check | Convert to `Dictionary<ReadOnlyMemory<byte>>` with UTF-8 span comparer |
| Append-only write mode | No dedicated path; full B-tree mutator for all writes | Design fast-path for sequential inserts (skip split checks, append to rightmost leaf) |
