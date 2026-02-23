# IPreparedReader, IPreparedWriter, IPreparedAgent

## Context

Sharc has 4 prepared types (`PreparedReader`, `PreparedQuery`, `PreparedTraversal`, `JitQuery`) that all follow the same lifecycle: pre-resolve at construction → reset+reuse on execute → DisposeForReal on teardown. But there's no shared interface — consumers can't accept "any prepared reader" polymorphically. The vector layer (`PRC/PlanVectorBuildingBlocks.md`) will add `PreparedVectorQuery`, making 5 types with no common contract.

Meanwhile, writes have no prepared pattern at all — every `SharcWriter.Insert()` re-scans schema, looks up root pages, and creates a fresh transaction. And there's no way to compose reads + writes into a coordinated, trust-enforced operation.

**Goals:**
1. `IPreparedReader` — unify all prepared readers behind one interface
2. `IPreparedWriter` + `PreparedWriter` — zero-overhead repeated writes on a single table
3. `PreparedAgent` — composite of readers + writers that executes like an agent (ordered steps, single transaction, trust enforcement at build time)
4. Save as `PRC/PreparedInterfaces.md`

## Interface Definitions

### IPreparedReader

```csharp
// src/Sharc/IPreparedReader.cs
public interface IPreparedReader : IDisposable
{
    /// Returns a SharcDataReader positioned before the first row.
    /// First call creates state; subsequent calls reuse with 0 B allocation.
    SharcDataReader Execute();
}
```

**Why `Execute()` not `CreateReader()`**: Consistent with `PreparedQuery.Execute()` and `PreparedTraversal.Execute()`. `PreparedReader.CreateReader()` preserved for backward compat; `Execute()` delegates to it.

**Why returns `SharcDataReader` not an abstraction**: SharcDataReader is already the universal output. An `ISharcDataReader` would break all existing consumers and add interface dispatch to the hot path.

### IPreparedWriter

```csharp
// src/Sharc/IPreparedWriter.cs
public interface IPreparedWriter : IDisposable
{
    long Insert(params ColumnValue[] values);
    bool Delete(long rowId);
    bool Update(long rowId, params ColumnValue[] values);
}
```

## New Types

### PreparedWriter (~100 lines)

```csharp
// src/Sharc/PreparedWriter.cs
public sealed class PreparedWriter : IPreparedWriter
{
    private SharcDatabase? _db;
    private readonly TableInfo _table;
    private readonly Dictionary<string, uint> _rootCache;
    private SharcWriteTransaction? _transaction;
    private readonly AgentInfo? _agent;

    // Factory: SharcWriter.PrepareWriter(tableName) or db.PrepareWriter(tableName)
    // Pre-resolves: TableInfo (no O(n) schema scan per call), root page cache
    // Supports: auto-commit or transaction-bound (via WithTransaction/DetachTransaction)
    // Agent: optional trust enforcement via EntitlementEnforcer.EnforceWrite
    // Delegates to: SharcWriter.InsertCore/DeleteCore/UpdateCore (same statics JitQuery uses)
}
```

**What's cached**: `TableInfo` (eliminates schema scan), root page `Dictionary` (shared with SharcWriter pattern)
**What's NOT cached**: `BTreeMutator` (per-transaction, freelist state is fresh), `ShadowPageSource` (managed by Transaction)

### PreparedAgent (~180 lines)

A composite that coordinates multiple `IPreparedReader` + `IPreparedWriter` steps as a single agent-like operation.

```csharp
// src/Sharc/PreparedAgent.cs
public sealed class PreparedAgent : IDisposable
{
    private readonly ExecuteStep[] _steps;
    private readonly AgentInfo? _agent;

    // Built via fluent Builder, immutable after Build()
    public PreparedAgentResult Execute(SharcWriteTransaction tx);
    public PreparedAgentResult Execute(); // auto-commit

    public sealed class Builder
    {
        public Builder WithAgent(AgentInfo agent);
        public Builder Read(IPreparedReader reader, Action<SharcDataReader>? callback = null);
        public Builder Insert(IPreparedWriter writer, Func<ColumnValue[]> valuesFactory);
        public Builder Delete(IPreparedWriter writer, Func<long> rowIdFactory);
        public Builder Update(IPreparedWriter writer, Func<long> rowIdFactory,
                              Func<ColumnValue[]> valuesFactory);
        public PreparedAgent Build(); // validates entitlements, fail-fast
    }
}
```

**Why "works like an agent"**:
- Has **identity** (bound to `AgentInfo`)
- Has **entitlements** (validated once at `Build()` time, not per-execute)
- Has **a plan** (ordered steps declared in builder)
- Executes **atomically** (single transaction)
- Is **reusable** (call `Execute()` repeatedly; closures provide dynamic values)
- Is **auditable** (all operations attributable via AgentInfo)

**Usage example:**
```csharp
using var userReader = db.PrepareReader("users", "name", "email");
using var auditWriter = writer.PrepareWriter("audit_log");

using var op = db.PrepareExecute()
    .WithAgent(agent) // validates: agent can read users + write audit_log
    .Read(userReader, r => { if (r.Seek(42)) name = r.GetString(0); })
    .Insert(auditWriter, () => new[] {
        ColumnValue.Text(13, "read_user"u8.ToArray()),
        ColumnValue.FromInt64(1, 42)
    })
    .Build();

var result = op.Execute(tx); // all steps in one transaction
```

### PreparedAgentResult (~25 lines)

```csharp
public sealed class PreparedAgentResult
{
    public int StepsExecuted { get; init; }
    public long[] InsertedRowIds { get; init; }
    public int RowsAffected { get; init; } // deletes + updates
}
```

## Retroactive Interface Implementation

| Type | Interface | Change |
|------|-----------|--------|
| `PreparedReader` | `IPreparedReader` | Add `: IPreparedReader`, add `Execute() => CreateReader()` |
| `PreparedQuery` | `IPreparedReader` | Add `: IPreparedReader` (`Execute()` already exists) |
| `JitQuery` | `IPreparedReader, IPreparedWriter` | Add both; `Execute() => Query()` (explicit impl) |
| Future `PreparedVectorQuery` | `IPreparedReader` | Designed in from the start |
| `PreparedTraversal` | None | Returns `GraphResult`, not `SharcDataReader` — different contract |

## Factory Methods

```csharp
// On SharcWriter:
public PreparedWriter PrepareWriter(string tableName);
public PreparedWriter PrepareWriter(AgentInfo agent, string tableName);

// On SharcDatabase:
public PreparedAgent.Builder PrepareExecute();
```

## Files

| File | Action | Lines |
|------|--------|-------|
| `src/Sharc/IPreparedReader.cs` | NEW | ~30 |
| `src/Sharc/IPreparedWriter.cs` | NEW | ~25 |
| `src/Sharc/PreparedWriter.cs` | NEW | ~100 |
| `src/Sharc/PreparedAgent.cs` | NEW | ~180 |
| `src/Sharc/PreparedAgentResult.cs` | NEW | ~25 |
| `src/Sharc/PreparedReader.cs` | MODIFY | +8 |
| `src/Sharc/PreparedQuery.cs` | MODIFY | +3 |
| `src/Sharc/JitQuery.cs` | MODIFY | +10 |
| `src/Sharc/SharcWriter.cs` | MODIFY | +20 |
| `src/Sharc/SharcDatabase.cs` | MODIFY | +5 |
| `tests/Sharc.IntegrationTests/IPreparedReaderTests.cs` | NEW | ~120 |
| `tests/Sharc.IntegrationTests/PreparedWriterTests.cs` | NEW | ~200 |
| `tests/Sharc.IntegrationTests/PreparedAgentTests.cs` | NEW | ~250 |
| `PRC/PreparedInterfaces.md` | NEW | plan doc |

## Implementation Order (TDD)

### Phase 1: IPreparedReader (30 min)
1. Write `IPreparedReaderTests.cs` → RED
2. Create `IPreparedReader.cs`
3. Modify `PreparedReader.cs`: add `: IPreparedReader`, add `Execute()`
4. Modify `PreparedQuery.cs`: add `: IPreparedReader`
5. Tests → GREEN

### Phase 2: IPreparedWriter + PreparedWriter (1 hr)
6. Write `PreparedWriterTests.cs` → RED
7. Create `IPreparedWriter.cs`
8. Create `PreparedWriter.cs`
9. Add `PrepareWriter()` to `SharcWriter.cs`
10. Tests → GREEN

### Phase 3: JitQuery Retrofit (20 min)
11. Add `JitQuery : IPreparedReader, IPreparedWriter` tests → RED
12. Modify `JitQuery.cs`: add both interfaces, add `Execute()` wrapper
13. Tests → GREEN

### Phase 4: PreparedAgent (1 hr)

14. Write `PreparedAgentTests.cs` → RED
15. Create `PreparedAgentResult.cs`
16. Create `PreparedAgent.cs` with Builder
17. Add `PrepareExecute()` to `SharcDatabase.cs`
18. Tests → GREEN

### Phase 5: Full Verification
19. Run `dotnet test` — all 2,600+ tests pass
20. Save plan as `PRC/PreparedInterfaces.md`

## What Does NOT Change

- `PreparedReader.CreateReader()` — preserved, backward compatible
- `PreparedQuery.Execute()` — unchanged signature
- `JitQuery.Query()` / `Insert()` / `Delete()` / `Update()` — unchanged
- `SharcWriter.Insert()` etc. — unchanged
- `SharcWriteTransaction` — unchanged
- Public API surface — only additions, no breaks
