# Zero-Allocation Hash Join: Paper vs Implementation Diff

**Paper**: *Tiered Zero-Allocation Hash Join for FULL OUTER JOIN in Context Engineering Database Engines*
**Branch**: `Zero.Hash`
**ADR**: ADR-025 in `PRC/DecisionLog.md`

---

## Implemented (Matching Paper Spec)

| Paper Section | Spec | Implementation | Status |
|:---|:---|:---|:---|
| §4, Table 3 | 3-tier dispatch by build cardinality | `JoinTier.Select()` with 256/8,192 thresholds | Match |
| §4, Table 3 | Tier I ≤256, Tier II 257–8,192, Tier III >8,192 | Exact same thresholds | Match |
| §4.2 | Tier II uses `ArrayPool` for markers | `PooledBitArray` backed by `ArrayPool<byte>` | Match |
| §6 | `MergeDescriptor` readonly struct | `MergeDescriptor` with `BuildIsLeft`, 3 emission methods | Match |
| §6 | `MergedWidth`, `LeftColumnCount`, `RightColumnCount`, `BuildIsLeft` | All present | Match |
| §6 | Three emission paths: matched, probe-unmatched, build-unmatched | `MergeMatched`, `EmitProbeUnmatched`, `EmitBuildUnmatched` | Match |
| §4.3 | ArrayPool-backed open-address hash table | `OpenAddressHashTable<TKey>` with parallel arrays | Match |
| §4.3 | Backward-shift deletion (no tombstones) | `BackwardShiftDelete` + `ShouldShift` | Match |
| §5 | Drain-then-remove for duplicate keys | `RemoveAll` does single-scan batch removal | Match |
| §8.5, Table 8 | 10-scenario correctness matrix across all 3 tiers | `TieredHashJoinCorrectnessMatrixTests` — 30 tests | Match |
| §8.5 | NULL keys don't match (SQL semantics) | `if (key.IsNull) continue` / `if (!key.IsNull)` guards | Match |
| §4.2 | Marker bits invisible to GC | `ArrayPool<byte>.Shared.Rent` / `.Return` | Match |

---

## Divergences (Deliberate Design Changes)

### D-1: Tier I Uses PooledBitArray Instead of stackalloc

**Paper**: Tier I uses `stackalloc bool[]` (256 bytes on stack, freed on method return).

**Implementation**: `PooledBitArray` (32 bytes from `ArrayPool<byte>`).

**Reason**: C# `yield return` (iterator methods) cannot use `stackalloc`. The compiler generates a state machine class — there is no stack frame to allocate on. `PooledBitArray` at 32 bytes for 256 bits is equally L1-resident and equally GC-invisible. The net effect is identical: zero GC-visible allocation, L1 cache residence.

### D-2: Bit-Packed Markers Instead of bool[]

**Paper** (§4.1–4.2): `stackalloc bool[]` (Tier I, 1 byte per row = 256 bytes) and `ArrayPool<bool>` (Tier II, 1 byte per row = 8,192 bytes).

**Implementation**: `PooledBitArray` using `ArrayPool<byte>` with bit packing (1 bit per row). Tier I = 32 bytes. Tier II = 1,024 bytes.

**Reason**: 8x more cache-efficient. This is strictly superior to the paper's design. For 8,192 rows, our tracker occupies 1 KB (fits in L2) vs the paper's 8 KB (may spill from L2 on some microarchitectures).

### D-3: Tier III Uses Read-Only Lookup + PooledBitArray Instead of Destructive Probe

This is the largest divergence from the paper and the only one driven by a correctness bug in the paper's design.

#### Paper's Design (§4.3, §5)

The paper's Tier III operates in three phases:

1. **Build**: Hash build rows into an ArrayPool-backed open-address table.
2. **Probe**: For each probe row, find matches, emit joined rows, then **remove** matched entries from the table. Probe rows with no match emit with build-side NULLs.
3. **Residual**: Iterate the hash table — whatever remains is the unmatched build set, by construction. No conditional check needed — the scan is unconditional.

The elegance is that the hash table *becomes* the unmatched set. No separate marker structure (PooledBitArray, BitArray, bool[]) is needed. This is how Tier III achieves 0 B marker allocation.

The paper identifies one hazard: **duplicate build keys**. If the build table has `{key=1, row=A}` and `{key=1, row=B}`, naive remove-on-first-match would drop row B. Section §5 solves this with the **drain-then-remove protocol**:

```csharp
// §5 pseudocode: drain all matches, then batch-remove
Span<int> slots = stackalloc int[16];
int count = 0;
for (int slot = First(key); slot != EMPTY; slot = Next(slot))
    if (Eq(slot, key)) {
        Emit(probeRow, buildRows[slot]);
        slots[count++] = slot;
    }
for (int i = 0; i < count; i++) Remove(slots[i]);
```

This correctly handles duplicate build keys. The drain phase reads all matches before any modification occurs.

#### The Bug: Duplicate Probe Keys

The paper does not address what happens when the **probe side** has duplicate keys. Standard SQL FULL OUTER JOIN semantics require a Cartesian product on matching keys:

```sql
-- Build: (1, 'A'), (1, 'B')
-- Probe: (1, 'X'), (1, 'Y')
-- Expected: 4 matched rows (Cartesian on key=1)
--   (X, A), (X, B), (Y, A), (Y, B)
-- Plus: 0 probe-unmatched, 0 build-unmatched
```

With destructive probe:

| Step | Action | Hash Table State | Output |
|:---|:---|:---|:---|
| 1 | Probe row X: drain key=1 | `{(1,A), (1,B)}` | Emit (X,A), (X,B) |
| 2 | Probe row X: remove A, B | `{}` (empty) | -- |
| 3 | Probe row Y: lookup key=1 | `{}` (empty) | **Emit (NULL, NULL, Y, ...)** |

Result: `(X,A), (X,B), (NULL,NULL,Y,...)` — **3 rows instead of 4**. Probe row Y is incorrectly classified as unmatched because the first probe row already removed all build entries with that key.

This was discovered during Phase 4 (correctness matrix) when test case C4 (duplicate keys on both sides, n=9000) expected 4 matched rows but got 2. The test is `TieredHashJoinCorrectnessMatrixTests.C4_DuplicateBothSides_TierIII`.

#### Why Drain-Then-Remove Doesn't Help

The drain-then-remove protocol (§5) operates **per probe row**. It correctly drains all build matches before removing any. But it has no mechanism to know that future probe rows may also need those same build entries. Once the first probe row with key=1 completes its drain-then-remove cycle, the build entries are gone — permanently.

Possible fixes within the destructive-probe paradigm:

1. **Track "already seen" probe keys**: Before removing, check if any future probe row might need the same key. This requires materializing or pre-scanning the probe side, destroying the streaming property.
2. **Only remove on last probe occurrence**: Count probe-side duplicates per key upfront. Decrement on each probe. Remove build entries only when counter hits zero. This requires a `Dictionary<key, int>` of probe-key frequencies — an O(P) allocation that contradicts the zero-allocation goal.
3. **Accept the bug for unique-probe-key workloads**: The paper's target (context engineering queries) likely has unique probe keys in >99% of cases. But a SQL-compliant join operator must handle all valid inputs.

We chose none of these. Instead:

#### Our Design

Tier III uses the same matched-tracking pattern as Tier I/II — `PooledBitArray` for marking which build rows were matched — but with `OpenAddressHashTable` instead of `Dictionary` for cache-efficient lookup at large build sizes:

```
Build: Hash build rows into OpenAddressHashTable (ArrayPool-backed, cache-friendly).
Probe: For each probe row, GetAll(key) → read-only lookup. Mark matched build indices in PooledBitArray.
Residual: Scan PooledBitArray for unmatched build rows (conditional check per row).
```

The hash table is **never modified** during probe. Every probe row sees the full build set.

| Step | Action | Hash Table State | PooledBitArray | Output |
|:---|:---|:---|:---|:---|
| 1 | Probe row X: GetAll(1) | `{(1,A), (1,B)}` | `matched[A]=1, matched[B]=1` | Emit (X,A), (X,B) |
| 2 | Probe row Y: GetAll(1) | `{(1,A), (1,B)}` | `matched[A]=1, matched[B]=1` | Emit (Y,A), (Y,B) |
| 3 | Residual: scan bits | -- | All set | 0 build-unmatched |

Result: `(X,A), (X,B), (Y,A), (Y,B)` — **correct**.

#### Trade-Offs

| Dimension | Paper (Destructive Probe) | Ours (Read-Only + BitArray) |
|:---|:---|:---|
| Marker allocation | 0 B (no marker needed) | O(n/8) bytes from ArrayPool (GC-invisible) |
| Residual scan | O(unmatched) unconditional | O(n) conditional (bit check per row) |
| Duplicate probe keys | **Incorrect** | Correct |
| Hash table after probe | Consumed (single-use) | Intact (reusable) |
| Requires §9 framework | Yes (liveness, fusion, copy-on-consume, JIT recompile) | **No** — no single-use constraint |

The O(n/8) marker allocation is GC-invisible (ArrayPool). For 10K build rows, it's 1.25 KB — well within L2 cache. The conditional residual scan adds ~1-2 microseconds vs unconditional iteration — negligible compared to the probe phase.

#### Cascade Effect: §9 Becomes Unnecessary

The paper's entire Section 9 (tier-aware query planning framework — 5 strategies, ~3 pages) exists to solve Tier III's single-use constraint. Because our Tier III doesn't consume the hash table:

- **§9.1 Liveness analysis**: Not needed — no refcount tracking required.
- **§9.2 Pipeline fusion**: Already natural — `IEnumerable` chaining in JoinExecutor.
- **§9.3 Copy-on-consume**: Not needed — no materialization for second consumer.
- **§9.4 Speculative JIT recompile**: Not needed — no `HashTableConsumedException`.
- **§9.5 Decision framework**: Not needed — all 5 topology rules address a constraint that doesn't exist.

This simplifies the overall architecture significantly. The join operator is a pure function of its inputs with no side effects on the hash table — easier to reason about, test, and compose.

### D-4: Hash Table Type Varies by Tier

**Paper**: Does not specify hash table type per tier (implies a single hash table design).

**Implementation**: Tier I/II use `Dictionary<QueryValue, List<int>>` (well-optimized by .NET runtime for small/medium sizes). Tier III uses `OpenAddressHashTable<TKey>` (ArrayPool-backed parallel arrays for cache-friendly large builds).

### D-5: Null Fill Instead of Pre-Allocated Null Rows

**Paper** (§6): `MergeDescriptor` stores pre-allocated `LeftNullRow` and `RightNullRow` arrays, reused across executions.

**Implementation**: Uses `Span<QueryValue>.Fill(QueryValue.Null)` inline, writing directly into the scratch buffer.

**Reason**: Eliminates 2 array allocations (`object?[]` null rows). `Span.Fill` is a single memset-like operation — no separate null row objects needed.

### D-6: No \_consumed Flag

**Paper** (§9): Tier III hash table has a `_consumed` flag enforcing single-use constraint.

**Implementation**: No consumed flag. The hash table is read-only during probe (D-3), so there is no single-use constraint.

---

## Not Implemented (Paper Sections Without Code)

### N-1: Plan-Level Liveness Analysis (§9.1)

Count downstream consumers per intermediate result. Force Tier I/II when refcount > 1.

**Gap Assessment**: Not needed. Our Tier III doesn't consume the hash table (D-3), so there's no single-use constraint to enforce.

### N-2: Pipeline Fusion (§9.2)

Chain FULL JOINs streaming without intermediate materialization.

**Gap Assessment**: Not needed (same reason). JoinExecutor already streams via `IEnumerable` chaining naturally.

### N-3: Copy-on-Consume with Lazy Materialization (§9.3)

`ConsumableHashTable` that rebuilds from materialized rows for second consumer.

**Gap Assessment**: Not needed (D-3 eliminates the problem).

### N-4: Speculative Tier Selection with JIT Recompilation (§9.4)

Optimistically select Tier III for all joins. On `HashTableConsumedException`, recompile plan with forced marker tier.

**Gap Assessment**: Not needed (D-3). Would require `QueryPlanCache` integration and exception-based control flow.

### N-5: Composed Tier-Aware Decision Framework (§9.5, Table 9)

Decision matrix for bushy trees, CTEs, correlated subqueries.

**Gap Assessment**: Not needed (D-3). All 5 topology rules address the single-use constraint that D-3 eliminates.

### N-6: Isolated 0 B Allocation Proof (§8.1, Table 5)

Paper claims all tiers report 0 B managed heap via BenchmarkDotNet MemoryDiagnoser.

**Gap Assessment**: We achieve 0 B for the **emission path** (with `reuseBuffer`), but the hash table build phase allocates: `Dictionary` (Tier I/II), `List<int>` value lists, `OpenAddressHashTable` arrays (Tier III, pooled but tracked). The paper's 0 B claim appears to exclude build-phase infrastructure or measures only the marker/probe/residual path in isolation.

### N-7: Isolated Probe Throughput (§8.2, Table 6)

M rows/s at 50% match rate, unique integer keys, per-tier.

**Gap Assessment**: We benchmark end-to-end (full pipeline: parse, scan, build, probe, project). No isolated probe-only microbenchmark exists yet.

### N-8: Concurrent Agent Simulation (§8.3, Table 7)

100 concurrent agents, p50/p99 tool-call latency, Gen0/Gen2 collection counts, total GC pause time.

**Gap Assessment**: Not measured. Would need a multi-threaded harness with GC event monitoring (`GC.RegisterForFullGCNotification` or `EventPipe`).

### N-9: Token Budget Protocol (§7.2)

Sort matched rows by relevance, accumulate until token budget exhausted.

**Gap Assessment**: Application-layer feature, not a join operator concern.

### N-10: GC-Transparent Execution (§11.3)

Extend zero-alloc to GROUP BY, ORDER BY, window functions.

**Gap Assessment**: Future work in both paper and implementation.

---

## Additions Beyond the Paper

### A-1: reuseBuffer Parameter

Eliminates per-row `CopyRow` allocation when downstream projects. A single scratch `QueryValue[]` is yielded on every iteration; `ProjectRows` reads the buffer before advancing. Paper mentions null row reuse but not scratch buffer reuse across the entire emission path.

**Impact**: -33% allocation at 5K rows (3,575 KB → 2,403 KB).

### A-2: RemoveAll Batch Optimization

Single-scan with `i--` re-check after backward-shift. Paper's drain-then-remove (§5) collects matching slots into a `stackalloc int[16]` buffer then batch-deletes. Our approach is simpler — no slot buffer needed, and handles arbitrary duplicate counts without ArrayPool fallback.

### A-3: QueryValueKeyComparer

Custom `IEqualityComparer<QueryValue>` handling cross-type int64/double equality (e.g., `42L == 42.0`). Paper doesn't address heterogeneous key types.

### A-4: GetAll on OpenAddressHashTable

Non-destructive multi-value lookup: `GetAll(key, List<int>)` collects all matching indices without modifying the table. Paper only describes destructive removal operations. We need this because Tier III uses read-only lookup + PooledBitArray (D-3).

---

## Benchmark Comparison

### Paper Claims (§8.2, Table 6)

| Build Size | Marker-bit | Sharc Tier | Speedup |
|:---|:---|:---|:---|
| 100 | 14.2 M rows/s | 19.8 M rows/s (Tier I) | 1.39x |
| 1,000 | 12.8 M rows/s | 13.1 M rows/s (Tier II) | 1.02x |
| 10,000 | 9.4 M rows/s | 10.1 M rows/s (Tier III) | 1.07x |

### Our Measured Results (End-to-End with reuseBuffer)

| Benchmark | Users | Time | Allocated | vs LEFT JOIN |
|:---|:---|:---|:---|:---|
| FULL OUTER (Tier I) | 1,000 | 749 us | 630 KB | 0.99x time, 0.99x alloc |
| FULL OUTER (Tier II) | 5,000 | 4,063 us | 2,403 KB | 0.80x time, 0.75x alloc |
| LEFT JOIN baseline | 1,000 | 758 us | 634 KB | -- |
| LEFT JOIN baseline | 5,000 | 5,056 us | 3,188 KB | -- |

FULL OUTER JOIN is faster and allocates less than LEFT JOIN at 5,000 rows.

---

## Summary

**Core design**: 5 of 6 paper primitives implemented (JoinTier, MergeDescriptor, PooledBitArray, OpenAddressHashTable, TieredHashJoin). The 6th (ConsumableHashTable) is unnecessary due to D-3.

**Biggest divergence**: Tier III uses PooledBitArray + read-only lookup instead of destructive probe. This is a correctness fix — the paper's destructive probe fails for duplicate probe keys.

**Biggest gap**: §9 (tier-aware query planning framework — 5 strategies) is entirely unimplemented. However, all 5 strategies exist solely to address the single-use constraint of destructive probe, which D-3 eliminates.

**Net assessment**: The implementation delivers the paper's core value proposition (tiered zero-allocation matched tracking, MergeDescriptor, cache-aware tier selection) while being strictly more correct on duplicate probe keys and strictly more cache-efficient on marker storage (bit-packed vs byte-per-row).

---

## Path to Full Paper Parity

If future workloads require true destructive probe (builds >100K where PooledBitArray scan cost dominates), the implementation path is:

1. Fix the duplicate-probe-key bug in destructive probe (track per-probe-key "already drained" state, or accept Cartesian semantics only for unique probe keys)
2. Add `_consumed` flag to `OpenAddressHashTable`
3. Implement §9.1 liveness analysis in `ExecutionRouter` or `QueryPlanCache`
4. Implement §9.3 copy-on-consume as `ConsumableHashTable` wrapper
5. Implement §9.4 speculative recompilation in `QueryPlanCache`

Estimated effort: 3-5 days. Trigger: benchmark evidence that PooledBitArray scan at >100K rows is a bottleneck.
