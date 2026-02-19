# Views.md — Sharc View Layer

> **Version:** 1.0
> **Status:** Implemented and tested (65+ dedicated tests)
> **Namespace:** `Sharc.Views`

---

## 1. Overview

Sharc views are **pre-compiled read lenses** — named, reusable, immutable cursor configurations that project, filter, and compose data from tables or other views. Opening a view returns a zero-allocation forward-only cursor.

A `SharcView` is conceptually: **a saved cursor configuration that can be opened repeatedly.**

Views in Sharc have three tiers of capability:

| Tier | Feature | API |
|------|---------|-----|
| **Cursor** | Open a view, iterate rows | `view.Open(db)` → `IViewCursor` |
| **Registration** | Register views by name, open by name | `db.RegisterView(view)` / `db.OpenView("name")` |
| **SQL Integration** | Query registered views with SQL | `db.Query("SELECT ... FROM view_name")` |

---

## 2. Defining Views

### 2.1 ViewBuilder Fluent API

All views are constructed through `ViewBuilder`:

```csharp
// Simple projection
var view = ViewBuilder
    .From("users")
    .Select("name", "age")
    .Named("user_basics")
    .Build();

// With row filter
var seniors = ViewBuilder
    .From("users")
    .Select("name", "age", "dept")
    .Where(row => row.GetInt64(1) >= 30)
    .Named("seniors")
    .Build();

// SELECT * (all columns)
var allUsers = ViewBuilder
    .From("users")
    .Named("all_users")
    .Build();
```

### 2.2 Subviews — Views on Views

A view can source from another view. There is no distinction between a "view" and a "subview" — a subview IS a `SharcView` with the same type and the same features. The only difference is the source: a table name vs. a parent `SharcView`.

```csharp
// Parent view: 3 columns from users
var parent = ViewBuilder
    .From("users")
    .Select("name", "age", "dept")
    .Named("v_parent")
    .Build();

// Subview: narrow to 2 columns, add filter
var sub = ViewBuilder
    .From(parent)
    .Select("name", "age")
    .Where(row => row.GetInt64(1) >= 30)
    .Named("v_seniors")
    .Build();

// Deep chain: view → view → view
var leaf = ViewBuilder
    .From(sub)
    .Select("name")
    .Named("v_senior_names")
    .Build();
```

Subview cursors compose by wrapping the parent cursor with `ProjectedViewCursor` (ordinal remapping) and optionally `FilteredViewCursor` (predicate filtering). Each level is zero-allocation per row.

### 2.3 SharcView Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | View name (used for registration and SQL queries) |
| `SourceTable` | `string?` | Table name, or null if sourced from a view |
| `SourceView` | `SharcView?` | Parent view, or null if sourced from a table |
| `ProjectedColumnNames` | `IReadOnlyList<string>?` | Column projection, or null for all columns |
| `Filter` | `Func<IRowAccessor, bool>?` | Row predicate, or null for no filtering |

Exactly one of `SourceTable` and `SourceView` is non-null.

---

## 3. Opening Views

### 3.1 Direct Open

```csharp
using var cursor = view.Open(db);
while (cursor.MoveNext())
{
    string name = cursor.GetString(0);
    long age = cursor.GetInt64(1);
}
```

### 3.2 Open by Name (Registration)

Register a view by name, then open it:

```csharp
using var db = SharcDatabase.Open("mydata.db");

var view = ViewBuilder
    .From("users")
    .Select("name", "age")
    .Where(row => row.GetInt64(1) >= 18)
    .Named("adults")
    .Build();

db.RegisterView(view);

// Later...
using var cursor = db.OpenView("adults");
while (cursor.MoveNext())
{
    Console.WriteLine(cursor.GetString(0));
}
```

`OpenView` lookup order:
1. Registered programmatic views (added via `RegisterView`)
2. SQLite schema views (auto-promoted from `CREATE VIEW` statements)

Registered views take priority — they override SQLite views with the same name.

### 3.3 Registration API

```csharp
// Register (last registration wins for duplicate names)
db.RegisterView(view);

// Unregister
bool removed = db.UnregisterView("adults"); // true if found and removed

// OpenView throws KeyNotFoundException if the name isn't registered
// and isn't an auto-promotable SQLite view
```

---

## 4. SQL Queries on Views

Registered views participate in the Sharc SQL query pipeline. Use `db.Query()` to query them with standard SQL:

```csharp
db.RegisterView(view);

// SELECT specific columns
using var r1 = db.Query("SELECT name FROM adults");

// WHERE filtering on top of view's built-in filter
using var r2 = db.Query("SELECT name FROM adults WHERE age > 30");

// ORDER BY + LIMIT
using var r3 = db.Query("SELECT name FROM adults ORDER BY age DESC LIMIT 10");

// COUNT
using var r4 = db.Query("SELECT COUNT(*) FROM adults");

// JOIN with other tables
using var r5 = db.Query(
    "SELECT adults.name, orders.amount " +
    "FROM adults JOIN orders ON adults.id = orders.user_id");
```

### 4.1 Resolution Mechanism

When `db.Query()` encounters a table name that matches a registered view:

1. **Filter-free views** (no `Where` predicate in the view chain) — converted to a Cote (Common Table Expression) with synthetic SQL targeting the root table.

2. **Filtered views** (any `Where` predicate in the chain) — pre-materialized by opening the view cursor and collecting rows into `QueryValue[][]`. The pre-materialized data overrides the Cote, preserving the Func filter semantics that cannot be expressed as SQL.

In both cases, the materialization path is used (not lazy Cote resolution), ensuring correct behavior for ORDER BY, LIMIT, JOIN, and qualified column references.

### 4.2 Composed Filters

When a view has a programmatic filter and the SQL query adds a WHERE clause, both filters apply:

```csharp
// View filter: dept == "eng"
var engView = ViewBuilder
    .From("users")
    .Select("name", "age", "dept")
    .Where(row => row.GetString(2) == "eng")
    .Named("v_eng")
    .Build();
db.RegisterView(engView);

// SQL adds: age > 28
// Result: rows where dept=="eng" AND age>28
using var reader = db.Query("SELECT name FROM v_eng WHERE age > 28");
```

---

## 5. SQLite View Auto-Promotion

Sharc automatically converts simple `CREATE VIEW` statements from `sqlite_schema` into `SharcView` instances via `ViewPromoter`. Promoted views are accessible through `OpenView()` without manual registration.

A SQLite view is promotable when:
- Single source table (no JOIN)
- No WHERE clause
- No subqueries or aggregations
- Column list is parseable

```csharp
// If the SQLite file has:
//   CREATE VIEW v_user_emails AS SELECT name, email FROM users
// Then:
using var cursor = db.OpenView("v_user_emails");
// Works automatically — no registration needed
```

### 5.1 ViewSqlScanner

The `ViewSqlScanner` extracts structural metadata from `CREATE VIEW` SQL using a lightweight forward-only token scanner (not a full SQL parser). It identifies source tables, column names/aliases, and presence of JOIN/WHERE/subqueries.

Enriched metadata is available on `ViewInfo` objects in `db.Schema.Views`.

---

## 6. Cursor Architecture

### 6.1 Cursor Types

| Cursor | Wraps | Purpose |
|--------|-------|---------|
| `SimpleViewCursor` | `SharcDataReader` | Table → view (ordinal remapping via `int[]`) |
| `ProjectedViewCursor` | `IViewCursor` | View → subview (ordinal remapping via `int[]`) |
| `FilteredViewCursor` | `IViewCursor` | Decorator adding `Func<IRowAccessor, bool>` predicate |

### 6.2 IViewCursor Interface

```csharp
public interface IViewCursor : IRowAccessor, IDisposable
{
    bool MoveNext();
    long RowsRead { get; }
}

public interface IRowAccessor
{
    int FieldCount { get; }
    long GetInt64(int ordinal);
    double GetDouble(int ordinal);
    string GetString(int ordinal);
    byte[] GetBlob(int ordinal);
    bool IsNull(int ordinal);
    string GetColumnName(int ordinal);
    SharcColumnType GetColumnType(int ordinal);
}
```

### 6.3 Cursor Chain Example

For a 3-deep subview with filters at each level:

```
SharcDataReader (B-tree scan)
  └─ SimpleViewCursor (table → Level 1 projection)
       └─ FilteredViewCursor (Level 1 filter)
            └─ ProjectedViewCursor (Level 1 → Level 2 projection)
                 └─ FilteredViewCursor (Level 2 filter)
                      └─ ProjectedViewCursor (Level 2 → Level 3 projection)
                           └─ FilteredViewCursor (Level 3 filter)
```

Each layer is O(1) per row — a single array index lookup for projection, a delegate call for filtering.

---

## 7. Memory Contract

| Operation | Allocations | Notes |
|-----------|------------|-------|
| `SharcView` construction | 1 object | Immutable, reusable across opens |
| `ViewBuilder.Build()` | 1 `SharcView` + optional `string[]` | Cold path |
| `view.Open(db)` | Cursor objects + `int[]` projection | Cold path per open |
| `cursor.MoveNext()` | **ZERO per row** | All cursor types |
| `cursor.Get*(ordinal)` | **ZERO per row** | Array index → delegate to inner |
| `ProjectedViewCursor` remapping | **ZERO per row** | `_projection[ordinal]` — one array index |
| `FilteredViewCursor` predicate | **ZERO per row** | Delegate call, no boxing |
| SQL query materialization | `QueryValue[]` per row | Required for filtered views in SQL queries |

---

## 8. File Map

| File | Purpose |
|------|---------|
| `src/Sharc/Views/SharcView.cs` | View definition, `Open()` logic |
| `src/Sharc/Views/ViewBuilder.cs` | Fluent construction API |
| `src/Sharc/Views/IViewCursor.cs` | Cursor interface |
| `src/Sharc/Views/IRowAccessor.cs` | Typed column access interface |
| `src/Sharc/Views/SimpleViewCursor.cs` | Table → view cursor |
| `src/Sharc/Views/ProjectedViewCursor.cs` | View → subview cursor |
| `src/Sharc/Views/FilteredViewCursor.cs` | Filter decorator |
| `src/Sharc/Views/ViewPromoter.cs` | SQLite view → SharcView promotion |
| `src/Sharc/SharcDatabase.cs` | `RegisterView`, `UnregisterView`, `OpenView`, SQL integration |
| `src/Sharc/Query/Sharq/ViewSqlScanner.cs` | CREATE VIEW SQL metadata extraction |

---

## 9. Test Coverage

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `Sharc.Tests/Views/ViewBuilderTests.cs` | ~18 | Builder API, validation, edge cases |
| `Sharc.Tests/Views/SubviewTests.cs` | ~15 | View-on-view composition, deep chains, filters |
| `Sharc.Tests/Views/ViewRegistrationTests.cs` | 8 | Register/unregister, OpenView, priority |
| `Sharc.Tests/Views/ViewCursorTests.cs` | ~10 | SimpleViewCursor, FilteredViewCursor, ProjectedViewCursor |
| `Sharc.IntegrationTests/ViewQueryIntegrationTests.cs` | 11 | SQL queries on registered views with real DB |
| `Sharc.IntegrationTests/ViewIntegrationTests.cs` | ~10 | End-to-end with SQLite databases |

---

## 10. Decision Log

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Lightweight token scanner, not SQL parser | `ViewSqlScanner` | A full SQL parser would be 500+ lines. Token scanning covers ~85% of real views in ~150 lines. |
| Auto-promotion only for simple views | `ViewPromoter` | Complex views (JOINs, WHERE) require the caller to build equivalent SharcView pipelines. Keeps the view layer honest about what it can do. |
| `IViewCursor` extends `IRowAccessor` | Composition | Views ARE row sources. They compose into any pipeline expecting `IRowAccessor`. |
| Decorator pattern for filtering | `FilteredViewCursor` | Wraps any `IViewCursor`. Filters compose with projections and further filters without combinatorial cursor types. |
| Column remapping via `int[]` | Zero-alloc | `_projection[viewOrdinal] → parentOrdinal` is one array index — O(1), no dictionary, no hash. |
| No distinction between view and subview | Uniform type | A subview IS a `SharcView`. Same constructor, same features, same `Open()` method. The only difference is `SourceView != null` instead of `SourceTable != null`. |
| `GetColumnType(int)` on `IRowAccessor` | Typed discriminant | Avoids `object? GetValue()` (type erasure). Named enum `SharcColumnType` gives callers a proper typed switch over `{Integral, Real, Text, Blob, Null}`. |
| Force materialization path for registered views | Correctness | The lazy Cote resolution path doesn't handle ORDER BY on non-projected columns, qualified column references in JOINs, or filtered views. The materialization fallback handles all cases correctly. |
| Registered views override SQLite views | Explicit wins | When a programmatic view has the same name as a SQLite view, the registered view takes priority. User intent is explicit and should win over implicit schema views. |
| Pre-materialize filtered views | Func can't be SQL | `Func<IRowAccessor, bool>` predicates can't be expressed as SQL. The view cursor is opened, rows are collected into `QueryValue[][]`, and injected into the query pipeline as pre-materialized Cote data. |
