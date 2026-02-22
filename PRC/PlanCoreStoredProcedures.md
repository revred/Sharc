# PlanCoreStoredProcedures — Stored Procedures on Steroids for Sharc Core

**Status**: Proposed
**Priority**: High — Complements Graph Stored Procedures
**Target**: v1.3.0 (alongside PlanGraphStoredProcedures)
**Depends on**: QueryPlanCache (existing), FilterTreeCompiler (existing), SharcWriter (existing)

---

## Problem Statement

Sharc already has sophisticated query plan caching (`QueryPlanCache`), pre-compiled filter trees (`FilterTreeCompiler`), and streaming aggregation. But these are internal implementation details — users can't:

1. **Register named queries** that skip parsing on every call
2. **Execute parameterized bulk mutations** (batch UPDATE/DELETE with WHERE)
3. **Compose multi-step operations** as atomic units (read → transform → write)
4. **Define custom streaming operators** that plug into the query pipeline
5. **Run conditional logic** (IF EXISTS → UPDATE, ELSE → INSERT)

The infrastructure is 80% built. The stored procedure layer is the **missing 20%** that turns Sharc from a fast reader/writer into a programmable data engine.

---

## Current Architecture (What Exists)

### Already Pre-Compiled

| Component | What's Cached | Reuse Level |
| :--- | :--- | :--- |
| `QueryPlanCache` | Parsed AST → QueryIntent | Per query string (thread-safe) |
| `FilterTreeCompiler` | FilterStar → IFilterNode tree | Per reader intent |
| `_paramFilterCache` | Parameterized filter nodes | Per (intent, param values) hash |
| `_readerInfoCache` | Column projection + filter + schema | Per query intent |
| `ViewResolver` | View definitions with generation tracking | Per database lifetime |

### Already Streaming (Zero-Materialization)

| Component | Mechanism |
| :--- | :--- |
| `StreamingAggregator` | GROUP BY + COUNT/SUM/AVG/MIN/MAX without materializing all rows |
| `CompoundQueryExecutor` | UNION/INTERSECT/EXCEPT via Fingerprint128 dedup |
| `JoinExecutor` | Hash join with build/probe phases |
| `CoteExecutor` | CTE materialization → virtual table injection |
| Pre-decode filtering | Evaluate filter on raw page bytes before record allocation |

### Already Bulk-Capable

| Component | Mechanism |
| :--- | :--- |
| `SharcWriter.InsertBatch()` | Multiple inserts in single transaction |
| `SharcWriteTransaction` | Explicit BEGIN/COMMIT/ROLLBACK |
| `ShadowPageSource` | All writes buffered until commit |
| `BTreeMutator` | Insert/Update/Delete with page splits and freelist |

**The gap**: These are wired as separate internal systems. Stored procedures unify them behind a single, user-facing abstraction.

---

## Design: Core Stored Procedures

### Core Concept

A **SharcProcedure** is a pre-compiled, named, parameterized operation that:
- Skips SQL parsing on every call (compiled once at registration)
- Supports read, write, and mixed read-write operations
- Executes within a transaction scope (auto or explicit)
- Reports execution metrics (rows affected, time, allocations)
- Can be composed into multi-step procedures

### Four Procedure Categories

| Category | Examples | Key Benefit |
| :--- | :--- | :--- |
| **Query Procedures** | Parameterized SELECT | Parse once, execute forever |
| **Mutation Procedures** | Bulk UPDATE/DELETE with WHERE | Pre-compiled filter + batch mutation |
| **Composite Procedures** | Read → transform → write | Atomic multi-step operations |
| **Custom Operators** | Streaming transforms, validations | Extensible pipeline |

---

## API Design

### Registration & Execution

```csharp
// ── Register a query procedure ──
db.RegisterProcedure("GetActiveUsers",
    "SELECT id, name, email FROM users WHERE status = $status AND created > $minDate");

// ── Execute with parameters ──
using var reader = db.ExecuteProcedure("GetActiveUsers", new SharcParameters
{
    ["$status"] = "active",
    ["$minDate"] = DateTimeOffset.Now.AddMonths(-1).ToUnixTimeSeconds()
});
while (reader.Read())
    Console.WriteLine(reader.GetString(1));
```

### Fluent Builder API

```csharp
// ── Pre-compiled parameterized query ──
var proc = SharcProcedure.CreateQuery("TopUsersByDept")
    .Sql("SELECT dept, name, score FROM users WHERE dept = $dept ORDER BY score DESC LIMIT $limit")
    .Parameter("$dept", SharcType.Text)
    .Parameter("$limit", SharcType.Integer, defaultValue: 10L)
    .Build();

db.RegisterProcedure(proc);

// ── Execute ──
using var reader = db.ExecuteProcedure("TopUsersByDept",
    new SharcParameters { ["$dept"] = "Engineering" });
```

### Bulk Mutation Procedures

```csharp
// ── Bulk UPDATE with WHERE ──
var deactivate = SharcProcedure.CreateMutation("DeactivateOldUsers")
    .Table("users")
    .Operation(MutationType.Update)
    .Set("status", "inactive")
    .Set("deactivated_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    .Where(FilterStar.Column("last_login").Lt(cutoffTimestamp))
    .Build();

long rowsAffected = db.ExecuteMutation("DeactivateOldUsers",
    new SharcParameters { ["cutoff"] = cutoffTimestamp });

// ── Bulk DELETE with WHERE ──
var purge = SharcProcedure.CreateMutation("PurgeArchivedEvents")
    .Table("events")
    .Operation(MutationType.Delete)
    .Where(FilterStar.Column("archived").Eq(1L)
        .And(FilterStar.Column("created").Lt(retentionCutoff)))
    .Build();

long deleted = db.ExecuteMutation("PurgeArchivedEvents");

// ── Upsert (INSERT OR UPDATE) ──
var upsert = SharcProcedure.CreateMutation("UpsertUserPreference")
    .Table("preferences")
    .Operation(MutationType.Upsert)
    .KeyColumn("user_id")
    .Values("user_id", "key", "value")
    .Build();

db.ExecuteMutation("UpsertUserPreference", new SharcParameters
{
    ["user_id"] = 42L,
    ["key"] = "theme",
    ["value"] = "dark"
});
```

### Composite Procedures (Multi-Step)

```csharp
// ── Atomic read-then-write ──
var transferBalance = SharcProcedure.CreateComposite("TransferBalance")
    .Step("ReadSource", step => step
        .Sql("SELECT balance FROM accounts WHERE id = $sourceId")
        .CaptureResult("sourceBalance"))
    .Step("ReadTarget", step => step
        .Sql("SELECT balance FROM accounts WHERE id = $targetId")
        .CaptureResult("targetBalance"))
    .Step("ValidateAndWrite", step => step
        .Custom((db, context) =>
        {
            long sourceBalance = context.Get<long>("sourceBalance");
            long amount = context.Parameter<long>("$amount");
            if (sourceBalance < amount)
                throw new InvalidOperationException("Insufficient balance");

            using var writer = SharcWriter.From(db);
            writer.Update("accounts", context.Parameter<long>("$sourceId"),
                ColumnValue.FromInt64(2, sourceBalance - amount));
            writer.Update("accounts", context.Parameter<long>("$targetId"),
                ColumnValue.FromInt64(2, context.Get<long>("targetBalance") + amount));
        }))
    .Transactional()  // All steps in single ACID transaction
    .Build();

db.ExecuteComposite("TransferBalance", new SharcParameters
{
    ["$sourceId"] = 1L,
    ["$targetId"] = 2L,
    ["$amount"] = 500L
});
```

### Custom Streaming Operators

```csharp
// ── Register a custom operator ──
db.RegisterOperator("Deduplicate", new DeduplicateOperator("email"));

// ── Use in a procedure ──
var proc = SharcProcedure.CreateQuery("UniqueEmails")
    .Sql("SELECT name, email FROM users WHERE active = 1")
    .PipeThrough("Deduplicate")
    .Build();
```

---

## Core Types

### `ISharcProcedure`

```csharp
/// <summary>
/// A pre-compiled, named, parameterized database operation.
/// </summary>
public interface ISharcProcedure
{
    string Name { get; }
    ProcedureKind Kind { get; }  // Query, Mutation, Composite
    IReadOnlyList<ProcedureParameter> Parameters { get; }
    int EstimatedCostTier { get; }
}

public enum ProcedureKind
{
    Query,      // Returns a SharcDataReader
    Mutation,   // Returns rows affected (long)
    Composite   // Returns ProcedureResult (reader + metadata)
}
```

### `SharcProcedure` (Static Builder)

```csharp
public static class SharcProcedure
{
    public static QueryProcedureBuilder CreateQuery(string name);
    public static MutationProcedureBuilder CreateMutation(string name);
    public static CompositeProcedureBuilder CreateComposite(string name);
}
```

### `SharcParameters`

```csharp
/// <summary>
/// Named parameter collection for procedure execution.
/// </summary>
public sealed class SharcParameters : IEnumerable<KeyValuePair<string, object?>>
{
    public object? this[string name] { get; set; }
    public int Count { get; }
    public bool ContainsKey(string name);
}
```

### `ProcedureParameter`

```csharp
/// <summary>
/// Metadata about a procedure parameter.
/// </summary>
public readonly record struct ProcedureParameter
{
    public required string Name { get; init; }
    public required SharcType Type { get; init; }
    public object? DefaultValue { get; init; }
    public bool IsRequired { get; init; }
}
```

### `ProcedureRegistry`

```csharp
/// <summary>
/// Thread-safe registry of named procedures.
/// </summary>
public sealed class ProcedureRegistry
{
    public void Register(ISharcProcedure procedure);
    public ISharcProcedure? Get(string name);
    public bool Remove(string name);
    public IReadOnlyList<string> ListNames();
    public int Count { get; }
}
```

### `ProcedureResult`

```csharp
/// <summary>
/// Result of a procedure execution with metrics.
/// </summary>
public sealed class ProcedureResult : IDisposable
{
    public SharcDataReader? Reader { get; }     // For query procedures
    public long RowsAffected { get; }            // For mutation procedures
    public TimeSpan Elapsed { get; }
    public int StepsExecuted { get; }            // For composite procedures
    public IReadOnlyDictionary<string, object?> Context { get; }  // Captured values
}
```

### `MutationType`

```csharp
public enum MutationType
{
    Insert,
    Update,
    Delete,
    Upsert  // INSERT OR UPDATE (key-based conflict resolution)
}
```

---

## Execution Model

### Query Procedure Execution

```
db.ExecuteProcedure("name", params)
    │
    ├── 1. Registry lookup → ISharcProcedure
    ├── 2. Parameter validation (type check, required check)
    ├── 3. Bind parameters into cached QueryPlan
    ├── 4. Create reader from pre-compiled plan
    │       ├── Filter nodes already compiled (from registration)
    │       ├── Column projection already resolved
    │       └── Index selection pre-determined
    └── 5. Return SharcDataReader (streaming)
```

**Parse cost**: Zero. The SQL was parsed once at `RegisterProcedure()` time. Only parameter binding happens per call.

### Mutation Procedure Execution

```
db.ExecuteMutation("name", params)
    │
    ├── 1. Registry lookup → MutationProcedure
    ├── 2. Parameter validation
    ├── 3. Begin auto-transaction
    ├── 4. Scan table with pre-compiled WHERE filter
    │       ├── For UPDATE: apply SET values to matching rows
    │       ├── For DELETE: remove matching rows
    │       └── For UPSERT: check key existence, insert or update
    ├── 5. Commit transaction
    └── 6. Return rows affected
```

**Key optimization for bulk UPDATE/DELETE**: The WHERE filter is pre-compiled as an `IFilterNode`. The scan evaluates on raw page bytes — only matching rows trigger the (more expensive) mutation.

### Composite Procedure Execution

```
db.ExecuteComposite("name", params)
    │
    ├── 1. Begin transaction (if Transactional())
    ├── 2. Execute Step 1
    │       ├── Bind parameters
    │       ├── Execute query/mutation
    │       └── Capture results into context
    ├── 3. Execute Step 2 (can reference Step 1 results)
    │       └── ...
    ├── 4. Execute Step N
    ├── 5. Commit transaction (if Transactional())
    └── 6. Return ProcedureResult with all captured values
```

---

## Bulk Mutation Deep Dive

### Bulk UPDATE with WHERE (Most Requested)

**Today** (manual, multi-step):
```csharp
// User has to: scan → collect IDs → update one by one
using var reader = db.CreateReader("users",
    new SharcFilter("status", SharcOperator.Equals, "inactive"));
var idsToUpdate = new List<long>();
while (reader.Read())
    idsToUpdate.Add(reader.GetInt64(0));

using var writer = SharcWriter.From(db);
using var txn = writer.BeginTransaction();
foreach (var id in idsToUpdate)
    writer.Update("users", id, ColumnValue.Text(5, "archived"u8.ToArray()));
txn.Commit();
```

**With stored procedures** (single call):
```csharp
var proc = SharcProcedure.CreateMutation("ArchiveInactive")
    .Table("users")
    .Operation(MutationType.Update)
    .Set("status", "archived")
    .Where(FilterStar.Column("status").Eq("inactive"))
    .Build();

long affected = db.ExecuteMutation("ArchiveInactive");
// Single scan, single transaction, no intermediate List<long>
```

**Internal implementation**:
1. Open B-tree cursor on table
2. For each row, evaluate pre-compiled `IFilterNode` on raw page bytes
3. If match: decode row, apply SET mutations, call `BTreeMutator.Update()`
4. All within a single `ShadowPageSource` transaction
5. Commit once at the end

**Performance advantage**: No intermediate `List<long>` allocation. No double-scan (first to collect IDs, then to update). Single pass with pre-compiled filter.

### Upsert (INSERT OR UPDATE)

**Algorithm**:
1. Seek to key using B-tree index or primary key
2. If found → UPDATE with new values
3. If not found → INSERT with all values
4. All within same transaction

```csharp
// Pre-compiled upsert
var upsert = SharcProcedure.CreateMutation("UpsertConfig")
    .Table("config")
    .Operation(MutationType.Upsert)
    .KeyColumn("key")
    .Values("key", "value", "updated_at")
    .Build();

// Execute
db.ExecuteMutation("UpsertConfig", new SharcParameters
{
    ["key"] = "theme",
    ["value"] = "dark",
    ["updated_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
});
```

### Conditional Batch Insert

```csharp
// Insert rows that don't already exist (by key)
var insertIfNew = SharcProcedure.CreateMutation("InsertIfNew")
    .Table("events")
    .Operation(MutationType.InsertIfNotExists)
    .KeyColumn("event_id")
    .Values("event_id", "timestamp", "data")
    .Build();

// Bulk execution — skips duplicates without error
long inserted = db.ExecuteBatchMutation("InsertIfNew", eventBatch);
```

---

## Integration with SharcDatabase

### New Methods on SharcDatabase

```csharp
public sealed partial class SharcDatabase
{
    // ── Registration ──
    public void RegisterProcedure(ISharcProcedure procedure);
    public void RegisterProcedure(string name, string sql);  // Convenience overload

    // ── Execution ──
    public SharcDataReader ExecuteProcedure(string name, SharcParameters? parameters = null);
    public long ExecuteMutation(string name, SharcParameters? parameters = null);
    public ProcedureResult ExecuteComposite(string name, SharcParameters? parameters = null);

    // ── Introspection ──
    public IReadOnlyList<string> ListProcedures();
    public ISharcProcedure? GetProcedure(string name);
    public bool RemoveProcedure(string name);

    // ── Registry (internal, exposed for testing) ──
    internal ProcedureRegistry ProcedureRegistry { get; }
}
```

### Interaction with Existing Features

| Feature | Integration |
| :--- | :--- |
| `QueryPlanCache` | Query procedures register their plan in the cache at Build() time |
| `FilterTreeCompiler` | Mutation WHERE filters pre-compiled at Build() time |
| `SharcWriter` | Mutation procedures use existing writer infrastructure |
| `SharcWriteTransaction` | Composite procedures use existing transaction model |
| `ViewResolver` | Procedures can reference registered views |
| `AgentRegistry` | Agent-scoped procedures honor `EntitlementEnforcer` |

---

## Implementation Plan

### Phase 1: Query Procedures

**New files**:
- `src/Sharc/Procedures/ISharcProcedure.cs`
- `src/Sharc/Procedures/SharcProcedure.cs` (static builder)
- `src/Sharc/Procedures/QueryProcedureBuilder.cs`
- `src/Sharc/Procedures/CompiledQueryProcedure.cs`
- `src/Sharc/Procedures/ProcedureRegistry.cs`
- `src/Sharc/Procedures/SharcParameters.cs`
- `src/Sharc/Procedures/ProcedureParameter.cs`

**Deliverables**:
- Named parameterized query registration
- Parse-once, execute-forever semantics
- Parameter type validation
- Integration with existing `QueryPlanCache`
- `db.RegisterProcedure(name, sql)` convenience method
- `db.ExecuteProcedure(name, params)` returns `SharcDataReader`

### Phase 2: Mutation Procedures

**New files**:
- `src/Sharc/Procedures/MutationProcedureBuilder.cs`
- `src/Sharc/Procedures/CompiledMutationProcedure.cs`
- `src/Sharc/Procedures/MutationType.cs`

**Deliverables**:
- Bulk UPDATE with pre-compiled WHERE filter
- Bulk DELETE with pre-compiled WHERE filter
- Upsert (INSERT OR UPDATE) by key column
- Conditional INSERT (skip duplicates)
- Single-pass scan with mutation (no intermediate collection)
- Transaction auto-management

### Phase 3: Composite Procedures

**New files**:
- `src/Sharc/Procedures/CompositeProcedureBuilder.cs`
- `src/Sharc/Procedures/CompiledCompositeProcedure.cs`
- `src/Sharc/Procedures/ProcedureContext.cs`
- `src/Sharc/Procedures/ProcedureResult.cs`

**Deliverables**:
- Multi-step procedures with shared context
- Result capture between steps (`CaptureResult()`)
- Custom step logic via `Action<SharcDatabase, ProcedureContext>`
- Optional transactional wrapping
- Execution metrics (time, rows, steps)

### Phase 4: Custom Streaming Operators

**New files**:
- `src/Sharc/Procedures/IStreamingOperator.cs`
- `src/Sharc/Procedures/OperatorRegistry.cs`
- `src/Sharc/Procedures/Operators/DeduplicateOperator.cs`
- `src/Sharc/Procedures/Operators/TopNOperator.cs`
- `src/Sharc/Procedures/Operators/TransformOperator.cs`

**Deliverables**:
- `IStreamingOperator` interface for custom transforms
- `PipeThrough("operatorName")` in query procedure builder
- Built-in operators: Deduplicate, TopN, Transform
- Operator chaining (multiple PipeThrough calls)

### Phase 5: Built-In Procedure Library

**New files**:
- `src/Sharc/Procedures/BuiltIn/BuiltInProcedures.cs`

**Built-in procedures shipped with library**:
1. `sharc.GetById` — Parameterized point lookup (any table)
2. `sharc.CountWhere` — Filtered COUNT(*) with pre-compiled filter
3. `sharc.Upsert` — Generic upsert by key column
4. `sharc.BulkInsert` — Optimized batch insert with duplicate handling
5. `sharc.Purge` — Filtered DELETE with safety limits
6. `sharc.Snapshot` — Read all rows into materialized result (for caching)

---

## Performance Targets

| Operation | Target | Baseline |
| :--- | :--- | :--- |
| Query procedure (vs ad-hoc SQL) | < 2% overhead | Ad-hoc parses SQL every call |
| Bulk UPDATE 1K rows | < 10 ms | Manual scan + update: ~15 ms |
| Bulk DELETE 1K rows | < 8 ms | Manual scan + delete: ~12 ms |
| Upsert (key exists) | < 1 us | Manual seek + update: ~2 us |
| Upsert (key missing) | < 2 us | Manual seek + insert: ~3 us |
| Composite 3-step | < 5% overhead vs sequential | Sequential manual calls |
| Procedure registration | < 100 us | One-time cost |

### Allocation Targets

| Operation | Target | Notes |
| :--- | :--- | :--- |
| Query procedure execution | Same as `Query()` | No additional allocation |
| Mutation procedure execution | O(affected rows) | ColumnValue[] per mutation |
| Composite procedure execution | O(steps + captured values) | ProcedureContext allocation |
| Parameter binding | 1 SharcParameters instance | Reusable across calls |

---

## Testing Strategy

### Unit Tests

```
QueryProcedureBuilder_Sql_StoresQuery
QueryProcedureBuilder_Parameter_AddsToList
QueryProcedureBuilder_Build_CompilesQueryPlan
CompiledQueryProcedure_Execute_ReturnsReader
CompiledQueryProcedure_Execute_BindsParameters
MutationProcedureBuilder_Update_SetsColumnsAndFilter
CompiledMutationProcedure_BulkUpdate_AffectsMatchingRows
CompiledMutationProcedure_BulkDelete_RemovesMatchingRows
CompiledMutationProcedure_Upsert_InsertsIfMissing
CompiledMutationProcedure_Upsert_UpdatesIfExists
CompositeProcedure_Execute_RunsAllSteps
CompositeProcedure_CaptureResult_AvailableInNextStep
CompositeProcedure_Transactional_RollsBackOnError
ProcedureRegistry_Register_Get_Roundtrip
ProcedureRegistry_ThreadSafety_ConcurrentAccess
SharcParameters_TypeValidation_RejectsWrongType
```

### Integration Tests

- Execute query procedures against real databases
- Verify bulk mutation atomicity (all-or-nothing on error)
- Verify composite procedures maintain transaction isolation
- Verify parameter binding produces correct results
- Verify procedure results match equivalent ad-hoc queries

### Benchmark Tests

- Query procedure vs `db.Query()` (parse elimination gain)
- Bulk UPDATE vs manual scan-then-update
- Bulk DELETE vs manual scan-then-delete
- Upsert vs manual seek-then-insert-or-update
- Registration cost (one-time)

---

## Deliverables Summary

| Phase | Deliverable | Effort |
| :--- | :--- | :--- |
| 1 | Query procedures (parameterized, cached) | 3 days |
| 2 | Mutation procedures (bulk UPDATE/DELETE/Upsert) | 4 days |
| 3 | Composite procedures (multi-step, transactional) | 3 days |
| 4 | Custom streaming operators | 2 days |
| 5 | Built-in procedure library | 2 days |
| **Total** | | **14 days** |

---

## Relationship to Graph Stored Procedures

`PlanGraphStoredProcedures.md` defines `IGraphProcedure` for traversal operations. The two systems are complementary:

| Aspect | Core Procedures | Graph Procedures |
| :--- | :--- | :--- |
| **Operates on** | Tables (relational) | Graph (nodes + edges) |
| **Primary ops** | SELECT, UPDATE, DELETE, Upsert | Traverse, PathFind, Expand |
| **Builder** | `SharcProcedure.CreateQuery/Mutation` | `GraphProcedure.Create/CreatePipeline` |
| **Registry** | `ProcedureRegistry` | `GraphProcedureRegistry` |
| **Execution** | `db.ExecuteProcedure()` | `proc.Execute(graph, startKey)` |
| **Composition** | Composite steps | Pipeline steps |
| **Cost tracking** | `ProcedureResult.Elapsed` | `ICostObserver` |

**Unified future**: A `SharcProcedure.CreateComposite()` step could invoke a graph procedure, enabling hybrid relational + graph operations in a single atomic procedure.

---

## Success Criteria

1. **Zero-parse execution**: Query procedures never re-parse SQL after registration
2. **Single-pass mutations**: Bulk UPDATE/DELETE scan the table once (not scan + collect + mutate)
3. **Atomic composites**: Multi-step procedures execute within a single ACID transaction
4. **Type-safe parameters**: Wrong parameter types caught at execution time with clear error messages
5. **Backwards compatible**: All existing `Query()`, `CreateReader()`, `SharcWriter` APIs unchanged
6. **Observable**: Every execution returns timing and rows-affected metrics
