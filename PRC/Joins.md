# Joins.md — Sharc Application-Level Join Specification

> **Version:** 0.1-draft  
> **Status:** Spec for fast implementation  
> **Prerequisites:** Index B-tree reads (P1), SchemaReader (✅ done), BTreeCursor (✅ done), RecordDecoder (✅ done)  
> **Estimated effort:** 2–3 weeks (assuming Index B-tree reads are landed)  
> **Namespace:** `Sharc.Joins`

---

## 1. Design Philosophy

Sharc does not implement SQL JOINs. Sharc implements **programmatic, cursor-composed joins** — a deliberate architectural choice that preserves zero-allocation hot paths while delivering the relational traversal that Context Space Engineering demands.

The distinction matters: SQL JOINs require a query planner, a cost model, and a VDBE. Sharc joins are **caller-directed merge strategies** where the application controls the join order, the index selection, and the materialisation policy. This is faster for known access patterns (which is every GCD use case) and trivially predictable in memory behaviour.

### Non-Goals

- No SQL `JOIN` syntax parsing
- No automatic join order selection or cost-based optimisation
- No hash joins (they require materialisation buffers that violate zero-alloc)
- No cross-database joins

### Goals

- Index nested-loop joins using existing `BTreeCursor` + index B-tree reads
- Merge joins on pre-sorted index cursors
- Zero-allocation inner loops for the 90% case (int/text foreign key equi-joins)
- Composable: joins return cursors, cursors compose into further joins
- Entitlement-aware: row-level encryption filters apply transparently during join traversal

---

## 2. Join Strategies

### 2.1 Index Nested-Loop Join (Primary Strategy)

This is the workhorse. For each row in the outer cursor, seek into an index B-tree on the inner table to find matching rows. This is the natural fit for Sharc because:

- `BTreeCursor.Seek(key)` already exists and runs in 585ns
- Index B-tree reads (P1) provide non-rowid key → rowid mapping
- No temporary tables, no hash maps, no materialisation

```
ALGORITHM IndexNestedLoop(outer: ICursor, inner: IndexBTree, keyExtractor):
  for each row in outer:
    key = keyExtractor(row)
    seek inner index to key
    for each matching rowid in inner index:
      fetch inner row via table B-tree seek
      yield (outer_row, inner_row)
```

**Complexity:** O(N × log M) where N = outer rows, M = inner table size. Each inner lookup is a B-tree seek — sub-microsecond.

**When to use:** Any equi-join where the inner table has an index on the join column. This covers ~95% of GCD access patterns (e.g., `commits` JOIN `files` ON `commit_id`).

### 2.2 Sorted Merge Join

When both cursors are already sorted on the join key (which they are when scanning index B-trees in key order), a single-pass merge is optimal.

```
ALGORITHM SortedMerge(left: ICursor sorted by K, right: ICursor sorted by K):
  advance both cursors to first row
  while both have rows:
    if left.Key == right.Key:
      yield (left_row, right_row)
      advance right (handle duplicates via lookahead buffer)
    elif left.Key < right.Key:
      advance left
    else:
      advance right
```

**Complexity:** O(N + M) single pass over both sorted streams. Zero seeks.

**When to use:** Both sides are scanned from index B-trees on the same key type. Common in GCD edge traversal (e.g., scanning `_edges` by `source` merged with `_records` by `id`).

### 2.3 Rowid Direct Join

The simplest and fastest path — when the foreign key IS the rowid of the target table (extremely common in SQLite schemas without WITHOUT ROWID).

```
ALGORITHM RowidDirect(outer: ICursor, innerTable: TableBTree, rowidExtractor):
  for each row in outer:
    rowid = rowidExtractor(row)
    seek innerTable to rowid           // single B-tree seek, 585ns
    yield (outer_row, inner_row)
```

**Complexity:** O(N × log M) but with the smallest constant factor — no index indirection, no secondary lookup.

**When to use:** Integer foreign keys referencing rowid primary keys.

---

## 3. Core Interfaces

### 3.1 IJoinCursor

The join result is itself a cursor — composable into further joins or filters.

```csharp
namespace Sharc.Joins;

/// <summary>
/// A cursor over joined row pairs. Composable — can be used as input to further joins.
/// </summary>
public interface IJoinCursor : IDisposable
{
    /// <summary>Advance to the next matched pair. Returns false when exhausted.</summary>
    bool MoveNext();

    /// <summary>Access the current left-side row's columns.</summary>
    IRowAccessor Left { get; }

    /// <summary>Access the current right-side row's columns.</summary>
    IRowAccessor Right { get; }

    /// <summary>Reset to the beginning (if supported by underlying cursors).</summary>
    void Reset();
}
```

### 3.2 IRowAccessor

A unified column-access interface that both `SharcDataReader` and join sides implement. Span-based, zero-copy where possible.

```csharp
/// <summary>
/// Read-only access to a row's column values. Zero-copy for blobs and text.
/// </summary>
public interface IRowAccessor
{
    long GetRowId();
    bool IsNull(int ordinal);
    long GetInt64(int ordinal);
    double GetDouble(int ordinal);
    ReadOnlySpan<char> GetText(int ordinal);
    ReadOnlySpan<byte> GetBlob(int ordinal);
    int ColumnCount { get; }
}
```

### 3.3 JoinBuilder (Fluent API)

```csharp
/// <summary>
/// Fluent builder for constructing joins. All joins are read-only, forward-only cursors.
/// </summary>
public sealed class JoinBuilder
{
    /// <summary>Start a join from a table scan or index scan.</summary>
    public static JoinBuilder From(SharcDatabase db, string tableName);

    /// <summary>Start from an existing cursor (for composing joins).</summary>
    public static JoinBuilder FromCursor(ICursor cursor);

    /// <summary>
    /// Index nested-loop join: for each outer row, seek the inner table's index.
    /// </summary>
    /// <param name="innerTable">Target table name</param>
    /// <param name="innerIndex">Index name on inner table (must cover join column)</param>
    /// <param name="outerColumn">Column ordinal on outer side providing the key</param>
    public JoinBuilder IndexJoin(string innerTable, string innerIndex, int outerColumn);

    /// <summary>
    /// Rowid direct join: outer column contains rowid of inner table.
    /// Fastest path — no index indirection.
    /// </summary>
    public JoinBuilder RowidJoin(string innerTable, int outerRowidColumn);

    /// <summary>
    /// Sorted merge join: both sides scanned in key order from indexes.
    /// </summary>
    public JoinBuilder MergeJoin(string innerTable, string innerIndex, 
                                  int outerColumn, SortDirection outerSort);

    /// <summary>Build and return the join cursor.</summary>
    public IJoinCursor Build();
}
```

---

## 4. Implementation Plan

### Phase 1: IndexNestedLoopJoinCursor (Week 1)

This is the critical path — it unlocks 95% of join use cases with the simplest implementation.

**Files to create:**

| File | Responsibility |
|------|---------------|
| `Sharc.Core/Joins/IJoinCursor.cs` | Interface definition |
| `Sharc.Core/Joins/IRowAccessor.cs` | Column access interface |
| `Sharc.Core/Joins/IndexNestedLoopJoinCursor.cs` | Core join implementation |
| `Sharc/JoinBuilder.cs` | Public fluent API |

**Implementation sketch for `IndexNestedLoopJoinCursor`:**

```csharp
internal sealed class IndexNestedLoopJoinCursor : IJoinCursor
{
    private readonly BTreeCursor _outerCursor;      // scans outer table
    private readonly BTreeReader _innerIndex;        // index B-tree on inner table
    private readonly BTreeReader _innerTable;        // table B-tree for row fetch
    private readonly int _outerKeyOrdinal;           // which column is the join key
    private readonly RecordDecoder _decoder;         // shared, stateless

    private RowAccessorAdapter _leftAccessor;        // wraps current outer row
    private RowAccessorAdapter _rightAccessor;       // wraps current inner row

    public bool MoveNext()
    {
        // If we have pending inner matches for current outer row, advance inner
        if (AdvanceInnerMatch())
            return true;

        // Otherwise advance outer and seek inner index
        while (_outerCursor.MoveNext())
        {
            var key = ExtractKey(_outerCursor, _outerKeyOrdinal);
            if (_innerIndex.SeekToKey(key))
            {
                FetchInnerRow();
                return true;
            }
        }
        return false;
    }
}
```

**Test matrix (TDD — tests first):**

- Equi-join on integer FK, 1:1 cardinality
- Equi-join on integer FK, 1:N cardinality (multiple inner matches)
- Equi-join on text FK (e.g., `_edges.source` → `_records.id`)
- No-match rows (outer row has no inner match — inner join semantics, row skipped)
- Empty outer table, empty inner table
- Single-row tables both sides
- Join with entitlement-filtered inner table (encrypted rows invisible)

### Phase 2: RowidDirectJoinCursor (Week 1, parallel)

Trivial variant — skip the index lookup, seek directly on rowid.

```csharp
internal sealed class RowidDirectJoinCursor : IJoinCursor
{
    // Same as IndexNestedLoop but:
    // - No _innerIndex — seek _innerTable directly by rowid
    // - ExtractKey returns long rowid, not typed key
    // - Single B-tree seek per outer row, no index indirection
}
```

### Phase 3: SortedMergeJoinCursor (Week 2)

```csharp
internal sealed class SortedMergeJoinCursor : IJoinCursor
{
    private readonly BTreeCursor _leftCursor;   // index-order scan
    private readonly BTreeCursor _rightCursor;  // index-order scan
    // Small lookahead buffer for duplicate keys on right side
    // Buffer is pooled (ArrayPool<byte>), returned on Dispose
}
```

**Key implementation detail:** The duplicate-key lookahead buffer is the only allocation in merge join. Use `ArrayPool<byte>.Shared` and cap at configurable size (default 64KB). If duplicates exceed buffer, fall back to re-seek (degenerate but correct).

### Phase 4: JoinBuilder + Composition (Week 2–3)

- JoinBuilder validates index existence via `SharcSchema`
- JoinBuilder selects strategy automatically when possible (rowid FK → RowidDirect, indexed column → IndexNested, both sorted → Merge)
- Multi-way joins: `JoinBuilder.From(db, "A").IndexJoin("B", ...).IndexJoin("C", ...)` — each join wraps the previous `IJoinCursor` as its outer source

---

## 5. Memory Contract

| Operation | Allocations | Notes |
|-----------|------------|-------|
| Join cursor construction | 1 object alloc | The cursor itself |
| Per-row advance (inner loop) | **ZERO** | Span-based key extraction and comparison |
| Key extraction (int) | Zero | Inline varint decode to `long` |
| Key extraction (text) | Zero | `ReadOnlySpan<char>` from record buffer |
| Merge join lookahead buffer | Pooled | `ArrayPool<byte>.Shared`, returned on Dispose |
| Multi-way join composition | 1 alloc per join level | Outer cursor reference |

**GC pressure target:** < 100 bytes allocated per 1,000 joined row pairs on the hot path.

---

## 6. GCD Integration Pattern

The canonical GCD use case — resolving a commit's changed files with their content:

```csharp
using var db = SharcDatabase.Open("project.gcd");

// commits JOIN commit_files ON commit_files.commit_id = commits.rowid
// commit_files JOIN files ON files.rowid = commit_files.file_id
using var join = JoinBuilder
    .From(db, "commits")
    .IndexJoin("commit_files", "idx_cf_commit_id", outerColumn: 0)
    .RowidJoin("files", outerRowidColumn: 3)  // commit_files.file_id → files.rowid
    .Build();

while (join.MoveNext())
{
    var commitHash = join.Left.Left.GetText(1);   // commits.hash
    var filePath   = join.Right.GetText(1);        // files.path
    // Feed to LLM context window
}
```

**Traversal cost for 1,000 commits × 10 files each:**
- 1,000 outer advances (sequential scan, ~45µs total for 1K rows)
- 10,000 index seeks at 585ns each = ~5.85ms
- 10,000 rowid seeks at 585ns each = ~5.85ms
- **Total: ~12ms for 10,000 resolved file references — zero allocations**

---

## 7. Entitlement Integration

Joins respect row-level entitlements transparently. When the inner table has encrypted rows:

1. `BTreeCursor` on inner table already skips rows whose entitlement tag doesn't match the active `SharcEntitlementContext`
2. Index seeks may land on an encrypted row — the cursor advances to the next visible match
3. No API change required — entitlements are enforced at the cursor layer, below joins

**Consequence:** Two users running the same join on the same GCD file see different result sets based on their entitlement tags. The join cursor has no awareness of encryption — it simply never sees invisible rows.

---

## 8. Decision Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| No hash joins | Deliberate | Hash tables require O(N) allocation, violating zero-alloc contract. Index nested-loop achieves same asymptotic performance with B-tree seeks. |
| Inner join only (v0.1) | Deliberate | LEFT/OUTER joins require null-row synthesis and tracking "matched" state per outer row. Ship inner join first, add outer semantics in v0.2. |
| No automatic strategy selection | Deliberate | Caller knows their access pattern. JoinBuilder can suggest, but never silently switches strategy. |
| IJoinCursor.Left/Right not flattened | Deliberate | Flattening column ordinals across tables creates ambiguity. Nested access (`join.Left.GetText(0)`) preserves table identity. |
| Merge join buffer cap | 64KB default | Covers up to ~8,000 duplicate keys assuming 8-byte average key. Configurable via `JoinOptions.MergeBufferSize`. |
