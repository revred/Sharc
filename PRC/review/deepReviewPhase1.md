# 🦈 Sharc — Deep Code Review
## Full Codebase Analysis · February 12, 2026

**Scope:** Every `.cs` file, `.csproj`, `Directory.Build.props`, README, PRC docs, benchmarks.
**Codebase:** ~5,000 LOC across 50 source files, 39 test files (~7,500 LOC), 22 benchmark files.

---

## Executive Summary

The Sharc core reader (Sharc + Sharc.Core) is genuinely well-engineered. The B-tree cursor, varint decoder, serial type codec, and record decoder are tight, correct implementations of the SQLite file format. The layered architecture (Primitives → Page I/O → B-Tree → Records → Schema → Public API) is clean and each boundary is well-defined through interfaces.

However, the codebase has **27 concrete issues** ranging from correctness bugs to inconsistencies that would undermine the "link quality" impression you're aiming for. Several are things that experienced engineers would spot in seconds and use to question the benchmark claims.

This review is organized by severity: Correctness Bugs → Architectural Inconsistencies → Benchmark Honesty → Code Quality Nits → README/Presentation.

---

## 🔴 CORRECTNESS BUGS (5 issues)

### BUG-01: `ConceptStore.Get()` Assumes BarID == SQLite Rowid

**File:** `src/Sharc.Graph/Store/ConceptStore.cs:63`

```csharp
public GraphRecord? Get(NodeKey key)
{
    using var cursor = _reader.CreateCursor((uint)_tableRootPage);
    if (cursor.Seek(key.Value))  // ← Seeks by ROWID
    {
        var columns = decoder.DecodeRecord(cursor.Payload);
        return MapToRecord(columns, key);
    }
    return null;
}
```

`BTreeCursor.Seek(long rowId)` performs a B-tree binary search on the **SQLite rowid**, not on the `BarID` column. In the Maker.AI schema, `BarID` is a separate column — it is NOT the rowid unless the table was created with `BarID INTEGER PRIMARY KEY` (which it wasn't — the Entity table uses an implicit rowid). This means `Get(NodeKey)` seeks to the *wrong row* unless `BarID` happens to equal the rowid, which is coincidental at best.

**Fix:** This needs a full table scan with a BarID column filter (like `RelationStore.GetEdges` does), or an index scan on the BarID index. The current code silently returns wrong data.

### BUG-02: `ConceptStore` Allocates a New `RecordDecoder` Per Call

**File:** `src/Sharc.Graph/Store/ConceptStore.cs:61`

```csharp
public GraphRecord? Get(NodeKey key)
{
    var decoder = new RecordDecoder();  // ← Allocated on every call
```

`RecordDecoder` is stateless — it should be a shared field (or even a singleton). This contradicts the zero-allocation philosophy and creates unnecessary GC pressure on a hot path. The same issue exists in `RelationStore.GetEdges()` at line 68.

**Fix:** Store `RecordDecoder` as a `private readonly` field initialized in the constructor.

### BUG-03: `RelationStore.GetEdges()` Uses Allocating `DecodeRecord` Overload

**File:** `src/Sharc.Graph/Store/RelationStore.cs:74`

```csharp
while (cursor.MoveNext())
{
    var columns = decoder.DecodeRecord(cursor.Payload);  // ← NEW array per row
```

This allocates a fresh `ColumnValue[]` for every edge row during a full table scan. For the Maker.AI database (4,861 edges), that's 4,861 array allocations. The buffer-reuse overload `DecodeRecord(payload, destination)` exists specifically for this pattern — `SharcDataReader` uses it correctly.

**Fix:** Pre-allocate a `ColumnValue[]` buffer and use the reuse overload.

### BUG-04: `FilePageSource.GetPage()` Is Not Thread-Safe

**File:** `src/Sharc.Core/IO/FilePageSource.cs` — `_pageBuffer` shared across calls

The existing TODO doc (item #2) correctly identifies this. What it doesn't mention: the `CachedPageSource` lock only protects the *cache* path. If `_capacity == 0`, it calls `_inner.GetPage()` without any lock:

```csharp
// CachedPageSource.cs:61
if (_capacity == 0)
    return _inner.GetPage(pageNumber);  // ← No lock, FilePageSource._pageBuffer corrupts
```

So the "mitigated by CachedPageSource lock" claim in the TODO is incorrect for the `PageCacheSize = 0` case.

**Fix:** Either make `FilePageSource._pageBuffer` thread-local, or allocate per-call when thread safety is needed.

### BUG-05: `NodeKey.ToAscii()` Returns Garbage for Non-ASCII-Encoded Keys

**File:** `src/Sharc.Graph.Abstractions/Model/NodeKey.cs:24`

If a `NodeKey` wraps a large integer that wasn't encoded from ASCII (e.g., a raw 48-bit timestamp or a hash), `ToAscii()` will interpret random bytes as ASCII, producing garbage strings with control characters. There's no validation that the bytes are actually printable ASCII.

```csharp
public string ToAscii()
{
    // ...
    return Encoding.ASCII.GetString(bytes[start..]);  // ← No validation
}
```

**Fix:** Add a validity check. If any byte is outside 0x20-0x7E, return the numeric string representation instead.

---

## 🟠 ARCHITECTURAL INCONSISTENCIES (8 issues)

### ARCH-01: `RecordId.FullId` Allocates via String Interpolation on Every Access

**File:** `src/Sharc.Graph.Abstractions/Model/RecordId.cs:27`

```csharp
public string FullId => $"{Table}:{Id}";  // ← New string every property access
```

This is a computed property that allocates on every call. `FullId` is used in `ToString()`, the implicit `string` conversion operator, and potentially in hash sets or dictionaries. For a `readonly record struct`, this property will be called repeatedly during graph traversal.

**Fix:** Cache the concatenated string in a `private readonly string _fullId` field set in the constructor. Since `RecordId` is a `readonly record struct`, this adds 8 bytes per instance but eliminates repeated allocations.

### ARCH-02: `RecordId.HasIntegerKey` Returns False for Key == 0

**File:** `src/Sharc.Graph.Abstractions/Model/RecordId.cs:30`

```csharp
public bool HasIntegerKey => Key.Value != 0;
```

In our prior analysis of the Maker.AI database, we found **19 edges with `OriginID = 0`**. Zero is a valid key value. This property would incorrectly report those records as having no integer key.

**Fix:** Use a nullable `NodeKey?` or add an explicit `bool _hasKey` field set by the constructor overloads.

### ARCH-03: `SharcOpenOptions.FileShareMode` Is Accepted But Ignored

**File:** `src/Sharc/SharcDatabase.cs:107` vs `src/Sharc/SharcOpenOptions.cs:44`

The `Open()` method creates `FilePageSource` without passing `options.FileShareMode`:

```csharp
pageSource = new FilePageSource(path, options.FileShareMode);  // ← Actually IS wired
```

Wait — looking at the actual code more carefully, this IS wired. The TODO doc (item #1) says it's not, but the code shows `FilePageSource(path, options.FileShareMode)`. **The TODO doc is stale/wrong.** However, when `PreloadToMemory == true`, the `File.ReadAllBytes(path)` call on line 105 uses `FileAccess.Read` and `FileShare.Read` internally — it does NOT respect `FileShareMode`.

**Fix:** For the `PreloadToMemory` path, use `File.Open(path, new FileStreamOptions { Share = options.FileShareMode })` and then read into memory.

### ARCH-04: Graph Stores Don't Implement `IContextGraph`

**File:** `src/Sharc.Graph.Abstractions/IContextGraph.cs`

The `IContextGraph` interface defines `Traverse()`, `GetNode()`, `GetEdges()` — but there is **no class that implements it**. `ConceptStore` and `RelationStore` are separate internal classes. There's no `SharcGraph` or `ContextGraph` facade class.

This means the entire published Graph API contract is unimplemented. Users can't actually use `IContextGraph` to do anything.

**Fix:** Create a `SharcContextGraph : IContextGraph` that composes `ConceptStore` + `RelationStore` and wires up `Traverse()` with BFS/DFS.

### ARCH-05: `GraphRecord.JsonData` Has a Private Setter — But No Method Sets It

**File:** `src/Sharc.Graph.Abstractions/Model/GraphRecord.cs:25`

```csharp
public string JsonData { get; private set; }
// ...
public DateTimeOffset UpdatedAt { get; private set; }
```

Both `JsonData` and `UpdatedAt` have `private set` (not `init`), implying they should be mutable. But there's no `Update()` method, no `SetJsonData()` method — nothing that would call the setter. This is either dead design surface or an incomplete write path.

**Fix:** Either make these `init` (read-only after construction) to match the current read-only reality, or add the mutation methods.

### ARCH-06: `NativeSchemaAdapter` References Column `alias` That Doesn't Exist

**File:** `src/Sharc.Graph/Schema/NativeSchemaAdapter.cs:75`

```csharp
"CREATE UNIQUE INDEX IF NOT EXISTS idx_concepts_alias ON _concepts(alias) WHERE alias IS NOT NULL",
```

The native schema's `NodeTableName` is `_concepts`, with columns: `id`, `key`, `kind`, `data`, `cvn`, `lvn`, `sync_status`, `updated_at`. There is no `alias` column defined anywhere in the adapter or its interface. This DDL would fail on execution.

**Fix:** Either add `alias` to the `ISchemaAdapter` interface (as `NodeAliasColumn`), or remove this index DDL.

### ARCH-07: `using Sharc.Core.Primitives` Imported But Unused

**File:** `src/Sharc.Graph/Store/ConceptStore.cs:20` and `src/Sharc.Graph/Store/RelationStore.cs:20`

Both stores import `Sharc.Core.Primitives` but never use `VarintDecoder` or `SerialTypeCodec` directly. With `TreatWarningsAsErrors = true`, this should be a build error — the fact that it compiles suggests either the analyzer isn't catching it, or there's an `ImplicitUsings` interaction hiding it.

**Fix:** Remove the unused imports.

### ARCH-08: `Sharc.Schema` Namespace Leaks Across Assembly Boundaries

The `SharcSchema`, `TableInfo`, `ColumnInfo`, `IndexInfo`, `ViewInfo` types live in the `Sharc.Schema` namespace but are defined in `Sharc.Core` (file `src/Sharc.Core/Schema/SchemaModels.cs`). Both `Sharc` (the public API project) and `Sharc.Graph` consume these types via `using Sharc.Schema`. This namespace doesn't match its assembly (`Sharc.Core`), which is confusing for anyone reading the code.

**Fix:** Either move `SchemaModels.cs` to the `Sharc` project (since it's public API), or rename the namespace to `Sharc.Core.Schema` and adjust consumers.

---

## 🟡 BENCHMARK HONESTY (4 issues)

These won't crash anything, but experienced engineers will spot them and question the credibility of the entire benchmark suite.

### BENCH-01: Sharc Allocations Are Higher Than SQLite in Most Benchmarks

**README claims:** "Zero GC pressure" and "0 B allocated" for primitive ops.

**Actual benchmark data in the README:**

| Benchmark | Sharc Alloc | SQLite Alloc |
|---|---:|---:|
| Schema: List tables | **40 KB** | 872 B |
| Schema: Column info (1 table) | **40 KB** | 696 B |
| Schema: Column info (all) | **40 KB** | 4.0 KB |
| Schema: Batch 100 reads | **4.0 MB** | 85 KB |
| Scan 100K rows (events) | **40 KB** | 688 B |
| Scan 100K rows (ints) | **41 KB** | 704 B |
| GC: Sustained 300K rows | **121 KB** | 2.1 KB |
| GC: Sustained 1M ints | **407 KB** | 7.0 KB |

In **every** schema benchmark and every sustained scan benchmark, Sharc allocates **10x–50x more** than SQLite. The "zero GC pressure" headline is only true for the isolated primitive micro-benchmarks, not for any realistic workload.

**This directly relates to your observation that "memory usage in realistic workloads — SQLite beats Sharc hands down."** The 40 KB baseline is the schema parsing at open time (allocates `List<TableInfo>`, `List<ColumnInfo>`, strings for every column name, etc.). Every `SharcDatabase.Open()` pays this cost.

Notably, the `PRC/catch.png` infographic prominently displays "Zero Allocations" as a Sharc selling point — making this inconsistency visible to anyone who reads both the graphic and the benchmark table in the same README.

**Fix for README:** Be honest about the tradeoff. Sharc trades memory for speed. The "Zero GC pressure" claim should be scoped to "zero per-row allocation on hot read paths" which is genuinely impressive and true. The headline should be something like: "Read SQLite files at 2x–40x the speed of Microsoft.Data.Sqlite. Pure C#. Zero native dependencies." Drop the "Zero GC pressure" from the hero line.

### BENCH-02: "41x Faster Seek" Comparison Is Unfair

**README:** "B-tree point lookup (Seek): 41x faster"

Sharc's `Seek()` finds a row by SQLite rowid via B-tree binary search — this is direct byte-level page navigation.

SQLite's equivalent via `Microsoft.Data.Sqlite` uses `WHERE key = ?` which goes through: SQL parsing → query planning → VDBE → B-tree seek → result marshalling. These aren't measuring the same thing. SQLite's B-tree seek is also O(log N), but the benchmark is measuring the entire SQL pipeline overhead, not the seek itself.

**Fix:** The benchmark is valid as a "total API cost" comparison, but the README should frame it as "end-to-end lookup including SQL overhead" rather than implying SQLite's B-tree implementation is 41x slower.

### BENCH-03: Graph Benchmark "Batch 6 Node Lookups" Shows 77.8x — Suspiciously High

| Operation | Sharc | SQLite | Speedup |
|---|---:|---:|---:|
| Single lookup | 593 ns | 24,136 ns | 40.7x |
| Batch 6 lookups | 1,841 ns | 143,269 ns | 77.8x |

6 lookups at 593 ns each = 3,558 ns. Sharc's batch (1,841 ns) is faster than 6 × single (3,558 ns) — plausible due to page cache warmth. But SQLite's batch (143,269 ns) is 5.9x slower than 6 × single (144,816 ns) — effectively identical, which is expected.

The 77.8x speedup comes from Sharc's batch being sub-linear (page cache) while SQLite's is linear. This is a cache effect, not a fundamental algorithmic advantage. Worth noting.

### BENCH-04: `Microsoft.Data.Sqlite` Version Mismatch in Benchmarks

**Files:**
- `bench/Sharc.Benchmarks/Sharc.Benchmarks.csproj` → `Microsoft.Data.Sqlite 9.0.4`
- `bench/Sharc.Comparisons/Sharc.Comparisons.csproj` → `Microsoft.Data.Sqlite 9.0.0`
- `tests/Sharc.IntegrationTests/Sharc.IntegrationTests.csproj` → `Microsoft.Data.Sqlite 9.0.2`

Three different versions of the same package across three projects. This undermines benchmark reproducibility and could mean the Graph benchmarks (Comparisons, using 9.0.0) run against a different SQLite native binary than the core benchmarks (Benchmarks, using 9.0.4).

**Fix:** Pin to a single version in `Directory.Packages.props`.

---

## 🔵 CODE QUALITY (6 issues)

These are things that an experienced engineer opening the repo would notice and that affect the "well-engineered" impression.

### QUALITY-01: UTF-8 BOM Inconsistency

Some files have UTF-8 BOM (`﻿` at byte level, visible as `Ã¢â‚¬â€` mojibake in the headers), some don't. The Graph.Abstractions files have BOM; the Sharc.Core files don't. This causes the decorative header comment to render with mojibake on some systems:

```
Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
```

vs the correct rendering:

```
Subtle conversations often begin with a single message — or a prompt with the right context.
```

**Fix:** Standardize. Add `charset = utf-8-bom` or `charset = utf-8` to `.editorconfig` and normalize all files.

### QUALITY-02: `CreateTableParser.IsTableConstraint` Allocates Unnecessarily

**File:** `src/Sharc.Core/Schema/CreateTableParser.cs:99`

```csharp
var upper = segment.TrimStart().ToUpperInvariant();  // ← Allocates trimmed + uppercased string
return upper.StartsWith("PRIMARY KEY", StringComparison.Ordinal) || ...
```

`ToUpperInvariant()` allocates a new string, then `StartsWith` with `StringComparison.Ordinal` compares it. Since `StartsWith` already supports `StringComparison.OrdinalIgnoreCase`, the allocation is unnecessary:

```csharp
var trimmed = segment.AsSpan().TrimStart();
return trimmed.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) || ...
```

Same pattern in `ReadTypeName` at line 146 — `ToUpperInvariant()` followed by ordinal `StartsWith`. This runs once per schema column, so it's not hot-path critical, but it contradicts the zero-allocation philosophy.

### QUALITY-03: `ColumnValue.Integer(8, 0)` and `Integer(9, 1)` Store Misleading Serial Types

**File:** `src/Sharc.Core/Records/RecordDecoder.cs:150-151`

```csharp
8 => ColumnValue.Integer(8, 0),   // Serial type 8 = constant 0
9 => ColumnValue.Integer(9, 1),   // Serial type 9 = constant 1
```

The `ColumnValue.Integer(serialType, value)` stores the serial type in the struct. But serial types 8 and 9 are "constant integer 0" and "constant integer 1" — they're shorthand for `ColumnValue.Integer(1, 0)` and `ColumnValue.Integer(1, 1)` (1-byte integer encoding). Storing serial type 8/9 means consumers checking `SerialType` get back 8 or 9 instead of a standard integer serial type (1-6). This might confuse code that switches on `SerialType` directly.

**Fix:** Either document this clearly in `ColumnValue`, or store the canonical serial type (1 for both cases).

### QUALITY-04: `Sharc.Crypto` Project Is an Empty Shell

The project exists, compiles, and is referenced by test projects — but contains zero source files. It adds build time (even if minimal) and creates a confusing impression. The `SharcEncryptionOptions.cs` file in the main `Sharc` project defines `SharcKdfAlgorithm` and `SharcCipherAlgorithm` enums that reference Argon2id and AES-256-GCM, but no implementation exists anywhere.

**Fix:** Either remove the project from the solution until encryption work begins, or add a single `Placeholder.cs` with a clear comment explaining it's reserved for Milestone 9.

### QUALITY-05: `GraphRecord` Constructor Sets `CreatedAt` to `UtcNow` — Wrong for Read Path

**File:** `src/Sharc.Graph.Abstractions/Model/GraphRecord.cs:40`

```csharp
public GraphRecord(RecordId id, NodeKey key, int typeId, string jsonData)
{
    // ...
    CreatedAt = DateTimeOffset.UtcNow;  // ← Meaningless when reading from DB
    UpdatedAt = CreatedAt;
}
```

When reading records from the database, the `CreatedAt` should come from the database column, not from `DateTimeOffset.UtcNow`. The current code means every record read from the Maker.AI database gets a creation timestamp of "right now," which is semantically wrong.

**Fix:** Either pass `createdAt` as a constructor parameter, or make it `init`-only and set it from the database column in `ConceptStore.MapToRecord()`.

### QUALITY-06: One Remaining `TODO` in Production Source

**File:** `src/Sharc.Graph/Store/RelationStore.cs:67`

```csharp
// TODO: Implement Index Scan for O(log M) lookup.
// Currently doing full table scan (O(M)).
```

The existing `TODO20260212.md` review claims "Zero TODOs/HACKs/FIXMEs" — this is a missed TODO that contradicts that claim. The TODO itself is valid — edge lookup is O(M) which destroys performance for graph traversal — but its existence contradicts the "clean" health claim.

**Fix:** Either implement the index scan, or convert the TODO to a documented known-limitation in the PRC docs.

---

## 📐 README & PRESENTATION (4 issues)

### README-01: Missing Shark Fin Logo and `catch.png` Image

The README has no project logo or visual identity. The `PRC/catch.png` (2076×920, 2.8 MB) is the "Software Freakonomics" comparison infographic — a strong visual for the benchmarks section but not a logo.

**Fix — Add simple unicode shark identity at top of README:**

```markdown
# 🦈 Sharc

**Read SQLite files 2x–40x faster than Microsoft.Data.Sqlite. Pure C#. Zero native dependencies.**
```

**Fix — Add `catch.png` as the benchmark comparison graphic:**

```markdown
## How It Works

<p align="center">
  <img src="PRC/catch.png" alt="Sharc vs Microsoft.Data.Sqlite" width="700" />
</p>
```

Note: At 2.8 MB, `catch.png` will slow GitHub README rendering. Compress to ~200 KB with: `pngquant --quality=65-80 catch.png` or convert to WebP. A 700px-wide display only needs ~1400px source width at 2x retina.

### README-02: "Zero GC Pressure" Headline Is Contradicted by Benchmark Data

As detailed in BENCH-01, the hero line "Zero GC pressure" is only true for isolated micro-benchmarks. In every realistic workload benchmark shown in the README itself, Sharc allocates 10x–50x more than SQLite.

**Suggested hero line:**

```markdown
**Read SQLite files 2x–40x faster than Microsoft.Data.Sqlite. Pure C#. Zero native dependencies.**
```

The zero-allocation story is genuinely impressive at the per-row primitive level — make that a section highlight rather than the headline claim.

### README-03: Missing "Limitations" Section

The README mentions limitations inline ("What Sharc does NOT do") but buries it in the Architecture section. For a project where the comparison table explicitly shows "No" for SQL, Writes, WAL, and Virtual Tables, a prominent Limitations section builds trust. Engineers respect honesty about scope.

**Suggested addition after the comparison table:**

```markdown
## Current Limitations

Sharc is a **read-only file format reader**, not a database engine. These are
deliberate scope boundaries, not missing features:

- **No SQL execution** — No VM, no VDBE, no query planner. Use Microsoft.Data.Sqlite for SQL.
- **No write support** — No INSERT, UPDATE, DELETE. The database file is never modified.
- **No WAL mode** — Throws `UnsupportedFeatureException` if WAL is detected at open time.
- **No virtual tables** — FTS5, R-Tree, and other extensions are not supported.
- **No WITHOUT ROWID** — Tables using `WITHOUT ROWID` are not readable.
- **Higher baseline memory** — Schema parsing allocates ~40 KB at open time. For
  sustained scans, Sharc uses more memory than SQLite (trades memory for speed).

For workloads that need any of the above, use Microsoft.Data.Sqlite alongside Sharc.
The two complement each other.
```

### README-04: Test Count Is Stale

README says "489 passed" but the TODO doc says "447 passed (393 unit + 54 integration)". With the addition of 42 Graph unit tests, the actual count should be 489 — but the TODO doc's "Architecture Health Summary" says 447. One of these numbers is wrong.

**Fix:** Run the actual test suite and update both documents with the real count.

---

## ✅ WHAT'S GENUINELY EXCELLENT

For balance — these are things I'd highlight as exemplary if reviewing this in a PR:

1. **`BTreeCursor` overflow handling** — The `ArrayPool` rental, cycle detection via `HashSet`, and inline-vs-overflow branching are textbook correct. The `ReturnAssembledPayload()` pattern in `Dispose()` and `MoveNext()` prevents pool leaks.

2. **`SharcDataReader` lazy decode with generation counter** — The `_decodedGeneration` / `_decodedGenerations[]` pattern is genuinely clever. It replaces O(N) `Array.Clear` with O(1) integer increment for invalidation. This is the kind of optimization that shows deep understanding.

3. **`FilePageSource` using `File.OpenHandle` + `RandomAccess`** — Most C# code uses `FileStream`. Using the raw handle with `RandomAccess.Read` eliminates Stream buffering overhead and async state machine cost. The stackalloc for the 100-byte header read is a nice touch.

4. **`CachedPageSource` LRU with `ArrayPool`** — Clean LRU implementation that rents from the shared pool and returns on eviction. Thread-safe via lock. No custom linked list — uses BCL `LinkedList<T>`.

5. **`MemoryMappedPageSource` safety boundary** — The single `unsafe` block is correctly confined to pointer acquisition, wrapped in `UnmanagedMemoryManager`, and everything downstream is safe spans. Good separation of concerns.

6. **`Directory.Build.props` centralization** — Proper use of `TreatWarningsAsErrors`, `Deterministic`, `IsAotCompatible`, `IsTrimmable`, conditional settings for src/test/bench. This is how professional .NET solutions should be structured.

7. **Exception hierarchy** — Four specific exception types (`InvalidDatabaseException`, `CorruptPageException`, `SharcCryptoException`, `UnsupportedFeatureException`) all deriving from `SharcException`. Each carries domain-specific context. `ThrowHelper` keeps throw sites out of hot paths for JIT inlining.

8. **`ISchemaAdapter` pattern** — The adapter abstraction cleanly separates Maker.AI brownfield schema from native greenfield schema. The `GenericSchemaAdapter` as a configurable fallback is a thoughtful API choice.

---

## Summary by Priority

| Priority | Count | Action |
|----------|------:|--------|
| 🔴 Correctness Bugs | 5 | Fix before any public release |
| 🟠 Architectural | 8 | Fix before "link quality" sharing |
| 🟡 Benchmark Honesty | 4 | Fix README claims to match data |
| 🔵 Code Quality | 6 | Fix for engineering credibility |
| 📐 Presentation | 4 | Fix README for first impressions |
| **Total** | **27** | |

The core Sharc reader is solid. The Graph layer needs the most work — it has correctness issues (BUG-01 alone returns wrong data), missing API surface (no `IContextGraph` implementation), and performance anti-patterns that contradict the project's stated philosophy. The README needs the 🦈 branding, honest memory tradeoff framing, and accurate scope claims.
