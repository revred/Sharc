# Layer As View — Design Document

> A View IS a Layer. The `ILayer` interface defines the contract;
> `SharcView` is the first (and currently only) concrete implementation.

---

## The Core Insight

A **View** in Sharc is a named, reusable cursor configuration — a read lens over
a table or another view. But the way a view *materializes* its rows is a separate
concern from what it *projects*. Today, all views materialize eagerly: every row
is decoded into `QueryValue[]` arrays upfront before the consumer sees any of them.

A **Layer** is the generalization: a row-producing source with an explicit
materialization strategy. Instead of always materializing eagerly, the engine
asks the Layer how it prefers to produce rows — and respects that preference.

The relationship is simple: **every View is a Layer**. The `ILayer` interface
captures the minimum contract; `SharcView` implements it. No new type is needed
because the abstraction lives at the interface level, not the class level.

---

## Interface Design

```csharp
public interface ILayer
{
    /// <summary>Human-readable name for this layer.</summary>
    string Name { get; }

    /// <summary>Controls how rows are produced during cursor iteration.</summary>
    MaterializationStrategy Strategy { get; }

    /// <summary>Opens a forward-only cursor over the layer's projected rows.</summary>
    IViewCursor Open(SharcDatabase db);
}
```

### Why These Three Members?

| Member | Purpose | SOLID Principle |
|--------|---------|-----------------|
| `Name` | Identity for error messages, registration, debugging | ISP: minimal surface |
| `Strategy` | Tells the engine how to handle row production | OCP: engine adapts to strategy |
| `Open()` | Produces the cursor — the only I/O operation | DIP: consumers depend on interface |

### What ILayer Does NOT Include

- `SourceTable` / `SourceView` — those are `SharcView`-specific implementation details
- `Filter` / `ProjectedColumnNames` — configuration, not contract
- `Build()` / `Named()` — construction, not runtime behavior

This follows **Interface Segregation**: consumers of rows need only `Open()`;
consumers checking strategy need only `Strategy`. No fat interface.

---

## MaterializationStrategy

```csharp
public enum MaterializationStrategy : byte
{
    /// <summary>
    /// Materialize all rows upfront into QueryValue[] arrays.
    /// Stable for repeated random access, aggregations, and ORDER BY.
    /// </summary>
    Eager = 0,

    /// <summary>
    /// Stream rows on demand via cursor. Each Read() decodes the next row.
    /// Lower memory, forward-only. Ideal for LIMIT, sequential scan, ETL.
    /// </summary>
    Streaming = 1,
}
```

### Default Is Eager

`Eager = 0` ensures backward compatibility — all existing views that don't
specify a strategy get the current behavior. No existing code breaks.

### When to Use Streaming

| Scenario | Why Streaming Wins |
|----------|--------------------|
| `LIMIT 10` on 100K rows | Only decode 10 rows, not 100K |
| ETL pipeline | Process-and-discard, no need to hold all rows |
| Filtered JitQuery over view | Filter applied during iteration, rejected rows never materialized |
| Memory-constrained environments | Never holds more than 1 row in memory |

### When Eager Is Better

| Scenario | Why Eager Wins |
|----------|----------------|
| ORDER BY | Must see all rows to sort |
| Aggregations (COUNT, SUM) | Must scan all rows |
| Repeated access | Don't re-open cursor for second pass |
| Small result sets | Overhead of strategy check > savings |

---

## SOLID Compliance

### Single Responsibility (SRP)

- `ILayer` defines the contract — what a layer provides
- `SharcView` owns the implementation — how a view produces rows
- `ViewBuilder` owns construction — how a view is configured
- `ExecutionRouter` owns hint routing — how queries dispatch by hint

No class does two jobs.

### Open/Closed (OCP)

New materialization strategies (e.g., `Cached`, `Partitioned`) can be added to
the enum without modifying `SharcView`, `ILayer`, or consuming code.
The engine's strategy-aware code uses `switch` on the enum — adding a new
case is additive, not destructive.

### Liskov Substitution (LSP)

Any `SharcView` is substitutable as an `ILayer`. Code that depends on `ILayer`
works identically whether the concrete type is `SharcView` or a future
`MaterializedLayer`, `CachedLayer`, etc.

### Interface Segregation (ISP)

`ILayer` has exactly 3 members. Consumers that only need to iterate rows
call `Open()`. Consumers that need strategy information read `Strategy`.
No consumer is forced to depend on methods it doesn't use.

### Dependency Inversion (DIP)

- `JitQuery` depends on `ILayer`, not `SharcView`
- `ExecutionRouter` depends on `ILayer`, not `SharcView`
- `ViewResolver` continues to work with `SharcView` directly (it needs
  `SourceTable`, `Filter`, etc. — SharcView-specific concerns)

High-level modules depend on the abstraction; low-level modules implement it.

---

## Impact on Existing Code

### JitQuery

```
Before: private SharcView? _sourceView;
After:  private ILayer? _sourceLayer;
```

- `QueryFromView()` calls `_sourceLayer!.Open(db)` — unchanged semantics
- `AsView()` checks `_sourceLayer is SharcView sv` for view-chain composition
- All 12 view-backed JitQuery tests continue to pass unchanged

### SharcDatabase.Jit() Overloads

```csharp
// Existing (backward-compatible — SharcView IS ILayer):
public JitQuery Jit(SharcView view) => Jit((ILayer)view);

// New:
public JitQuery Jit(ILayer layer) => new JitQuery(this, layer);
```

### ViewResolver

No changes. ViewResolver works with `SharcView` directly because it needs
`SourceTable`, `Filter`, and `ProjectedColumnNames` — these are implementation
details, not interface concerns. This is correct by ISP.

### ViewBuilder

Gains `.Materialize(MaterializationStrategy strategy)` fluent method.
Default remains `Eager`. No existing builder chains break.

---

## Relationship to SQL Execution Hints

The `ILayer` interface and `MaterializationStrategy` are the foundation for
SQL execution hints (`DIRECT / CACHED / JIT`). When the engine encounters:

```sql
JIT SELECT * FROM users WHERE age > 25
```

The `ExecutionRouter` creates a `JitQuery` internally. If the source is a
registered view with `Strategy = Streaming`, the JitQuery streams rows
lazily through `ILayer.Open()` instead of materializing them eagerly.

The hints control **execution tier** (Direct/Cached/Jit).
The strategy controls **materialization behavior** (Eager/Streaming).
These are orthogonal concerns that compose cleanly.

---

*See also:*
- [JitSQL.md](JitSQL.md) — Full JitSQL specification
- [PotentialJitInternalUse.md](PotentialJitInternalUse.md) — Internal JIT optimization map
- [WildUserScenariosForJitUse.md](WildUserScenariosForJitUse.md) — External user scenarios
- [APIDesign.md](APIDesign.md) — Public API philosophy
