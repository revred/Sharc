# Filter Engine Action Plan

**Source:** `secrets/SharcFilterEngine.md`
**Status:** DONE (Phase 1-2 complete; Phase 3 deferred)
**Priority:** P1
**Estimated Effort:** 6 weeks (3 phases)

> **Resolution (2026-02-23):** The `FilterStar`/`FilterTreeCompiler`/`CompileBaked` pipeline is fully implemented and achieves **0 B per-row allocation** for equality, range, prefix, suffix, and contains predicates on all types. `PreparedQuery` reuse eliminates cold-path overhead. The original 1,089 KB target was reduced to **0 B (hot)** / **680 B (cold)**. Remaining gap: `Utf8SetContains()` in `IN`/`NotIn` string set predicates still allocates one `string` per row — fixable by converting to `Dictionary<ReadOnlyMemory<byte>>` with a UTF-8 span comparer. The legacy `SharcFilter`/`ResolvedFilter` path is superseded but not yet removed.

---

## Current State Inventory

| Component | File | Status |
|:---|:---|:---|
| `SharcOperator` enum (6 ops) | `src/Sharc/SharcFilter.cs:23-42` | Replace with `FilterOp` |
| `SharcFilter` record | `src/Sharc/SharcFilter.cs:51` | Replace with `FilterStar` API |
| `ResolvedFilter` struct | `src/Sharc/FilterEvaluator.cs:25-30` | Replace with `IFilterNode` tree |
| `FilterEvaluator` static class | `src/Sharc/FilterEvaluator.cs:36-114` | Replace with `RawByteComparer` |
| `SharcDataReader.Read()` | `src/Sharc/SharcDataReader.cs:244-259` | Modify — byte-level eval |
| `SharcDataReader.EvaluateFilters()` | `src/Sharc/SharcDataReader.cs:261-280` | Replace — raw span eval |
| `SharcDatabase.CreateReader` (4 overloads) | `src/Sharc/SharcDatabase.cs:313-425` | Modify — accept `IFilterStar` |
| `SharcDatabase.ResolveFilters()` | `src/Sharc/SharcDatabase.cs:405-425` | Replace with `FilterCompiler` |
| Unit tests (18 tests) | `tests/Sharc.Tests/FilterEvaluationTests.cs` | Delete — replaced entirely |
| Integration tests (8 tests) | `tests/Sharc.IntegrationTests/FilterIntegrationTests.cs` | Rewrite for new API |

**Existing primitives reused by the filter engine:**
- `RecordDecoder.ReadSerialTypes()` — parses serial types into buffer (already exists)
- `SerialTypeCodec.GetContentSize()` — computes body offset per column (already exists)
- `BinaryPrimitives.Read*BigEndian()` — raw byte decode (stdlib)
- `MemoryExtensions.StartsWith/EndsWith/IndexOf` — SIMD-accelerated span ops (stdlib)

---

## Phase 1 — Foundation + Critical Operators (3 weeks, ~100 tests)

**Goal:** Build byte-level filter engine. Cover all P1 operators. Close the benchmark gap from 2.1x slower to within 1.5x of SQLite. Drop allocation from 1,089 KB to under 20 KB.

### Week 1 — Core Types + Raw Byte Comparers

#### Task 1.1: Create `FilterOp` enum
- **File:** `src/Sharc/Filter/FilterOp.cs` (NEW)
- **What:** Define all operator codes — P1 values (Eq, Neq, Lt, Lte, Gt, Gte, Between, IsNull, IsNotNull, StartsWith, EndsWith, Contains, In, NotIn) plus P2/P3 placeholders
- **Tests:** None (enum only)
- **Depends on:** Nothing

#### Task 1.2: Create `TypedFilterValue` discriminated union
- **File:** `src/Sharc/Filter/TypedFilterValue.cs` (NEW)
- **What:** `readonly struct` with Tag enum (Null, Int64, Double, Utf8, Utf8Set, Int64Set, Regex). Static factory methods: `FromInt64`, `FromDouble`, `FromUtf8(ReadOnlyMemory<byte>)`, `FromUtf8(string)`, `FromInt64Set`, `FromUtf8Set`, `FromRegex`, `Null()`. Zero-boxing — primitives stored inline.
- **Tests:** 8 tests — construction + tag verification for each variant
- **Depends on:** Nothing

#### Task 1.3: Create `IFilterNode` interface
- **File:** `src/Sharc/Filter/IFilterNode.cs` (NEW)
- **What:** `internal interface IFilterNode` with single method: `bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes, int bodyOffset, long rowId)`
- **Tests:** None (interface only)
- **Depends on:** Nothing

#### Task 1.4: Create `AndNode`, `OrNode`, `NotNode` combinators
- **Files:** `src/Sharc/Filter/AndNode.cs`, `OrNode.cs`, `NotNode.cs` (NEW)
- **What:**
  - `AndNode(IFilterNode[])` — short-circuit AND, empty → true
  - `OrNode(IFilterNode[])` — short-circuit OR, empty → false
  - `NotNode(IFilterNode)` — logical negation
- **Tests:** 12 tests in `tests/Sharc.Tests/Filter/LogicNodeTests.cs`
  - AND short-circuits on first false
  - AND with all true returns true
  - AND empty returns true (vacuous truth)
  - OR short-circuits on first true
  - OR with all false returns false
  - OR empty returns false
  - NOT inverts true → false and false → true
  - Deeply nested: `AND(OR(a, b), NOT(OR(c, d)))` evaluates correctly
  - Verify short-circuit by counting child evaluations
- **Depends on:** Task 1.3

#### Task 1.5: Create `RawByteComparer` static class
- **File:** `src/Sharc/Filter/RawByteComparer.cs` (NEW)
- **What:** Zero-alloc comparison primitives operating on `ReadOnlySpan<byte>`:
  - `CompareInt64(ReadOnlySpan<byte> data, long serialType, long filterValue) → int` — handles serial types 1-6, 8, 9
  - `CompareDouble(ReadOnlySpan<byte> data, double filterValue) → int` — serial type 7
  - `Utf8Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) → int` — ordinal
  - `Utf8StartsWith(span, prefix) → bool`
  - `Utf8EndsWith(span, suffix) → bool`
  - `Utf8Contains(span, pattern) → bool`
  - All marked `[MethodImpl(AggressiveInlining)]`
- **Tests:** 20 tests in `tests/Sharc.Tests/Filter/RawByteComparerTests.cs`
  - Int64 compare: serial types 1 (1-byte), 2 (2-byte), 3 (3-byte), 4 (4-byte), 5 (6-byte), 6 (8-byte), 8 (constant 0), 9 (constant 1)
  - Int64 compare: negative values, boundary values (int.MinValue, int.MaxValue, long.MinValue, long.MaxValue)
  - Double compare: positive, negative, zero, NaN behavior
  - Utf8 compare: empty strings, ASCII, multi-byte UTF-8
  - Utf8 StartsWith/EndsWith/Contains: match, no-match, empty pattern, full match
- **Depends on:** Nothing (uses `BinaryPrimitives` from stdlib)

#### Task 1.6: Create column body offset calculator
- **File:** `src/Sharc/Filter/ColumnOffsetCalculator.cs` (NEW) or inline in `PredicateNode`
- **What:** `GetColumnBodyPosition(ReadOnlySpan<long> serialTypes, int bodyStartOffset, int columnIndex) → (int Offset, int Length)`. Iterates serial types, sums `SerialTypeCodec.GetContentSize()` for columns before target. Marked `AggressiveInlining`.
- **Tests:** 5 tests — 0th column, middle column, last column, single-column record, column with serial type 0 (NULL → 0 bytes)
- **Depends on:** `SerialTypeCodec.GetContentSize()` (exists in `src/Sharc.Core/Primitives/SerialTypeCodec.cs`)

#### Task 1.7: Create `PredicateNode` (leaf evaluator)
- **File:** `src/Sharc/Filter/PredicateNode.cs` (NEW)
- **What:** `internal sealed class PredicateNode : IFilterNode` with:
  - `int ColumnOrdinal`
  - `FilterOp Operator`
  - `TypedFilterValue Value`
  - `Evaluate()`: uses column offset calculator to locate raw bytes, then dispatches to `RawByteComparer` methods based on serial type + operator. Handles:
    - Serial-type-only short circuits (IsNull → check serial type == 0, IsNotNull → != 0)
    - Type mismatch → return false (comparing int filter to text column)
    - Integer comparisons via `CompareInt64`
    - Real comparisons via `CompareDouble`
    - Text comparisons via `Utf8Compare`/`Utf8StartsWith`/etc.
- **Tests:** 25 tests in `tests/Sharc.Tests/Filter/PredicateNodeTests.cs`
  - Every P1 operator × every applicable storage class (see test matrix in spec §8.1):
    - Eq: NULL, Int, Real, Text (4 tests)
    - Neq: NULL, Int, Real, Text (4 tests)
    - Lt/Lte/Gt/Gte: Int, Real, Text (boundary cases) (6 tests)
    - IsNull: on NULL column → true, on non-NULL → false (2 tests)
    - IsNotNull: inverse (2 tests)
    - StartsWith: match, no-match, empty prefix (3 tests)
    - Between: inclusive boundaries, outside range (2 tests)
    - In (int64): present, absent (2 tests)
  - INTEGER PRIMARY KEY alias: serial type 0 on rowid alias column → use rowId param
- **Depends on:** Tasks 1.2, 1.3, 1.5, 1.6

**Week 1 total: ~70 tests across 4 test files**

---

### Week 2 — Public API + Remaining P1 Operators

#### Task 2.1: Create `IFilterStar` interface + `FilterStar` fluent API
- **Files:** `src/Sharc/Filter/FilterStar.cs` (NEW)
- **What:**
  - `public interface IFilterStar` — marker interface for composable filter expressions
  - `public static class FilterStar` — entry points:
    - `Column(string name) → ColumnRef`
    - `Column(int ordinal) → ColumnRef`
    - `And(params IFilterStar[]) → IFilterStar`
    - `Or(params IFilterStar[]) → IFilterStar`
    - `Not(IFilterStar) → IFilterStar`
  - `public readonly struct ColumnRef` — typed predicate builder:
    - Eq/Neq/Lt/Lte/Gt/Gte overloads for long, double, string
    - Between(long, long) / Between(double, double)
    - IsNull() / IsNotNull()
    - StartsWith(string) / EndsWith(string) / Contains(string)
    - In(params long[]) / In(params string[])
    - NotIn(params long[]) / NotIn(params string[])
- **Tests:** 15 tests in `tests/Sharc.Tests/Filter/FilterStarTests.cs`
  - Construction: Column("age").Gt(30) produces correct IFilterStar
  - And/Or/Not composition: verify tree structure
  - Column by name vs ordinal both work
  - All P1 operators callable without runtime error
  - API usage examples from spec §4.2 compile and produce nodes
- **Depends on:** Tasks 1.1, 1.2

#### Task 2.2: Create `FilterCompiler`
- **File:** `src/Sharc/Filter/FilterCompiler.cs` (NEW)
- **What:** `internal static class FilterCompiler` — transforms `IFilterStar` tree into `IFilterNode` tree:
  - `Compile(IFilterStar expression, IReadOnlyList<ColumnInfo> columns) → IFilterNode`
  - Resolves column names to ordinals (case-insensitive)
  - Validates column exists (throws `ArgumentException` if not)
  - Pre-encodes string filter values to UTF-8 bytes (one-time allocation at compile)
  - Validates tree depth ≤ 32 (stack overflow guard)
- **Tests:** 10 tests in `tests/Sharc.Tests/Filter/FilterCompilerTests.cs`
  - Compiles simple predicate → PredicateNode with correct ordinal
  - Compiles And/Or/Not → correct node tree
  - Column name resolution is case-insensitive
  - Invalid column name throws ArgumentException
  - Tree depth > 32 throws ArgumentException
  - String values pre-encoded to UTF-8 in TypedFilterValue
- **Depends on:** Tasks 1.4, 1.7, 2.1

#### Task 2.3: EndsWith + Contains on `PredicateNode`
- **Already handled in Task 1.7** — EndsWith and Contains use `RawByteComparer.Utf8EndsWith` / `Utf8Contains`
- **Additional tests:** 4 tests
  - EndsWith: match, no-match
  - Contains: substring present in middle, absent

#### Task 2.4: In / NotIn operators on `PredicateNode`
- **Extend:** `PredicateNode.Evaluate()` for `FilterOp.In` and `FilterOp.NotIn`
- **What:**
  - Int64 In: linear scan of `TypedFilterValue._int64Set`, return true on match
  - Utf8 In: linear scan of `TypedFilterValue._utf8Set`, `SequenceEqual` per entry
  - NotIn: negate In result
- **Tests:** 6 tests
  - In (int): value present, value absent, empty set
  - In (string): value present, value absent
  - NotIn: inverse behavior
- **Depends on:** Task 1.7

**Week 2 total: ~35 tests across 3 test files**

---

### Week 3 — Integration + Benchmark Validation

#### Task 3.1: Wire `IFilterNode` into `SharcDataReader.Read()`
- **File:** `src/Sharc/SharcDataReader.cs` (MODIFY)
- **What:** Replace the current `EvaluateFilters()` flow with byte-level evaluation:
  1. Add `_filterNode` field (IFilterNode?)
  2. Add `_serialTypesBuffer` field (long[] — allocated once at reader creation)
  3. Modify `Read()` loop:
     - If `_filterNode is null` → existing fast path (no change)
     - Otherwise: parse serial types via `_recordDecoder.ReadSerialTypes()`, compute body offset, call `_filterNode.Evaluate()` on raw payload
     - Only call `DecodeCurrentRow()` on matching rows
  4. Remove `_filters` (ResolvedFilter[]) field
  5. Remove `EvaluateFilters()` method
  6. Handle INTEGER PRIMARY KEY alias: pass `_cursor.RowId` to `Evaluate()`
- **Tests:** Covered by integration tests (Task 3.3)
- **Depends on:** Tasks 1.7, 2.2

#### Task 3.2: Update `SharcDatabase.CreateReader()` overloads
- **File:** `src/Sharc/SharcDatabase.cs` (MODIFY)
- **What:**
  - Add new overload: `CreateReader(string tableName, IFilterStar filter)`
  - Add new overload: `CreateReader(string tableName, string[]? columns, IFilterStar filter)`
  - Remove old `SharcFilter[]` overloads (breaking change — intentional)
  - Remove `ResolveFilters()` method
  - Use `FilterCompiler.Compile()` to produce `IFilterNode` from `IFilterStar`
  - Pass compiled `IFilterNode` to `SharcDataReader` constructor
- **Tests:** Covered by integration tests (Task 3.3)
- **Depends on:** Tasks 2.2, 3.1

#### Task 3.3: Integration tests for all P1 operators
- **File:** `tests/Sharc.IntegrationTests/FilterEngineIntegrationTests.cs` (NEW)
- **What:** End-to-end tests with real `.db` files via `TestDatabaseFactory`:
  - Basic Eq/Neq/Lt/Lte/Gt/Gte on integer, real, text columns
  - AND: multiple filters combined
  - OR: `status = 'active' OR status = 'pending'`
  - NOT: exclude deleted records
  - IsNull / IsNotNull on nullable column
  - StartsWith / EndsWith / Contains on text column
  - In (int) / In (string) on columns
  - Between on integer and real columns
  - Complex nested: `AND(age BETWEEN 18 AND 65, OR(name.StartsWith("A"), city.Eq("London")))`
  - Filter + column projection combination
  - Filter on INTEGER PRIMARY KEY alias column
  - Empty result set (no matches)
  - Invalid column name throws ArgumentException
- **Tests:** ~25 tests
- **Depends on:** Tasks 3.1, 3.2

#### Task 3.4: Delete old filter system
- **Files:**
  - DELETE `src/Sharc/SharcFilter.cs` (entire file — `SharcFilter` record + `SharcOperator` enum)
  - DELETE `src/Sharc/FilterEvaluator.cs` (entire file)
  - DELETE `tests/Sharc.Tests/FilterEvaluationTests.cs` (18 old tests)
  - REWRITE `tests/Sharc.IntegrationTests/FilterIntegrationTests.cs` → replaced by Task 3.3
- **What:** Remove all legacy filter code. The old `SharcFilter[]` API is fully replaced by `FilterStar`. Any external references (benchmarks, Arena) must be updated to use the new API.
- **Depends on:** Tasks 3.1, 3.2, 3.3 (all new code working first)

#### Task 3.5: Update benchmark to use new API
- **File:** `bench/Sharc.Comparisons/CoreBenchmarks.cs` (MODIFY) or equivalent
- **What:** Update the WHERE filter benchmark to use `FilterStar`:
  ```csharp
  using var reader = db.CreateReader("users",
      FilterStar.And(
          FilterStar.Column("age").Gt(30),
          FilterStar.Column("score").Lt(50)
      ));
  ```
- **Validate:** Speed ≤ 800 μs, Allocation ≤ 20 KB
- **Depends on:** Task 3.2

#### Task 3.6: Update Arena engine if applicable
- **File:** `src/Sharc.Arena.Wasm/Services/SharcEngine.cs` (MODIFY if it uses SharcFilter)
- **What:** Replace any `SharcFilter[]` usage with `FilterStar` API
- **Depends on:** Task 3.2

**Week 3 total: ~25 tests + deletions + benchmark validation**

---

## Phase 1 Exit Criteria

- [ ] ~100 new tests passing
- [ ] Old `SharcFilter`/`FilterEvaluator`/`SharcOperator` deleted
- [ ] WHERE filter benchmark: speed ≤ 800 μs (currently 1,130 μs)
- [ ] WHERE filter benchmark: allocation ≤ 20 KB (currently 1,089 KB)
- [ ] 18 P1 operators functional: Eq, Neq, Lt, Lte, Gt, Gte, Between, IsNull, IsNotNull, StartsWith, EndsWith, Contains, In (int), In (string), NotIn (int), NotIn (string), AND, OR, NOT
- [ ] All existing tests still passing (no read-path regression)
- [ ] `dotnet test` — all green

---

## Phase 2 — Extended Operators (2 weeks, ~40 tests)

**Goal:** Add P2 operators to reach 90% SurrealDB filter coverage.

### Week 4 — Advanced String + Type Operators

#### Task 4.1: `ExactEq` operator
- **File:** `src/Sharc/Filter/PredicateNode.cs` (MODIFY)
- **What:** Equality that also checks storage class match (serial type category must match filter value type)
- **Tests:** 4 tests — type match succeeds, type mismatch fails, NULL handling

#### Task 4.2: `EqIgnoreCase` operator
- **File:** `src/Sharc/Filter/RawByteComparer.cs` (MODIFY — add `Utf8CompareIgnoreCase`)
- **What:** Case-insensitive UTF-8 comparison. Use `Ascii.EqualsIgnoreCase` for ASCII fast path, fall back to `string.Compare(Ordinal, IgnoreCase)` with one-time allocation for non-ASCII
- **Tests:** 5 tests — ASCII match, ASCII case-insensitive, non-ASCII, empty strings

#### Task 4.3: `Matches` (regex) operator
- **File:** `src/Sharc/Filter/PredicateNode.cs` (MODIFY)
- **What:** Pre-compiled `Regex` stored in `TypedFilterValue._regex`. On evaluation, materialize UTF-8 span to string (one-time per row for regex only), call `Regex.IsMatch()`.
- **Tests:** 4 tests — simple match, no match, complex pattern, empty string

#### Task 4.4: `ContainsNot` operator
- **File:** `src/Sharc/Filter/PredicateNode.cs` (MODIFY)
- **What:** Negation of Contains — `!Utf8Contains(span, pattern)`
- **Tests:** 2 tests — substring absent (true), substring present (false)

#### Task 4.5: Add ColumnRef methods for P2 string operators
- **File:** `src/Sharc/Filter/FilterStar.cs` (MODIFY)
- **What:** Add `ContainsNot(string)`, `EqIgnoreCase(string)`, `Matches(string)` to `ColumnRef`
- **Tests:** 3 tests — API construction tests

**Week 4: ~18 tests**

### Week 5 — Array/Set Operators + Computed Expressions

#### Task 5.1: Array column support via `Utf8JsonReader`
- **File:** `src/Sharc/Filter/ArrayParser.cs` (NEW)
- **What:** Parse JSON arrays from TEXT columns using `System.Text.Json.Utf8JsonReader` (zero-alloc JSON parsing). Extract int64 or UTF-8 elements for set operations.
- **Tests:** 5 tests — parse int array, string array, mixed, empty, malformed

#### Task 5.2: `ContainsAll` / `ContainsAny` / `AnyEq` / `AnyIn` operators
- **File:** `src/Sharc/Filter/PredicateNode.cs` (MODIFY)
- **What:**
  - ContainsAll: every filter set value found in column array
  - ContainsAny: any filter set value found in column array
  - AnyEq: any column array element equals scalar filter value
  - AnyIn: any column array element is in filter set
- **Tests:** 12 tests — each operator × match/no-match/empty cases

#### Task 5.3: Computed expression support
- **File:** `src/Sharc/Filter/ComputeNode.cs` (NEW)
- **What:** Arithmetic expressions in filters: `FilterStar.Compute(col, "+", 10).Gt(100)`. Evaluate simple arithmetic (+ - * /) on column values before comparison.
- **Tests:** 5 tests — add, subtract, multiply, divide, division by zero

**Week 5: ~22 tests**

---

## Phase 2 Exit Criteria

- [ ] ~140 total new tests
- [ ] 28+ operators functional (90%+ SurrealDB coverage)
- [ ] P2 operators: ExactEq, EqIgnoreCase, Matches, ContainsNot, ContainsAll, ContainsAny, AnyEq, AnyIn, computed expressions
- [ ] WHERE filter benchmark: speed ≤ 700 μs (within 1.3x of SQLite)
- [ ] WHERE filter benchmark: allocation ≤ 10 KB

---

## Phase 3 — Graph Edge Filtering Integration (1 week, ~15 tests)

### Week 6

#### Task 6.1: Extend edge cursors to accept `IFilterNode`
- **Files:**
  - `src/Sharc.Core/BTree/IndexBTreeCursor.cs` (MODIFY)
  - `src/Sharc.Graph/Store/RelationStore.cs` (MODIFY)
- **What:** Allow `IEdgeCursor` implementations to accept an optional `IFilterNode` for edge-level predicates. Replace the current `_kindFilter.HasValue && kindVal != (int)_kindFilter.Value` managed check in edge iteration with byte-level predicate on the edge kind column.
- **Tests:** 8 tests — filter by edge kind, filter by weight range, filter by data field, combined edge filters

#### Task 6.2: Expose edge filtering in graph API
- **Files:**
  - `src/Sharc.Graph/SharcContextGraph.cs` (MODIFY)
  - `src/Sharc.Graph.Surface/IContextGraph.cs` (MODIFY if needed)
- **What:** Add `GetEdges(NodeKey origin, IFilterStar edgeFilter)` overload
- **Tests:** 4 tests — edge filter via graph API, filter + kind, no matches, combined

#### Task 6.3: Benchmark edge filter improvement
- **What:** Measure edge filter allocation — target drop from 2,087x to under 10x vs SQLite
- **Tests:** 3 benchmark validation tests

---

## Phase 3 Exit Criteria

- [ ] ~155 total new tests
- [ ] Edge filter allocation: under 10x SQLite (currently 2,087x)
- [ ] Graph edge filtering via `IFilterStar` API
- [ ] All existing graph tests still passing

---

## New File Summary

| File | Phase | Type |
|:---|:---:|:---|
| `src/Sharc/Filter/FilterOp.cs` | 1 | Enum — all operator codes |
| `src/Sharc/Filter/TypedFilterValue.cs` | 1 | Struct — zero-boxing value container |
| `src/Sharc/Filter/IFilterNode.cs` | 1 | Interface — evaluation contract |
| `src/Sharc/Filter/AndNode.cs` | 1 | Class — AND combinator |
| `src/Sharc/Filter/OrNode.cs` | 1 | Class — OR combinator |
| `src/Sharc/Filter/NotNode.cs` | 1 | Class — NOT combinator |
| `src/Sharc/Filter/PredicateNode.cs` | 1 | Class — leaf evaluator |
| `src/Sharc/Filter/RawByteComparer.cs` | 1 | Static class — span comparisons |
| `src/Sharc/Filter/FilterStar.cs` | 1 | Static class + struct — public API |
| `src/Sharc/Filter/FilterCompiler.cs` | 1 | Static class — expression → node tree |
| `src/Sharc/Filter/ArrayParser.cs` | 2 | Static class — JSON array parse |
| `src/Sharc/Filter/ComputeNode.cs` | 2 | Class — arithmetic expressions |
| `tests/Sharc.Tests/Filter/RawByteComparerTests.cs` | 1 | Unit tests |
| `tests/Sharc.Tests/Filter/LogicNodeTests.cs` | 1 | Unit tests |
| `tests/Sharc.Tests/Filter/PredicateNodeTests.cs` | 1 | Unit tests |
| `tests/Sharc.Tests/Filter/FilterStarTests.cs` | 1 | Unit tests |
| `tests/Sharc.Tests/Filter/FilterCompilerTests.cs` | 1 | Unit tests |
| `tests/Sharc.IntegrationTests/FilterEngineIntegrationTests.cs` | 1 | Integration tests |

## Deleted Files

| File | Phase | Reason |
|:---|:---:|:---|
| `src/Sharc/SharcFilter.cs` | 1 | Replaced by `FilterStar` + `FilterOp` |
| `src/Sharc/FilterEvaluator.cs` | 1 | Replaced by `RawByteComparer` + `IFilterNode` |
| `tests/Sharc.Tests/FilterEvaluationTests.cs` | 1 | Replaced by new test files |

## Modified Files

| File | Phase | Changes |
|:---|:---:|:---|
| `src/Sharc/SharcDataReader.cs` | 1 | New `Read()` pipeline with byte-level eval |
| `src/Sharc/SharcDatabase.cs` | 1 | New `CreateReader` overloads, remove old |
| `tests/Sharc.IntegrationTests/FilterIntegrationTests.cs` | 1 | Rewrite or replace |
| `bench/Sharc.Comparisons/CoreBenchmarks.cs` | 1 | Update WHERE benchmark API |
| `src/Sharc.Arena.Wasm/Services/SharcEngine.cs` | 1 | Update filter API calls |
| `src/Sharc.Core/BTree/IndexBTreeCursor.cs` | 3 | Accept IFilterNode for edges |
| `src/Sharc.Graph/Store/RelationStore.cs` | 3 | Edge cursor filter support |
| `src/Sharc.Graph/SharcContextGraph.cs` | 3 | Expose edge filter API |

---

## Risk Mitigations

| Risk | Mitigation |
|:---|:---|
| Body offset wrong for overflow pages | Overflow assembles into contiguous span via `BTreeCursor.Payload` — offsets computed against assembled payload |
| Regex requires string materialization | Accept one-time alloc for P2 Matches. Pre-compile Regex at filter creation. Not on benchmark hot path. |
| INTEGER PRIMARY KEY alias (rowid as NULL) | Special-case serial type 0 at `_rowidAliasOrdinal`: check `cursor.RowId` directly |
| Large IN sets (1000+ values) slow | For int64 sets > 16: sort + binary search. For utf8 sets > 16: HashSet at compile time. |
| Expression tree depth → stack overflow | Cap at 32 in `FilterCompiler`. Real filters rarely exceed depth 5. |

---

## Success Metrics

| Metric | Current | Phase 1 | Phase 2 | Phase 3 |
|:---|---:|---:|---:|---:|
| WHERE speed | 1,130 μs | ≤ 800 μs | ≤ 700 μs | ≤ 700 μs |
| WHERE allocation | 1,089 KB | ≤ 20 KB | ≤ 10 KB | ≤ 10 KB |
| Operator count | 6 | 18 | 28+ | 28+ |
| SurrealDB coverage | ~16% | ~49% | ~89% | ~89% |
| Boolean logic | AND only | AND/OR/NOT | AND/OR/NOT | AND/OR/NOT |
| New tests | 0 | ~100 | ~140 | ~155 |
| Edge filter alloc ratio | 2,087x | — | — | < 10x |
