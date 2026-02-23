# Potential JIT Internal Use

> Where JitQuery should be used inside Sharc itself — and where it shouldn't.

## The Core Thesis

JitQuery's compiled-filter pipeline (`IFilterStar` → `BakedDelegate`) is the fastest
row-evaluation path in Sharc. Anywhere the engine currently interprets predicates
at runtime — pattern-matching on node types, recursing through intent trees,
re-resolving column ordinals per call — is a candidate for JIT compilation.

The question isn't "can we use JIT here?" but "does the compilation cost amortize?"

---

## Current Query Execution Costs

| Path | Parse | Plan Cache | View Resolve | Filter Compile | Cursor | Total Overhead |
|------|-------|-----------|-------------|---------------|--------|---------------|
| `db.Query(sql)` | Cached | Hash lookup | Gen check | Cached (static) or per-param | New per call | ~5 lookups + cursor |
| `db.Prepare(sql)` | Once | None | None | Once (static) or per-param | New per call | ~1 lookup + cursor |
| `db.Jit("table")` | Never | Never | Never | Once via `CompileFilters()` | New per call | Cursor only |

JitQuery eliminates everything except cursor creation. That's the floor.

---

## Internal Opportunity Map

### 1. CoteExecutor — Materialized Row Filtering (CRITICAL)

**Location**: `src/Sharc/Query/CoteExecutor.cs`, `ApplyPredicateFilter()` lines 114-144

**Current behavior**: After materializing a CTE's rows into `QueryValue[]` arrays, the engine
filters them by walking the `PredicateIntent` tree **per row**:

```
For each row in materialized set:
  EvalNode(root) →
    match node.Op:
      And → EvalNode(left) && EvalNode(right)
      Eq  → match (node.Value.Kind, row[ordinal].Type):
              (Signed64, Int64) → row.AsInt64() == node.Value.AsInt64
              (Text, String)    → row.AsString() == node.Value.AsText
              ... 50+ pattern arms
```

This is **interpreted execution** — the predicate tree is re-traversed for every row,
with type dispatch at every leaf. For a 10K-row CTE with a 3-node WHERE clause,
that's 30K pattern matches per query.

**JIT approach**: Compile `PredicateIntent` → `Func<QueryValue[], bool>` once, then:

```
var compiled = JitRowFilter.Compile(predicate, columnNames, parameters);
foreach (var row in rows)
    if (compiled(row)) result.Add(row);
```

**Expected benefit**:
- Eliminate per-row `EvalNode()` recursion and pattern matching
- Pre-resolve column ordinals once in the compiled delegate
- Pre-dispatch type comparisons at compile time (no runtime switch)
- Estimated: **4-6x throughput improvement** on filtered CTEs
- Allocation savings: zero ordinal arrays per filter application

**Amortization**: Compiles once per CTE filter. Even for single-execution CTEs,
the compilation cost (~2 µs) is recovered after ~40 rows. Any CTE over 100 rows benefits.

---

### 2. CompoundQueryExecutor — Compound Query Filter Reuse (HIGH)

**Location**: `src/Sharc/Query/CompoundQueryExecutor.cs`, `ExecuteIntent()` line 673

**Current behavior**: Each arm of a UNION/INTERSECT/EXCEPT compiles its filter independently
through `CreateReaderFromIntent()`. The `_readerInfoCache` on `SharcDatabase` caches
by `QueryIntent` reference — but compound queries create new intent references during
CTE resolution, defeating the cache.

```
UNION query:
  Left arm  → CreateReaderFromIntent(leftIntent)   → compile filter A
  Right arm → CreateReaderFromIntent(rightIntent)   → compile filter B
  (Even if filter A == filter B textually, they're different intent objects)
```

**JIT approach**: At CTE resolution time, pre-compile filters and attach them to
the resolved `QueryIntent`. When `ExecuteIntent()` runs, check for a pre-compiled
filter before falling through to `IntentToFilterBridge` + `FilterTreeCompiler`.

**Expected benefit**:
- Eliminate 1-2 filter compilations per compound query execution
- For repeated compound queries (e.g., dashboard refreshes), save ~10-30 µs per call
- Reduces `IntentToFilterBridge` allocations (IFilterStar node tree construction)

**Amortization**: Pays off on the second execution of any compound query with WHERE.

---

### 3. ViewResolver.PreMaterializeFilteredViews — View Materialization (MODERATE)

**Location**: `src/Sharc/Views/ViewResolver.cs`, `PreMaterializeFilteredViews()` lines 218-277

**Current behavior**: When a SQL query references a registered view with a `Func<IRowAccessor, bool>`
filter, the engine materializes the entire view into a `MaterializedResultSet` before
the query executor sees it. This happens on **every** `db.Query()` call that touches
the view.

```
Per db.Query() that references a filtered view:
  1. Open view cursor
  2. Iterate all rows
  3. For each: check Func filter → convert to QueryValue[] → add to RowSet
  4. Store in CoteMap
```

**JIT approach**: If the view's filter was originally constructed from `IFilterStar`
(via `ViewFilterBridge.Convert()`), cache the compiled `BakedDelegate` alongside the view.
On subsequent materializations, skip `Func<IRowAccessor, bool>` and use the faster
byte-level `BakedDelegate` path when the view sources from a table.

**Expected benefit**:
- Faster row evaluation during materialization (BakedDelegate vs Func delegate)
- Could skip materialization entirely if the view is a simple SELECT+WHERE
  by inlining it as a CTE with a compiled filter
- Reduces per-`Query()` overhead for apps that use registered views heavily

**Amortization**: Benefits any app that calls `db.Query(sql)` referencing the same
registered view more than once. The view's filter compilation happens at registration time.

---

### 4. PreparedQuery — Filter Parameter Specialization (LOW-MODERATE)

**Location**: `src/Sharc/PreparedQuery.cs`, `Execute()` lines 77-117

**Current behavior**: `PreparedQuery` caches static (non-parameterized) filters at
`Prepare()` time. For parameterized filters, it caches by parameter-value hash:

```
Execute(parameters):
  paramKey = hash(parameters)
  if _paramCache.TryGetValue(paramKey, out node): use cached
  else: IntentToFilterBridge.Build() → FilterTreeCompiler.CompileBaked() → cache
```

This is already efficient — the cache hit rate is high for apps that reuse the same
parameter values. But the first call with new parameter values pays the full
compilation cost.

**JIT approach**: Pre-compile a **parameterized template** that accepts value slots.
Instead of rebuilding the entire IFilterStar tree per parameter set, compile a delegate
with parameter slots that get filled at execution time:

```
// At Prepare() time:
var template = JitFilterTemplate.Compile(intent.Filter, table.Columns);

// At Execute() time:
var node = template.Bind(parameters);  // Fills slots, no tree walk
```

**Expected benefit**:
- First call with new parameters: ~5-10 µs saved (skip IntentToFilterBridge tree walk)
- Reduces `IFilterStar` node allocations per parameter set
- Tighter delegate closure (no intermediate AST objects)

**Amortization**: Only matters for apps that call `Prepare()` with many distinct
parameter combinations. Most apps reuse <10 parameter sets.

---

### 5. Graph Property Filtering — Future Extension (SPECULATIVE)

**Location**: `src/Sharc.Graph/Store/ConceptStore.cs`, `src/Sharc.Graph/Store/RelationStore.cs`

**Current behavior**: Graph lookups use hardcoded equality checks:
- `ConceptStore.Get(NodeKey)` → `if (barId != key.Value) continue;`
- `RelationStore.CreateEdgeCursor()` → key filter + optional kind filter

These are already zero-GC (no filter compilation, no delegates, no allocations).

**JIT approach**: Not applicable today. Graph queries don't have dynamic predicates.

**Future potential**: If the graph API adds property-based filtering
(e.g., "edges where weight > 0.5 AND label = 'friend'"), JitQuery could pre-compile
those predicates. But this requires an API design change first.

**Expected benefit**: None today. Low priority for future planning.

---

### 6. Auto-Promotion — Hot Query Detection (FUTURE)

**Concept**: Track `db.Query(sql)` call frequency. When a query string exceeds
a threshold (e.g., 10 executions), automatically create an internal JitQuery
and route subsequent calls through it.

**How it would work**:
```
QueryCore(sql):
  plan = cache.GetOrCompile(sql)
  if plan.ExecutionCount > threshold && plan.JitHandle == null:
    plan.JitHandle = BuildJitFromIntent(plan.Simple)  // One-time
  if plan.JitHandle != null:
    return plan.JitHandle.Query()  // Fast path
  else:
    return CreateReaderFromIntent(...)  // Normal path
```

**Expected benefit**:
- Zero API changes — completely transparent optimization
- Hot queries get JitQuery speed without user opting in
- Cold queries pay no overhead (no JitQuery construction)

**Risks**:
- Memory growth from cached JitQuery handles (mitigated by LRU eviction)
- Incorrect promotion of queries with side effects (views mutate generation counter)
- Stale JitQuery if schema changes (mitigated by schema cookie invalidation)

**Recommendation**: Defer until benchmarks prove the plan-cache-to-JitQuery gap is
measurable in real workloads. The existing 3-layer cache (`QueryPlanCache` +
`_readerInfoCache` + `_paramFilterCache`) already narrows the gap significantly.

---

## Where JIT Should NOT Be Used Internally

| System | Why Not |
|--------|---------|
| **Page I/O** | No predicates — pure byte shuffling |
| **BTreeReader/Cursor** | Already hardware-optimized (SIMD seek, zero-copy spans) |
| **RecordDecoder** | Operates on serial types + offsets, no filter logic |
| **SchemaReader** | Cold path (once per database open), no filters |
| **Encryption layer** | AES-GCM is hardware-accelerated, no predicate evaluation |
| **ConceptStore/RelationStore** | Already zero-GC with hardcoded comparisons |
| **WriteEngine (BTreeMutator)** | B-tree splits are structural, not predicate-based |

---

## Priority Matrix

| Opportunity | Impact | Effort | Amortization Threshold | Priority |
|-------------|--------|--------|----------------------|----------|
| CoteExecutor row filtering | Very High | Medium | ~40 rows | **P0** |
| CompoundQuery filter reuse | High | Low | 2nd execution | **P1** |
| ViewResolver materialization | Moderate | Medium | 2nd query | **P2** |
| PreparedQuery param templates | Low-Moderate | High | Many distinct params | P3 |
| Auto-promotion | Moderate | High | 10+ executions | P4 (defer) |
| Graph property filtering | None today | N/A | N/A | Backlog |

---

## Implementation Strategy

**Phase 1** (P0 + P1): Compile predicates to delegates in CoteExecutor and
CompoundQueryExecutor. No public API changes. Internal `JitRowFilter` utility
class that compiles `PredicateIntent` → `Func<QueryValue[], bool>`.

**Phase 2** (P2): Extend ViewResolver to cache compiled BakedDelegates
for registered views. Optional: skip materialization for simple table-sourced views.

**Phase 3** (P3-P4): Parameterized filter templates and auto-promotion.
Requires profiling data from real workloads to justify complexity.

---

## Relationship to External JitQuery API

Internal JIT use does not expand the public API surface. Users never see
the internal `JitRowFilter` or `JitFilterTemplate` classes. The benefit
flows through existing APIs:

- `db.Query(sql)` gets faster for CTEs and compound queries (invisible to user)
- `db.Prepare(sql)` gets faster for parameterized queries (invisible to user)
- `db.Jit("table")` remains the explicit opt-in for programmatic control

This maintains the coherence principle: **one obvious path per use case,
JIT optimizations are invisible plumbing, not additional API surface**.

---

*See also:*
- [JitSQL.md](JitSQL.md) — Full JitSQL syntax specification and Python bindings
- [WildUserScenariosForJitUse.md](WildUserScenariosForJitUse.md) — External user scenarios
- [APIDesign.md](APIDesign.md) — Public API philosophy
- [PerformanceBaseline.md](PerformanceBaseline.md) — Allocation tier definitions
