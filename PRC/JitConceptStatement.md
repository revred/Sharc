# JIT Prepared Statements — Scoped Filter Composition

**Status**: Concept (design complete, not yet implemented)
**Depends on**: `PreparedQuery` (implemented), `FilterStarCompiler` (implemented)
**Priority**: Medium — natural extension of PreparedQuery for caller-specific customization

---

## Problem Statement

`PreparedQuery` pre-compiles everything at `Prepare()` time: SQL parse → intent → table resolution → filter compilation → projection. `Execute()` skips all that overhead (~2.5-6.5 us saved per call).

But PreparedQuery's filter is **fixed at prepare time**. Callers often need to add context-specific filtering that depends on local variables, business logic state, or runtime conditions. Today they must either:

1. **Parameterized queries** — limited to WHERE clause values, can't add new predicates
2. **Fall back to `db.Query()`** — loses all PreparedQuery benefits (re-parse, re-compile)
3. **Post-filter in application code** — wastes I/O on rows that should have been skipped at the byte level

**JIT Prepared Statements** solve this by adding a scoped customization layer on top of PreparedQuery that composes caller-specific filters at the `BakedDelegate` level (zero-alloc hot path), applies limit/offset overrides, executes, and discards all JIT state when the scope exits.

---

## Core Concept: "Push the Stack, Pop the Stack"

```text
PreparedQuery (persistent)          JIT Layer (ephemeral)
┌──────────────────────┐           ┌──────────────────────┐
│ Table, Projection    │           │ .Where(score > 90)   │
│ StaticFilter (baked) │ ──AND──▶  │ .WithLimit(50)       │
│ Intent, PostProcess  │           │ .Execute(params)     │
└──────────────────────┘           └──────────────────────┘
       persists                     discarded after Execute
```

The PreparedQuery is the **stack frame** that persists across calls. The JIT layer is **pushed** on top with caller-local closures and variables, executes, and is **popped** when the scope exits. The PreparedQuery is completely untouched.

### What Gets Composed

| Layer | Resolved when | Lifetime | Cost |
|-------|--------------|----------|------|
| SQL parse → QueryIntent | `Prepare()` | PreparedQuery lifetime | 0 at Execute |
| Table + projection resolution | `Prepare()` | PreparedQuery lifetime | 0 at Execute |
| Static filter (non-parameterized WHERE) | `Prepare()` | PreparedQuery lifetime | 0 at Execute |
| Parameterized filter (cached by param hash) | First `Execute(params)` | PreparedQuery lifetime | ~0.1 us cache hit |
| **JIT filter (caller closures)** | **`Jit().Execute()`** | **Single Execute scope** | **~1-2 us compile** |
| **JIT limit/offset override** | **`Jit().Execute()`** | **Single Execute scope** | **~0** |

---

## API Design

```csharp
// PreparedQuery persists across calls — expensive work done once
using var prepared = db.Prepare("SELECT name, age, score FROM users WHERE dept = $dept");

// ── Pattern 1: Inline JIT (most common) ──
// Caller captures local variables in closures, filters at byte level, pops when done
double threshold = GetCurrentThreshold();
using var reader = prepared.Jit()
    .Where(FilterStar.Column("score").Gt(threshold))  // closure captures local
    .Where(FilterStar.Column("active").Eq(1L))         // multiple .Where() → AND
    .WithLimit(50)                                      // tighter limit for this call
    .Execute(new Dictionary<string, object> { ["dept"] = "Engineering" });

while (reader.Read()) { ... }
// reader disposed → JIT filter discarded, PreparedQuery untouched

// ── Pattern 2: Reusable JIT builder ──
// Same JIT config, multiple executions (each gets a fresh cursor)
var jit = prepared.Jit()
    .Where(FilterStar.Column("level").Eq("ERROR"))
    .WithLimit(100);

using var batch1 = jit.Execute();
// ... process ...
using var batch2 = jit.Execute();  // fresh cursor, same composed filter

// ── Pattern 3: JIT with parameterized base ──
// Combines PreparedQuery parameters WITH JIT closures
int minAmount = GetMinOrderAmount();
using var reader2 = prepared.Jit()
    .Where(FilterStar.Column("amount").Gte(minAmount))
    .WithLimit(20)
    .Execute(new Dictionary<string, object> { ["dept"] = "Sales" });
```

---

## Filter Composition Strategy

The composition happens at the `BakedDelegate` level — the same closure-composition technique used by `FilterStarCompiler`. This preserves the zero-alloc hot path: a single `FilterNode.Evaluate()` call per row with a single stackalloc for offset hoisting.

```text
1. Merge all JIT .Where() calls into one IFilterStar via FilterStar.And()
2. Compile JIT filter: FilterStarCompiler.Compile() → BakedDelegate
3. Collect JIT ordinals: FilterTreeCompiler.GetReferencedColumns()
4. If base FilterNode exists:
   a. Extract base BakedDelegate via filterNode.CompiledDelegate
   b. Merge ordinals: HashSet<int>(base.ReferencedOrdinals ∪ jitOrdinals)
   c. Compose: (p,s,o,r) => base(p,s,o,r) && jit(p,s,o,r)
   d. Wrap in new FilterNode(composed, mergedOrdinals)
5. If no base filter: wrap JIT alone in FilterNode
6. Pass composed FilterNode to SharcDataReader via CursorReaderConfig
```

### Why BakedDelegate-Level, Not IFilterNode-Level

`SharcDataReader.Read()` checks `_concreteFilterNode` (cast to `FilterNode`) for a direct call path, bypassing `IFilterNode` virtual dispatch. If we composed via `AndNode` wrapping two `IFilterNode` children, we'd lose:

- **Offset hoisting**: Two separate stackalloc passes instead of one
- **Direct call**: Falls back to virtual dispatch through `IFilterNode.Evaluate()`
- **Short-circuit locality**: Two delegate invocations through the interface

By composing at the `BakedDelegate` level, the composed closure is wrapped in a single `FilterNode`, preserving the optimized hot path.

---

## Type Design

### JitQuery (Builder)

```csharp
public sealed class JitQuery
{
    private readonly PreparedQuery _prepared;
    private List<IFilterStar>? _jitFilters;
    private long? _limitOverride;
    private long? _offsetOverride;

    internal JitQuery(PreparedQuery prepared);

    public JitQuery Where(IFilterStar filter);    // AND-chains, returns this
    public JitQuery WithLimit(long limit);         // overrides LIMIT
    public JitQuery WithOffset(long offset);       // overrides OFFSET
    public SharcDataReader Execute();              // no params
    public SharcDataReader Execute(IReadOnlyDictionary<string, object>? parameters);
}
```

**Not disposable** — holds no unmanaged resources. The returned `SharcDataReader` is the only disposable.

### Lifecycle

```text
PreparedQuery ──▶ Jit() ──▶ .Where() ──▶ .Where() ──▶ .WithLimit() ──▶ Execute()
                  │         │             │             │                │
                  │         builds List<IFilterStar>    sets override    │
                  │                                                      │
                  creates JitQuery                       calls ExecuteJit()
                  (lightweight ~40 B)                    on PreparedQuery
                                                              │
                                                              ▼
                                                    SharcDataReader (disposable)
                                                    - owns cursor
                                                    - owns composed FilterNode
                                                    - reader.Dispose() releases all
                                                              │
                                                              ▼
                                                    JIT state is gone
                                                    PreparedQuery untouched
```

---

## LIMIT/OFFSET Override Semantics

| Scenario | Intent LIMIT | JIT LIMIT | Effective |
|----------|-------------|-----------|-----------|
| No override | 100 | null | 100 |
| JIT tighter | 100 | 50 | 50 |
| JIT wider (capped) | 100 | 200 | 100 |
| Only JIT | null | 50 | 50 |
| Neither | null | null | null |

**Rule**: `Math.Min(intentLimit, jitLimit)` — JIT can only **restrict**, never **widen** beyond what the PreparedQuery allows.

**OFFSET**: JIT override replaces intent offset entirely (additive would be confusing — positional semantics).

---

## Allocation Budget

| Component | Bytes | When |
|-----------|-------|------|
| `JitQuery` object | ~40 | Per `Jit()` call |
| `List<IFilterStar>` | ~72 | If `.Where()` called |
| Composed `BakedDelegate` closure | ~48 | Per `Execute()` |
| Composed `FilterNode` | ~64 | Per `Execute()` |
| Merged ordinals `HashSet<int>` | ~120 | Per `Execute()` (temp) |
| **Total** | **~344 B** | **Tier 0 (≤888 B)** |
| **Per-row hot path** | **0 B** | Closure + stackalloc |

---

## Foundation Already Built

| Component | Location | Role in JIT |
|-----------|----------|-------------|
| `FilterStar.And()` | [Filter/FilterStar.cs](../src/Sharc/Filter/FilterStar.cs) | Merges multiple `.Where()` calls |
| `FilterStarCompiler.Compile()` | [Filter/FilterStarCompiler.cs](../src/Sharc/Filter/FilterStarCompiler.cs) | Compiles JIT IFilterStar → BakedDelegate |
| `FilterTreeCompiler.GetReferencedColumns()` | [Filter/FilterTreeCompiler.cs](../src/Sharc/Filter/FilterTreeCompiler.cs) | Collects ordinals for offset hoisting |
| `FilterNode` | [Filter/FilterNode.cs](../src/Sharc/Filter/FilterNode.cs) | Wraps composed BakedDelegate with stackalloc offset hoisting |
| `BakedDelegate` | [Filter/FilterNode.cs](../src/Sharc/Filter/FilterNode.cs) | `(Span<byte>, Span<long>, Span<int>, long) → bool` — zero-alloc |
| `PreparedQuery` | [PreparedQuery.cs](../src/Sharc/PreparedQuery.cs) | Base type that JitQuery extends |
| `QueryPostProcessor.Apply()` | [Query/QueryPostProcessor.cs](../src/Sharc/Query/QueryPostProcessor.cs) | LIMIT/OFFSET application pattern |

### What Needs Adding

| Component | File | Change |
|-----------|------|--------|
| `FilterNode.CompiledDelegate` | `src/Sharc/Filter/FilterNode.cs` | Internal accessor for `_compiledDelegate` |
| `FilterNode.ReferencedOrdinals` | `src/Sharc/Filter/FilterNode.cs` | Internal accessor for `_referencedOrdinals` |
| `JitQuery` | `src/Sharc/JitQuery.cs` | New file — fluent builder |
| `JitPostProcessor` | `src/Sharc/Query/JitPostProcessor.cs` | New file — LIMIT/OFFSET override |
| `PreparedQuery.Jit()` | `src/Sharc/PreparedQuery.cs` | Factory method |
| `PreparedQuery.ExecuteJit()` | `src/Sharc/PreparedQuery.cs` | Internal execution with JIT composition |
| `PreparedQuery.ComposeJitFilters()` | `src/Sharc/PreparedQuery.cs` | BakedDelegate-level AND composition |

---

## Use Cases

### 1. Multi-Agent Context Queries

An MCP tool prepares a base query once, then each agent adds its own scope restrictions:

```csharp
var prepared = db.Prepare("SELECT * FROM documents WHERE category = $cat");

// Agent A: needs recent, high-priority docs
using var reader = prepared.Jit()
    .Where(FilterStar.Column("priority").Gte(8L))
    .Where(FilterStar.Column("updated_at").Gt(cutoffTimestamp))
    .WithLimit(agent.MaxTokens / avgTokensPerDoc)
    .Execute(new Dictionary<string, object> { ["cat"] = "technical" });
```

### 2. Pagination with Dynamic Filters

```csharp
var prepared = db.Prepare("SELECT * FROM products");

// Page through results with caller-specific filter
foreach (var filter in userSelectedFilters)
{
    using var reader = prepared.Jit()
        .Where(FilterStar.Column(filter.Column).Eq(filter.Value))
        .WithLimit(pageSize)
        .WithOffset(page * pageSize)
        .Execute();
}
```

### 3. Threshold-Based Streaming

```csharp
var prepared = db.Prepare("SELECT sensor_id, value, ts FROM readings");

// Each monitoring cycle uses current threshold
while (monitoring)
{
    double alertThreshold = GetDynamicThreshold();
    using var reader = prepared.Jit()
        .Where(FilterStar.Column("value").Gt(alertThreshold))
        .WithLimit(1000)
        .Execute();
    ProcessAlerts(reader);
}
// PreparedQuery reused across cycles; threshold closure re-created each time
```

---

## Success Criteria

1. **`Jit().Execute()` with no customization** produces identical results to `Execute()`
2. **JIT filter composes correctly** with PreparedQuery's static filter (AND semantics)
3. **PreparedQuery isolation**: After JIT execution, `prepared.Execute()` produces same results as before
4. **Zero-alloc hot path**: Per-row filter evaluation allocates 0 bytes (BakedDelegate + stackalloc)
5. **Total overhead ≤ 344 B** per JIT Execute() call (Tier 0)
6. **AOT/WASM safe**: No dynamic code generation — closure-composed delegates only
