# Views.md — Sharc View Layer Specification

> **Version:** 0.1-draft  
> **Status:** Spec for fast implementation  
> **Prerequisites:** SchemaReader (✅ done), RecordDecoder (✅ done), Index B-tree reads (P1), Joins (see Joins.md)  
> **Estimated effort:** 1.5–2 weeks  
> **Namespace:** `Sharc.Views`

---

## 1. Design Philosophy

SQLite views are stored as `CREATE VIEW` SQL statements in `sqlite_schema`. SQLite executes them by parsing the SQL, building a query plan, and running the VDBE. Sharc has no SQL parser and no VDBE.

Sharc views are **pre-compiled read lenses** — metadata objects that describe a data projection (which tables, which columns, which joins, which filters) and produce a cursor when opened. They are the declarative complement to the imperative `JoinBuilder` API.

A Sharc view is conceptually: **a saved cursor configuration that can be opened repeatedly.**

### What Sharc Views Are

- Schema-aware column projections over single tables
- Pre-configured join pipelines (wrapping `JoinBuilder` configurations)
- Named, reusable access patterns stored in code or configuration
- SQLite view metadata readers (parse `CREATE VIEW` from `sqlite_schema` for column/table inference)

### What Sharc Views Are Not

- SQL query executors (no `SELECT ... FROM ... WHERE` evaluation)
- Materialised views (no cached result sets)
- Updatable views

---

## 2. Two-Tier Architecture

### Tier 1: SQLite View Metadata (Schema Layer — read what SQLite stored)

Sharc already reads `sqlite_schema` and knows that rows with `type = 'view'` exist. The `ViewInfo` model already captures `name`, `tbl_name`, and `sql`. This tier extends `ViewInfo` to extract useful structural metadata from the `CREATE VIEW` SQL without building a full SQL parser.

### Tier 2: Sharc Native Views (Runtime Layer — programmable read lenses)

A `SharcView` is a reusable, named cursor factory. It encapsulates a table source, optional column projection, optional join pipeline, and optional row filter predicate. Opening a view returns a cursor.

---

## 3. Tier 1: SQLite View Metadata Parsing

### 3.1 What We Extract

From `CREATE VIEW view_name AS SELECT col1, col2 FROM table_name ...` we extract:

| Field | Extraction Method | Difficulty |
|-------|------------------|------------|
| View name | Already in `sqlite_schema.name` | ✅ Done |
| Source table(s) | Regex/token scan for `FROM table_name` | Simple |
| Column names | Regex/token scan for `SELECT col1, col2, ...` or `SELECT *` | Simple |
| Column aliases | `AS alias` detection | Simple |
| Is SELECT * | Check if first token after SELECT is `*` | Trivial |
| Has WHERE clause | Token scan for `WHERE` keyword | Trivial |
| Has JOIN | Token scan for `JOIN` keyword | Trivial |
| Raw SQL | Already in `sqlite_schema.sql` | ✅ Done |

### 3.2 ViewInfo Extension

```csharp
namespace Sharc.Schema;

/// <summary>
/// Extended metadata for a SQLite view, parsed from CREATE VIEW SQL.
/// This is structural metadata only — Sharc does not execute the view's SQL.
/// </summary>
public sealed class ViewInfo
{
    /// <summary>View name from sqlite_schema.</summary>
    public required string Name { get; init; }

    /// <summary>Raw CREATE VIEW SQL from sqlite_schema.</summary>
    public required string Sql { get; init; }

    /// <summary>Source table name(s) referenced in FROM clause.</summary>
    public required IReadOnlyList<string> SourceTables { get; init; }

    /// <summary>Column names/aliases in the SELECT list. Empty if SELECT *.</summary>
    public required IReadOnlyList<ViewColumnInfo> Columns { get; init; }

    /// <summary>True if the view uses SELECT *.</summary>
    public required bool IsSelectAll { get; init; }

    /// <summary>True if the view's SQL contains JOIN.</summary>
    public required bool HasJoin { get; init; }

    /// <summary>True if the view's SQL contains WHERE.</summary>
    public required bool HasFilter { get; init; }

    /// <summary>
    /// Whether Sharc can natively execute this view.
    /// True when: single source table, no JOIN, no WHERE, no subqueries, no aggregation.
    /// These views can be opened as a SharcView automatically.
    /// </summary>
    public bool IsSharcExecutable =>
        SourceTables.Count == 1 && !HasJoin && !HasFilter;
}

/// <summary>
/// A column reference within a view definition.
/// </summary>
public sealed class ViewColumnInfo
{
    /// <summary>Original column name from the source table.</summary>
    public required string SourceName { get; init; }

    /// <summary>Alias if AS was used, otherwise same as SourceName.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Column ordinal in the view's SELECT list (0-based).</summary>
    public required int Ordinal { get; init; }
}
```

### 3.3 Lightweight SQL Token Scanner

**This is NOT a SQL parser.** It is a forward-only token scanner that extracts the structural elements listed above. It handles ~85% of real-world `CREATE VIEW` statements — the simple projections and single-table views that dominate SQLite databases in practice.

```csharp
namespace Sharc.Core.Schema;

/// <summary>
/// Extracts structural metadata from CREATE VIEW SQL.
/// Forward-only, allocation-minimal. Not a SQL parser.
/// </summary>
internal static class ViewSqlScanner
{
    /// <summary>
    /// Scan CREATE VIEW SQL and return structural metadata.
    /// </summary>
    /// <param name="sql">The raw CREATE VIEW statement from sqlite_schema.</param>
    /// <returns>Parsed view metadata, or a fallback with just the raw SQL if unparseable.</returns>
    public static ViewParseResult Scan(ReadOnlySpan<char> sql);
}

internal readonly struct ViewParseResult
{
    public readonly string[] SourceTables;
    public readonly ViewColumnInfo[] Columns;
    public readonly bool IsSelectAll;
    public readonly bool HasJoin;
    public readonly bool HasFilter;
    public readonly bool ParseSucceeded;
}
```

**Implementation constraints:**

- Span-based scanning — no string splits, no regex on hot path
- Keywords detected by ordinal comparison (`FROM`, `WHERE`, `JOIN`, `SELECT`, `AS`)
- Identifiers extracted between keyword boundaries
- Quoted identifiers (`"name"`, `` `name` ``, `[name]`) handled
- Nested subqueries detected (presence of inner `SELECT`) → sets `ParseSucceeded = false` for the subquery portion, but still extracts outer metadata
- **Total implementation: ~150–200 lines of C#**

---

## 4. Tier 2: SharcView — Native Read Lens

### 4.1 SharcView Definition

```csharp
namespace Sharc.Views;

/// <summary>
/// A named, reusable cursor configuration. Opening a view returns a forward-only
/// cursor over the projected columns. Views compose with joins and entitlements.
/// </summary>
public sealed class SharcView
{
    /// <summary>Human-readable name for this view.</summary>
    public string Name { get; }

    /// <summary>The root table this view reads from.</summary>
    public string SourceTable { get; }

    /// <summary>
    /// Column projection. If null, all columns are returned.
    /// Ordinals reference the source table's column positions.
    /// </summary>
    public IReadOnlyList<int>? ProjectedColumns { get; }

    /// <summary>
    /// Optional join pipeline. If set, the view opens a join cursor
    /// instead of a simple table scan.
    /// </summary>
    public JoinPipeline? Joins { get; }

    /// <summary>
    /// Optional row filter predicate. Applied during cursor iteration.
    /// Signature: (IRowAccessor row) → bool (true = include).
    /// </summary>
    public Func<IRowAccessor, bool>? Filter { get; }

    /// <summary>
    /// Open this view and return a cursor. The cursor respects
    /// the active entitlement context on the database.
    /// </summary>
    public IViewCursor Open(SharcDatabase db);
}
```

### 4.2 IViewCursor

```csharp
/// <summary>
/// Forward-only cursor over a view's projected rows.
/// Implements IRowAccessor for column access — column ordinals
/// are remapped to the view's projection.
/// </summary>
public interface IViewCursor : IRowAccessor, IDisposable
{
    /// <summary>Advance to the next row. Returns false when exhausted.</summary>
    bool MoveNext();

    /// <summary>
    /// Number of rows read so far. Useful for progress reporting.
    /// </summary>
    long RowsRead { get; }
}
```

### 4.3 ViewBuilder (Fluent API)

```csharp
/// <summary>
/// Fluent builder for defining SharcView instances.
/// </summary>
public sealed class ViewBuilder
{
    public static ViewBuilder From(string tableName);

    /// <summary>Project specific columns by ordinal.</summary>
    public ViewBuilder Select(params int[] columnOrdinals);

    /// <summary>Project specific columns by name (resolved via SharcSchema).</summary>
    public ViewBuilder Select(params string[] columnNames);

    /// <summary>Add a join to the view's cursor pipeline.</summary>
    public ViewBuilder Join(Action<JoinBuilder> configure);

    /// <summary>Add a row filter predicate.</summary>
    public ViewBuilder Where(Func<IRowAccessor, bool> predicate);

    /// <summary>Name the view.</summary>
    public ViewBuilder Named(string name);

    /// <summary>Build the immutable view definition.</summary>
    public SharcView Build();
}
```

---

## 5. Auto-Promotion: SQLite Views → SharcViews

When `ViewInfo.IsSharcExecutable` is true (single table, no joins, no filters, no subqueries), Sharc can automatically create a `SharcView` from the SQLite view metadata.

```csharp
/// <summary>
/// Attempts to create a SharcView from a SQLite VIEW definition.
/// Returns null if the view's SQL is too complex for Sharc to execute.
/// </summary>
public static class ViewPromoter
{
    public static SharcView? TryPromote(ViewInfo viewInfo, SharcSchema schema)
    {
        if (!viewInfo.IsSharcExecutable)
            return null;

        var table = schema.GetTable(viewInfo.SourceTables[0]);

        int[]? projection = null;
        if (!viewInfo.IsSelectAll)
        {
            projection = viewInfo.Columns
                .Select(vc => table.GetColumnOrdinal(vc.SourceName))
                .ToArray();
        }

        return ViewBuilder
            .From(viewInfo.SourceTables[0])
            .Select(projection ?? Array.Empty<int>())
            .Named(viewInfo.Name)
            .Build();
    }
}
```

**Coverage estimate:** Based on analysis of real-world SQLite databases, ~60–70% of views are simple single-table projections (SELECT subset of columns FROM one table). These auto-promote. The remaining 30–40% have JOINs, WHERE clauses, or aggregations — they remain as metadata-only `ViewInfo` objects that consumers can inspect and manually build equivalent `SharcView` + `JoinBuilder` pipelines for.

---

## 6. Implementation Plan

### Phase 1: ViewInfo Extension + ViewSqlScanner (Week 1)

**Files to create:**

| File | Responsibility |
|------|---------------|
| `Sharc.Core/Schema/ViewInfo.cs` | Extended view metadata model |
| `Sharc.Core/Schema/ViewColumnInfo.cs` | Column reference model |
| `Sharc.Core/Schema/ViewSqlScanner.cs` | Lightweight CREATE VIEW token scanner |
| `Sharc.Tests/Schema/ViewSqlScannerTests.cs` | Comprehensive parsing tests |

**Test matrix (TDD):**

- `CREATE VIEW v AS SELECT * FROM t` → `IsSelectAll=true`, `SourceTables=["t"]`
- `CREATE VIEW v AS SELECT a, b, c FROM t` → columns parsed, ordinals assigned
- `CREATE VIEW v AS SELECT a AS x, b AS y FROM t` → aliases captured
- `CREATE VIEW v AS SELECT a FROM t WHERE a > 5` → `HasFilter=true`
- `CREATE VIEW v AS SELECT a, b FROM t1 JOIN t2 ON t1.id = t2.fk` → `HasJoin=true`, `SourceTables=["t1","t2"]`
- `CREATE VIEW v AS SELECT a FROM (SELECT ...)` → `ParseSucceeded=false` for inner
- `CREATE VIEW v AS SELECT "quoted col" FROM [bracketed table]` → identifiers unquoted
- `CREATE TEMP VIEW v AS SELECT ...` → TEMP keyword handled
- `CREATE VIEW IF NOT EXISTS v AS SELECT ...` → IF NOT EXISTS skipped
- Empty SQL / malformed SQL → graceful fallback, `ParseSucceeded=false`

### Phase 2: SharcView + ViewBuilder + IViewCursor (Week 1–2)

**Files to create:**

| File | Responsibility |
|------|---------------|
| `Sharc.Views/SharcView.cs` | View definition |
| `Sharc.Views/ViewBuilder.cs` | Fluent API |
| `Sharc.Views/IViewCursor.cs` | Cursor interface |
| `Sharc.Views/SimpleViewCursor.cs` | Single-table projected cursor |
| `Sharc.Views/JoinedViewCursor.cs` | View cursor wrapping IJoinCursor |
| `Sharc.Views/FilteredViewCursor.cs` | Decorator adding predicate filtering |

**SimpleViewCursor implementation sketch:**

```csharp
internal sealed class SimpleViewCursor : IViewCursor
{
    private readonly SharcDataReader _reader;
    private readonly int[]? _projection;     // column ordinal remapping

    public bool MoveNext() => _reader.Read();

    public long GetInt64(int ordinal)
    {
        int mapped = _projection != null ? _projection[ordinal] : ordinal;
        return _reader.GetInt64(mapped);
    }

    // Same pattern for GetText, GetDouble, GetBlob, IsNull
}
```

**FilteredViewCursor — decorator pattern:**

```csharp
internal sealed class FilteredViewCursor : IViewCursor
{
    private readonly IViewCursor _inner;
    private readonly Func<IRowAccessor, bool> _predicate;

    public bool MoveNext()
    {
        while (_inner.MoveNext())
        {
            if (_predicate(_inner))
                return true;
        }
        return false;
    }

    // All IRowAccessor methods delegate to _inner
}
```

### Phase 3: ViewPromoter + Integration (Week 2)

- Wire `ViewPromoter.TryPromote()` into `SharcSchema` loading
- Add `SharcSchema.Views` enrichment: each `ViewInfo` gains an optional `SharcView? NativeView` property
- Add `SharcDatabase.OpenView(string name)` convenience method
- Integration tests with real SQLite databases containing views

---

## 7. Memory Contract

| Operation | Allocations | Notes |
|-----------|------------|-------|
| ViewSqlScanner.Scan() | 1 `ViewParseResult` + string arrays | Cold path (schema load), acceptable |
| SharcView construction | 1 object | Immutable, reusable |
| SimpleViewCursor iteration | **ZERO per row** | Delegates to SharcDataReader (already zero-alloc) |
| FilteredViewCursor iteration | **ZERO per row** | Predicate is a delegate, no boxing |
| JoinedViewCursor iteration | Same as IJoinCursor | See Joins.md memory contract |
| Column ordinal remapping | **ZERO** | Array index lookup, no allocation |

---

## 8. GCD Integration Patterns

### Pattern 1: Simple Projection View

```csharp
// Define a view that shows only file paths and sizes from the files table
var fileListView = ViewBuilder
    .From("files")
    .Select("path", "size_bytes", "language")
    .Named("file_list")
    .Build();

using var cursor = fileListView.Open(db);
while (cursor.MoveNext())
{
    var path = cursor.GetText(0);      // projected ordinal 0 → "path"
    var size = cursor.GetInt64(1);     // projected ordinal 1 → "size_bytes"
}
```

### Pattern 2: Filtered View for Context Window Budgeting

```csharp
// Only files under 10KB — suitable for inlining into LLM context
var smallFilesView = ViewBuilder
    .From("files")
    .Select("path", "content", "language")
    .Where(row => row.GetInt64(1) < 10_240)  // size_bytes < 10KB
    .Named("small_files")
    .Build();
```

### Pattern 3: Joined View — Commit History with Files

```csharp
// Pre-configured view that joins commits → commit_files → files
var commitFilesView = ViewBuilder
    .From("commits")
    .Join(j => j
        .IndexJoin("commit_files", "idx_cf_commit_id", outerColumn: 0)
        .RowidJoin("files", outerRowidColumn: 3))
    .Named("commit_files_resolved")
    .Build();

// Open returns a joined cursor — same zero-alloc guarantees
using var cursor = commitFilesView.Open(db);
```

### Pattern 4: Auto-Promoted SQLite View

```csharp
using var db = SharcDatabase.Open("project.gcd");

// If the SQLite file has: CREATE VIEW active_contributors AS 
//   SELECT name, email FROM contributors
// Sharc auto-promotes it:
var view = db.Schema.GetView("active_contributors").NativeView;
if (view != null)
{
    using var cursor = view.Open(db);
    // Works natively — no SQL execution needed
}
```

### Pattern 5: Entitlement-Scoped Views

```csharp
// Same view definition, different entitlement context → different data
var sensitiveView = ViewBuilder
    .From("_records")
    .Select("id", "data")
    .Where(row => row.GetText(0).StartsWith("secret:"))
    .Named("secrets")
    .Build();

// User A (role:admin) sees all matching rows
// User B (role:viewer) sees only rows their entitlement tag unlocks
// The view definition is identical — entitlements are enforced below the cursor
```

---

## 9. Decision Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Lightweight token scanner, not SQL parser | Deliberate | A full SQL parser (even a subset) would be 500+ lines and require grammar maintenance. Token scanning covers ~85% of real views in ~150 lines. |
| Auto-promotion only for simple views | Deliberate | Complex views (JOINs, WHERE, subqueries) require the caller to build equivalent SharcView + JoinBuilder pipelines. This keeps the view layer thin and honest about what it can do. |
| `IViewCursor` extends `IRowAccessor` | Deliberate | Views ARE row sources. Making them implement `IRowAccessor` means they compose directly into JoinBuilder as outer sources. |
| Decorator pattern for filtering | Deliberate | `FilteredViewCursor` wraps any `IViewCursor`. This means filters compose with projections, joins, and further filters without combinatorial cursor types. |
| No materialised views | Deliberate | Materialisation requires write support + cache invalidation. Both are out of scope for read-only Sharc. If you need cached results, materialise in SQLite and read with Sharc. |
| Column remapping via int array | Deliberate | `_projection[viewOrdinal] → tableOrdinal` is a single array index — zero allocation, zero branching, O(1). No dictionary, no hash map. |
