# PlanStoredProcedures — Pre-Compiled Query Delegates

**Status**: Foundation (performance expectations documented)
**Priority**: Medium — natural evolution of existing cache infrastructure
**Target**: v1.3.0+

---

## Problem Statement

Today, every `db.Query(sql)` call follows the full pipeline:

```text
SQL string → Parse → View Resolution → Plan Cache → Intent Compilation
→ Filter Compilation → Cursor Creation → Execution
```

The `QueryPlanCache` and `_readerInfoCache` already eliminate most of this cost on repeat calls. But the user-facing API is still string-based — every call re-hashes the SQL, checks the plan cache, resolves views, and builds intent objects before reaching the cached execution path.

**Stored procedures** formalize what the cache already does: pre-compile everything once, return a typed handle, and execute with zero parse/bind overhead on subsequent calls.

---

## Current Pipeline Performance (Baseline)

All measurements from `PRC/PerformanceBaseline.md` — 5,000 rows, .NET 10.0.2, i7-11800H.

### Full Pipeline (Cold — First Call)

| Operation | Latency | Allocation | Path |
|-----------|---------|------------|------|
| SELECT * (3 cols) | 105 us | 672 B | Parse → Intent → CreateReader → Scan |
| WHERE filter | 298 us | 912 B | + FilterTreeCompiler.CompileBaked |
| FilterStar (closure) | 304 us | 800 B | + IntentToFilterBridge → BakedDelegate |
| Point lookup | 272 ns | 664 B | B-tree seek, single row |
| Index seek (int) | 1.25 us | 1,456 B | SeekFirst + table seek, 3 rows |
| GROUP BY (5 groups) | 352 us | 5,424 B | StreamingAggregateProcessor |
| ORDER BY + LIMIT 50 | 389 us | 32,672 B | Top-N heap materialization |

### Warm Pipeline (Cache Hit — Repeat Call)

On a cache-hit path (`_readerInfoCache` hit), the pipeline skips:

- SQL parsing (~10-30 us estimated)
- View resolution (~5-15 us for registered views)
- Filter tree compilation (~20-50 us for closure composition)

**Estimated warm path overhead**: ~35-95 us eliminated per cached call.

The remaining cost is:

| Component | Cost | Notes |
|-----------|------|-------|
| QueryPlanCache hash lookup | ~0.5-1 us | String hash + dictionary lookup |
| _readerInfoCache lookup | ~0.3-0.5 us | QueryIntent hash + dictionary lookup |
| Cursor construction | ~0.2-0.5 us | B-tree root page seek |
| **Hot-path scan** | **bulk of time** | MoveNext + accessor: zero-allocation |

### Execution-Only Baseline (Theoretical Floor)

These represent the irreducible cost — raw cursor scan with no pipeline overhead:

| Operation | Latency | Allocation | How Measured |
|-----------|---------|------------|--------------|
| DirectTable_SequentialScan | 220 us | 672 B | `CreateReader("users", ...)` directly |
| Sharc_TypeDecode | 176 us | 688 B | Full scan, type decode only |
| Sharc_PointLookup | 272 ns | 664 B | Direct B-tree seek |

**Key insight**: `SELECT *` at 105 us vs DirectTable at 220 us suggests the SQL path is already highly optimized — the apparent 2x gap is due to column projection (3 cols vs all 9), not pipeline overhead.

---

## Stored Procedure Design

### Core Concept

A stored procedure is a pre-compiled query handle that captures:

1. **QueryIntent** — parsed, resolved, immutable
2. **CachedReaderInfo** — table metadata, column projection, rowid alias ordinal
3. **IFilterNode** (optional) — pre-compiled closure-composed filter delegate
4. **Index plan** (optional) — pre-selected index for seek operations

This is exactly what `_readerInfoCache` already stores. The stored procedure formalizes it as a public API.

### Proposed API

```csharp
// Compile once
var getUser = db.Prepare("SELECT name, email FROM users WHERE id = @id");

// Execute many times — skips parse, bind, cache lookup
using var reader = getUser.Execute(new { id = 42 });

// Parameterized filter — closure re-composed only when param types change
using var reader2 = getUser.Execute(new { id = 99 });
```

### Internal Representation

```csharp
public sealed class PreparedQuery : IDisposable
{
    // Pre-resolved at Prepare() time — never re-computed
    internal readonly TableInfo Table;
    internal readonly int[]? Projection;
    internal readonly int RowidAliasOrdinal;
    internal readonly IndexPlan? IndexPlan;

    // Pre-compiled for non-parameterized filters
    internal readonly IFilterNode? StaticFilterNode;

    // Parameterized filter cache (param hash → IFilterNode)
    internal readonly ConcurrentDictionary<long, IFilterNode>? ParamFilterCache;

    // Post-processing config (ORDER BY, LIMIT, GROUP BY)
    internal readonly QueryIntent Intent;
}
```

---

## Performance Targets

### Stored Procedure Execution (Target)

| Operation | Current (SQL) | Target (Prepared) | Savings | Notes |
|-----------|---------------|-------------------|---------|-------|
| SELECT * (3 cols, 5K rows) | 105 us / 672 B | 95 us / 640 B | ~10 us | Eliminate parse + plan cache lookup |
| WHERE filter (5K rows) | 298 us / 912 B | 270 us / 720 B | ~28 us | Skip filter compilation on cache hit |
| Point lookup (1 row) | 272 ns / 664 B | 250 ns / 640 B | ~22 ns | Skip everything — direct seek |
| Index seek (3 rows) | 1.25 us / 1,456 B | 1.10 us / 1,400 B | ~150 ns | Pre-selected index plan |
| Parameterized WHERE | 298 us / 912 B | 275 us / 760 B | ~23 us | Param filter cache hit |

**Conservative targets** — the existing cache infrastructure already eliminates most overhead. Stored procedures primarily eliminate the dictionary lookups and hash computations.

### Operator-Level Budgets

These are the per-operation costs that stored procedures must not exceed:

| Operator | Budget | Rationale |
|----------|--------|-----------|
| Cursor construction | ≤ 640 B | Current baseline minus cache overhead |
| Filter delegate invocation | 0 B per row | Closure-captured, zero-alloc hot path |
| Column projection | 0 B per row | int[] index into record span |
| B-tree seek | ≤ 300 ns | Current point lookup baseline |
| B-tree MoveNext | ≤ 40 ns/row | Current scan rate (5K rows in 200 us) |
| Record decode (projected) | ≤ 15 ns/col | Varint + serial type + span slice |
| Index seek (integer) | ≤ 1.2 us | Current SeekFirst + table seek |
| Index seek (text) | ≤ 185 us | Current UTF-8 byte-span comparison |

### Allocation Budgets by Tier

| Tier | Allocation | What's Included |
|------|-----------|-----------------|
| Tier 0 (read-only scan) | ≤ 640 B | Cursor + reader construction only |
| Tier 0.5 (index seek) | ≤ 1,400 B | + ArrayPool serial type buffer |
| Tier 1 (filtered scan) | ≤ 720 B | + filter node reference (pre-compiled) |
| Tier 2 (aggregation) | ≤ 5,000 B | + streaming aggregator state |
| Tier 3 (ORDER BY + LIMIT) | ≤ 32,000 B | + top-N heap (reducible target) |

---

## Foundation Already Built

The closure-based delegate composition (replacing System.Linq.Expressions) directly enables stored procedures:

### What Exists Today

| Component | Location | Role in Stored Procedures |
|-----------|----------|--------------------------|
| `CachedReaderInfo` | [SharcDatabase.cs:77-83](src/Sharc/SharcDatabase.cs#L77-L83) | The stored procedure's pre-compiled state |
| `_readerInfoCache` | [SharcDatabase.cs:73-74](src/Sharc/SharcDatabase.cs#L73-L74) | Intent → compiled reader (effectively a proc cache) |
| `_paramFilterCache` | [SharcDatabase.cs:75](src/Sharc/SharcDatabase.cs#L75) | Param hash → IFilterNode (param-level caching) |
| `QueryPlanCache` | [SharcDatabase.cs:64](src/Sharc/SharcDatabase.cs#L64) | SQL string → QueryPlan (parse cache) |
| `FilterStarCompiler` | [FilterStarCompiler.cs](src/Sharc/Filter/FilterStarCompiler.cs) | Closure-composed BakedDelegate — AOT-safe |
| `JitPredicateBuilder` | [Filter/JitPredicateBuilder.cs](src/Sharc/Filter/JitPredicateBuilder.cs) | Individual predicate → closure delegate |
| `BakedDelegate` | [Filter/IFilterStar.cs](src/Sharc/Filter/IFilterStar.cs) | `(ReadOnlySpan<byte>, ReadOnlySpan<long>, ReadOnlySpan<int>, long) → bool` |
| `IndexSelector` | [Query/IndexSelector.cs](src/Sharc/Query/IndexSelector.cs) | Best index selection for sargable conditions |
| `CreateReaderFromIntent` | [SharcDatabase.cs:543-631](src/Sharc/SharcDatabase.cs#L543-L631) | The method that stored procedures will wrap |

### What `Prepare()` Would Do Differently

```text
Current (db.Query):
  SQL → [Parse] → [ViewResolve] → [PlanCache] → [IntentBuild] → [ReaderInfoCache] → Execute

Stored Procedure (db.Prepare + proc.Execute):
  Prepare: SQL → Parse → ViewResolve → IntentBuild → FilterCompile → IndexPlan → PreparedQuery
  Execute: PreparedQuery → CursorCreate → Execute
                           (skips 4 stages)
```

The `Prepare()` call front-loads all compilation. `Execute()` only creates a cursor and scans.

---

## Detailed Plans

- **Core SQL**: See [PlanCoreStoredProcedures.md](PlanCoreStoredProcedures.md) for pipeline stage cost breakdown, `PreparedQuery` API design, and Phase 0 implementation details.
- **Graph Traversal**: See [PlanGraphStoredProcedures.md](PlanGraphStoredProcedures.md) for `PreparedTraversal` API design, concurrency gains, and Phase 0 implementation details.

---

## Implementation Phases

### Phase 1: `PreparedQuery` Type

- Extract `CachedReaderInfo` fields into a public `PreparedQuery` class
- Add `db.Prepare(sql)` that returns `PreparedQuery`
- `PreparedQuery.Execute(params?)` calls `CreateReader` with pre-compiled state
- No new allocation paths — reuse existing infrastructure

### Phase 2: Parameterized Stored Procedures

- `PreparedQuery` holds a param filter cache internally
- First execution with new param types compiles a new BakedDelegate
- Subsequent calls with same param types hit the cache
- Target: param cache hit adds ≤ 1 us overhead

### Phase 3: Compound Stored Procedures

- Support prepared CTEs (Cotes), JOINs, set operations
- Pre-plan join strategy (hash join build/probe sides)
- Pre-materialize CTE dependency graph

### Phase 4: Write Stored Procedures

- `db.Prepare("INSERT INTO users (name, email) VALUES (@name, @email)")`
- Pre-compiled write path: table lookup, column mapping, cell builder config
- Target: eliminate per-INSERT schema resolution

---

## State-of-the-Art Comparison

### How Stored Procedures Perform Elsewhere

| System | Prepared Statement Overhead | Notes |
|--------|-----------------------------|-------|
| PostgreSQL | ~5-20 us parse savings | `PREPARE` + `EXECUTE`, server-side plan cache |
| SQLite | ~10-50 us parse savings | `sqlite3_prepare_v2` + `sqlite3_step`, statement cache |
| DuckDB | ~50-200 us parse savings | Columnar engine, plan compilation is heavier |
| Redis (Lua scripts) | ~1-5 us | Pre-compiled bytecode, no parse |

### Sharc's Advantage

Sharc's stored procedures will be unusually efficient because:

1. **No network round-trip** — in-process, same address space
2. **No plan optimizer** — B-tree seek vs scan is the only choice, decided at prepare time
3. **No type coercion** — closure-captured constants are already in wire format (byte[])
4. **No lock acquisition** — single-writer, readers are lock-free (snapshot isolation via DataVersion)
5. **Zero-allocation hot path** — BakedDelegate operates on raw page spans

The `Prepare()` → `Execute()` gap should be **≤ 1 us** for non-parameterized queries (just cursor construction). This is 5-50x less overhead than traditional database prepared statements.

---

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Schema changes invalidate prepared queries | Check `DataVersion` on execute; throw if stale |
| Memory leak from accumulated PreparedQuery objects | IDisposable + weak reference option |
| Parameterized filters accumulate unbounded cache entries | LRU eviction on param filter cache (cap at 100 entries) |
| Compound queries (CTEs, JOINs) are harder to pre-plan | Phase 3 — start with simple SELECT/WHERE only |

---

## Success Criteria

1. **`Prepare()` cost**: ≤ 200 us for any single-table query (one-time)
2. **`Execute()` overhead vs direct cursor**: ≤ 1 us for non-parameterized, ≤ 5 us for parameterized
3. **Allocation**: Same tier as `CreateReader` (640-720 B for Tier 0)
4. **Zero behavioral difference**: `db.Prepare(sql).Execute(params)` produces identical results to `db.Query(sql, params)`
5. **AOT/WASM safe**: No dynamic code generation — closure-composed delegates only
