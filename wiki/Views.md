# Views

Sharc provides a full **programmatic view system** — named, reusable, composable read lenses over tables. Views support column projection, row filtering, arbitrary nesting (subviews), SQLite `CREATE VIEW` auto-promotion, and full SQL query integration.

---

## Table of Contents

- [Creating a View](#creating-a-view)
- [Reading from a View](#reading-from-a-view)
- [Column Projection](#column-projection)
- [Row Filtering](#row-filtering)
- [SubViews (Views on Views)](#subviews-views-on-views)
- [Deep SubView Chains](#deep-subview-chains)
- [View Registration & Management](#view-registration--management)
- [SQLite CREATE VIEW (Auto-Promotion)](#sqlite-create-view-auto-promotion)
- [SQL Queries on Views](#sql-queries-on-views)
- [Composing Filters: Programmatic + SQL](#composing-filters-programmatic--sql)
- [Views in JOINs and UNION](#views-in-joins-and-union)
- [View Priority & Override Rules](#view-priority--override-rules)
- [IViewCursor API Reference](#iviewcursor-api-reference)
- [IRowAccessor API Reference](#irowaccessor-api-reference)
- [Cursor Architecture (Internals)](#cursor-architecture-internals)
- [Memory & Performance](#memory--performance)
- [Safety: Circular References & Depth Limits](#safety-circular-references--depth-limits)
- [Common Patterns](#common-patterns)
- [Limitations](#limitations)

---

## Creating a View

Use `ViewBuilder` with a fluent API. A view is an immutable, reusable definition — construct once, open many times.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Minimal: all columns, no filter
var allUsers = ViewBuilder
    .From("users")
    .Named("all_users")
    .Build();

// With projection and filter
var adults = ViewBuilder
    .From("users")
    .Select("id", "name", "email")
    .Where(row => row.GetInt64(0) > 18)  // ordinals are post-projection
    .Named("adults")
    .Build();

db.RegisterView(adults);
```

### ViewBuilder API

| Method | Description |
|--------|-------------|
| `From(string tableName)` | Source from a database table |
| `From(SharcView parentView)` | Source from another view (creates a subview) |
| `Select(params string[] columns)` | Project specific columns by name |
| `Where(Func<IRowAccessor, bool> predicate)` | Filter rows with a predicate |
| `Named(string name)` | Set the view's name |
| `Build()` | Create the immutable `SharcView` |

---

## Reading from a View

Open a view to get a forward-only `IViewCursor`. The cursor applies projection and filtering automatically.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var view = ViewBuilder
    .From("users")
    .Select("name", "age")
    .Where(row => row.GetInt64(1) >= 21)
    .Named("drinking_age")
    .Build();

db.RegisterView(view);

// Open via registration name
using var cursor = db.OpenView("drinking_age");
while (cursor.MoveNext())
{
    string name = cursor.GetString(0);  // "name" → ordinal 0
    long age = cursor.GetInt64(1);      // "age"  → ordinal 1
    Console.WriteLine($"{name}, age {age}");
}
Console.WriteLine($"Total rows: {cursor.RowsRead}");
```

You can also open a view directly without registration:

```csharp
using var cursor = view.Open(db);
while (cursor.MoveNext())
    Console.WriteLine(cursor.GetString(0));
```

---

## Column Projection

`Select()` narrows the visible columns. Ordinals in the cursor refer to the **projected** column positions, not the underlying table positions.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Table has: id, name, age, dept, email, salary (6 columns)
// View exposes: name, dept, salary (3 columns)
var view = ViewBuilder
    .From("employees")
    .Select("name", "dept", "salary")
    .Named("employee_summary")
    .Build();

using var cursor = view.Open(db);
while (cursor.MoveNext())
{
    string name = cursor.GetString(0);    // "name"   → ordinal 0
    string dept = cursor.GetString(1);    // "dept"   → ordinal 1
    long salary = cursor.GetInt64(2);     // "salary" → ordinal 2
    // cursor.FieldCount == 3
}
```

Omitting `Select()` returns all columns from the source.

---

## Row Filtering

`Where()` takes a `Func<IRowAccessor, bool>` predicate. Ordinals in the predicate match the **projected** columns.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var highEarners = ViewBuilder
    .From("employees")
    .Select("name", "dept", "salary")
    .Where(row => row.GetInt64(2) > 100_000)  // salary > 100K
    .Named("high_earners")
    .Build();

using var cursor = highEarners.Open(db);
while (cursor.MoveNext())
    Console.WriteLine($"{cursor.GetString(0)} ({cursor.GetString(1)}): ${cursor.GetInt64(2):N0}");

// cursor.RowsRead counts only rows that passed the filter
```

### NULL-safe Filtering

Always check `IsNull()` when columns may contain NULL values:

```csharp
var nonNull = ViewBuilder
    .From("employees")
    .Select("name", "email")
    .Where(row => !row.IsNull(1))  // skip rows with NULL email
    .Named("has_email")
    .Build();
```

---

## SubViews (Views on Views)

A **subview** sources from another `SharcView` instead of a table. Use `ViewBuilder.From(SharcView)` to compose views. Each level can add its own projection and filter.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Level 1: Base view over the table — select 5 columns
var allEmployees = ViewBuilder
    .From("employees")
    .Select("name", "age", "dept", "email", "salary")
    .Named("all_employees")
    .Build();

// Level 2: SubView — narrow to engineering, keep 3 columns
var engineers = ViewBuilder
    .From(allEmployees)                        // <-- sources from a VIEW, not a table
    .Select("name", "age", "salary")
    .Where(row => row.GetString(0) != null)    // ordinals are post-projection
    .Named("engineers")
    .Build();

// Level 3: SubView of SubView — only names
var engineerNames = ViewBuilder
    .From(engineers)                           // <-- sources from engineers view
    .Select("name")
    .Named("engineer_names")
    .Build();

// Open the deepest subview — filters from ALL levels are applied
using var cursor = engineerNames.Open(db);
while (cursor.MoveNext())
    Console.WriteLine(cursor.GetString(0));    // only "name" column, FieldCount == 1
```

### How SubViews Work Internally

When you open a subview, Sharc builds a **cursor chain** using the decorator pattern:

```text
SharcDataReader           (B-tree scan over "employees")
  └─ SimpleViewCursor     (table → Level 1 projection: 5 columns)
       └─ ProjectedViewCursor  (Level 1 → Level 2 projection: 3 columns)
            └─ FilteredViewCursor  (Level 2 filter applied)
                 └─ ProjectedViewCursor  (Level 2 → Level 3 projection: 1 column)
```

Each level adds at most two lightweight decorators (projection + optional filter). All column remapping is via `int[]` array lookups — **zero allocation per row**.

---

## Deep SubView Chains

You can nest subviews up to **10 levels deep**. Each level progressively narrows the data:

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Level 1: All users, all columns
var level1 = ViewBuilder.From("users")
    .Named("all_users").Build();

// Level 2: Active users only
var level2 = ViewBuilder.From(level1)
    .Where(row => row.GetInt64(3) == 1)  // active == 1
    .Named("active_users").Build();

// Level 3: Active adults (age >= 18)
var level3 = ViewBuilder.From(level2)
    .Where(row => row.GetInt64(2) >= 18)  // age >= 18
    .Named("active_adults").Build();

// Level 4: Just names and emails of active adults
var level4 = ViewBuilder.From(level3)
    .Select("name", "email")
    .Named("active_adult_contacts").Build();

// Opening level4 applies ALL filters from levels 2 + 3, then projects to 2 columns
using var cursor = level4.Open(db);
while (cursor.MoveNext())
    Console.WriteLine($"{cursor.GetString(0)}: {cursor.GetString(1)}");
```

**Key principle:** Filters at every level in the chain are composed — a row must pass **all** ancestor filters to be returned. Projections narrow progressively — each level can only see columns exposed by its parent.

---

## View Registration & Management

Register views on a `SharcDatabase` to enable `OpenView()` by name and SQL query integration.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var view = ViewBuilder.From("users")
    .Select("name", "email")
    .Named("user_contacts")
    .Build();

// Register
db.RegisterView(view);

// Open by name
using var cursor = db.OpenView("user_contacts");

// List all registered view names
IReadOnlyCollection<string> names = db.ListRegisteredViews();
foreach (string name in names)
    Console.WriteLine($"Registered: {name}");

// Unregister
bool removed = db.UnregisterView("user_contacts");
```

### Registration Rules

| Behavior | Detail |
|----------|--------|
| **Duplicate names** | Last registration wins (silent overwrite) |
| **Case sensitivity** | View names are case-insensitive |
| **SubViews** | Can be registered — the entire chain opens when the subview is opened |
| **Query cache** | Registering/unregistering invalidates cached query plans automatically |

---

## SQLite CREATE VIEW (Auto-Promotion)

Sharc reads `CREATE VIEW` statements from the SQLite `sqlite_schema` table and attempts to **auto-promote** them to executable `SharcView` instances.

### Schema Inspection

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");

// List all SQLite-defined views
foreach (var viewInfo in db.Schema.Views)
{
    Console.WriteLine($"View: {viewInfo.Name}");
    Console.WriteLine($"  SQL: {viewInfo.Sql}");
    Console.WriteLine($"  Source tables: {string.Join(", ", viewInfo.SourceTables)}");
    Console.WriteLine($"  Has JOIN: {viewInfo.HasJoin}");
    Console.WriteLine($"  Has WHERE: {viewInfo.HasFilter}");
    Console.WriteLine($"  Promotable: {viewInfo.IsSharcExecutable}");

    foreach (var col in viewInfo.Columns)
        Console.WriteLine($"  Column: {col.SourceName} AS {col.DisplayName}");
}

// Lookup by name
var info = db.Schema.GetView("v_user_emails");
```

### Auto-Promotion Criteria

A SQLite view is **auto-promotable** when ALL of these are true:

| Criterion | Example that passes | Example that fails |
|-----------|--------------------|--------------------|
| Single source table | `SELECT * FROM users` | `SELECT * FROM users, orders` |
| No JOIN | `SELECT name FROM users` | `SELECT * FROM users JOIN orders ON ...` |
| No WHERE clause | `SELECT name, email FROM users` | `SELECT name FROM users WHERE active = 1` |
| Parse succeeded | Standard SQL syntax | Unsupported SQL features |
| Source table exists | Table `users` in schema | Table `deleted_table` missing |

This covers ~60-70% of real-world `CREATE VIEW` definitions.

### Opening SQLite Views

Auto-promoted views can be opened directly — no manual registration needed:

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");

// If the SQLite file contains:
//   CREATE VIEW v_user_emails AS SELECT name, email FROM users
// Then this works automatically:
using var cursor = db.OpenView("v_user_emails");
while (cursor.MoveNext())
    Console.WriteLine($"{cursor.GetString(0)}: {cursor.GetString(1)}");
```

### Non-Promotable Views

For views with JOINs, WHERE clauses, or subqueries, use `db.Query()` instead:

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");

// SQLite file has: CREATE VIEW v_active AS SELECT * FROM users WHERE active = 1
// This is NOT promotable (has WHERE) — OpenView will throw.

// Use the SQL query pipeline instead:
using var reader = db.Query("SELECT * FROM v_active");
while (reader.Read())
    Console.WriteLine(reader.GetString(1));
```

### Manual Promotion via ViewPromoter

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var viewInfo = db.Schema.GetView("v_user_emails");
if (viewInfo != null)
{
    SharcView? promoted = ViewPromoter.TryPromote(viewInfo, db.Schema);
    if (promoted != null)
    {
        using var cursor = promoted.Open(db);
        // ... iterate
    }
}
```

### ViewInfo Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | View name |
| `Sql` | `string` | Full `CREATE VIEW ...` SQL statement |
| `SourceTables` | `IReadOnlyList<string>` | Tables referenced in FROM clause |
| `Columns` | `IReadOnlyList<ViewColumnInfo>` | Parsed column references |
| `IsSelectAll` | `bool` | True if `SELECT *` |
| `HasJoin` | `bool` | True if JOIN detected |
| `HasFilter` | `bool` | True if WHERE detected |
| `ParseSucceeded` | `bool` | True if SQL was successfully parsed |
| `IsSharcExecutable` | `bool` | True if auto-promotable (computed) |

---

## SQL Queries on Views

Registered views participate fully in Sharc's SQL query pipeline. Use `db.Query()` to run SQL against views just like tables.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var view = ViewBuilder
    .From("employees")
    .Select("name", "dept", "salary")
    .Named("emp_summary")
    .Build();

db.RegisterView(view);

// SELECT with WHERE
using var r1 = db.Query("SELECT name FROM emp_summary WHERE salary > 80000");
while (r1.Read())
    Console.WriteLine(r1.GetString(0));

// ORDER BY + LIMIT
using var r2 = db.Query(
    "SELECT name, salary FROM emp_summary ORDER BY salary DESC LIMIT 10");

// Aggregation
using var r3 = db.Query(
    "SELECT dept, COUNT(*) AS cnt, AVG(salary) AS avg_sal FROM emp_summary GROUP BY dept");

// DISTINCT
using var r4 = db.Query("SELECT DISTINCT dept FROM emp_summary");
```

### SQL Features Supported on Views

| Feature | Example |
|---------|---------|
| SELECT + projection | `SELECT name, dept FROM view_name` |
| WHERE | `SELECT * FROM view_name WHERE age > 30` |
| ORDER BY | `SELECT * FROM view_name ORDER BY name ASC` |
| LIMIT / OFFSET | `SELECT * FROM view_name LIMIT 10 OFFSET 20` |
| COUNT / AVG / SUM | `SELECT dept, COUNT(*) FROM view_name GROUP BY dept` |
| GROUP BY | `SELECT dept, AVG(salary) FROM view_name GROUP BY dept` |
| DISTINCT | `SELECT DISTINCT dept FROM view_name` |
| JOIN | `SELECT * FROM view_name JOIN other ON ...` |
| UNION / INTERSECT / EXCEPT | `SELECT name FROM view1 UNION SELECT name FROM view2` |

---

## Composing Filters: Programmatic + SQL

When a view has a programmatic `Where()` filter AND you add a SQL `WHERE` clause via `db.Query()`, **both filters are applied**. The programmatic filter runs first (during pre-materialization), then the SQL filter runs on the results.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Programmatic filter: only engineering department
var engView = ViewBuilder
    .From("employees")
    .Select("name", "age", "dept", "salary")
    .Where(row => row.GetString(2) == "engineering")
    .Named("v_engineers")
    .Build();

db.RegisterView(engView);

// SQL adds: salary > 100000
// Result: engineers AND salary > 100K (both filters applied)
using var reader = db.Query(
    "SELECT name, salary FROM v_engineers WHERE salary > 100000 ORDER BY salary DESC");

while (reader.Read())
    Console.WriteLine($"{reader.GetString(0)}: ${reader.GetInt64(1):N0}");
```

### How Composed Filters Work

Views with programmatic filters cannot be expressed as pure SQL. Sharc handles this via **pre-materialization**:

1. The view cursor is opened and iterated — the programmatic filter runs
2. Matching rows are collected into an in-memory result set
3. The SQL query runs against this materialized result set
4. SQL WHERE, ORDER BY, GROUP BY etc. are applied on top

Views **without** programmatic filters use a more efficient path — they generate a synthetic SQL CTE (Cote) that references the root table directly, allowing the query optimizer to push down predicates.

---

## Views in JOINs and UNION

Registered views can be used anywhere a table name is accepted in SQL queries.

### JOIN with a View

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var deptView = ViewBuilder
    .From("departments")
    .Select("id", "dept_name")
    .Named("v_depts")
    .Build();

db.RegisterView(deptView);

// JOIN a view with a table
using var reader = db.Query(
    "SELECT e.name, v_depts.dept_name " +
    "FROM employees AS e " +
    "INNER JOIN v_depts ON e.dept_id = v_depts.id");

while (reader.Read())
    Console.WriteLine($"{reader.GetString(0)} - {reader.GetString(1)}");
```

### JOIN Two Views

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var empView = ViewBuilder.From("employees")
    .Select("name", "dept_id")
    .Named("v_emp").Build();

var deptView = ViewBuilder.From("departments")
    .Select("id", "dept_name")
    .Named("v_dept").Build();

db.RegisterView(empView);
db.RegisterView(deptView);

using var reader = db.Query(
    "SELECT v_emp.name, v_dept.dept_name " +
    "FROM v_emp INNER JOIN v_dept ON v_emp.dept_id = v_dept.id");
```

### UNION with Views and Tables

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

var activeView = ViewBuilder.From("users")
    .Select("name", "email")
    .Where(row => row.GetInt64(2) == 1)  // assuming active is col 2 pre-projection
    .Named("v_active")
    .Build();

db.RegisterView(activeView);

// UNION a view with a table
using var reader = db.Query(
    "SELECT name FROM v_active UNION SELECT name FROM archived_users ORDER BY name");
```

---

## View Priority & Override Rules

When a registered programmatic view and a SQLite `CREATE VIEW` share the same name, the **registered view wins**.

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// SQLite file has: CREATE VIEW v_users AS SELECT * FROM users
// But we register our own:
var customView = ViewBuilder
    .From("users")
    .Select("name", "email")
    .Named("v_users")   // same name as SQLite view
    .Build();

db.RegisterView(customView);

// This opens the REGISTERED view (2 columns), not the SQLite view (all columns)
using var cursor = db.OpenView("v_users");
// cursor.FieldCount == 2
```

### Lookup Order

1. Registered programmatic views (checked first)
2. SQLite schema views (auto-promoted if promotable)
3. `KeyNotFoundException` if not found in either

---

## IViewCursor API Reference

`IViewCursor` extends `IRowAccessor` and `IDisposable`.

| Member | Type | Description |
|--------|------|-------------|
| `MoveNext()` | `bool` | Advance to the next matching row. Returns `false` when exhausted. |
| `RowsRead` | `long` | Number of rows yielded (rows that passed all filters). |
| `FieldCount` | `int` | Number of projected columns. |
| `GetInt64(int ordinal)` | `long` | Get column as 64-bit integer. |
| `GetDouble(int ordinal)` | `double` | Get column as double. |
| `GetString(int ordinal)` | `string` | Get column as string. |
| `GetBlob(int ordinal)` | `byte[]` | Get column as byte array. |
| `IsNull(int ordinal)` | `bool` | Check if column value is NULL. |
| `GetColumnName(int ordinal)` | `string` | Get column name at position. |
| `GetColumnType(int ordinal)` | `SharcColumnType` | Get SQLite storage class at current row. |
| `Dispose()` | `void` | Release underlying reader resources. |

---

## IRowAccessor API Reference

The shared column-access interface, implemented by both view cursors and data readers.

| Member | Type | Description |
|--------|------|-------------|
| `FieldCount` | `int` | Number of accessible columns. |
| `GetInt64(int ordinal)` | `long` | Read integer value. |
| `GetDouble(int ordinal)` | `double` | Read floating-point value. |
| `GetString(int ordinal)` | `string` | Read text value. |
| `GetBlob(int ordinal)` | `byte[]` | Read blob value. |
| `IsNull(int ordinal)` | `bool` | Test for NULL. |
| `GetColumnName(int ordinal)` | `string` | Column name by ordinal. |
| `GetColumnType(int ordinal)` | `SharcColumnType` | Storage class for current row. |

`IRowAccessor` enables views to be used as filter inputs — the `Where(Func<IRowAccessor, bool>)` predicate receives the cursor itself.

---

## Cursor Architecture (Internals)

Sharc uses a **decorator pattern** with three internal cursor types that compose into chains:

### SimpleViewCursor

Wraps a `SharcDataReader` (B-tree table cursor). Used for the bottom of every view chain — the leaf that actually reads from disk.

```text
Table "employees" → SharcDataReader → SimpleViewCursor
```

### ProjectedViewCursor

Wraps any `IViewCursor` with an `int[]` ordinal remapping array. Used when a subview projects a subset of its parent's columns.

```text
Parent cursor → ProjectedViewCursor (int[] projection: subviewOrdinal → parentOrdinal)
```

### FilteredViewCursor

Wraps any `IViewCursor` and applies a `Func<IRowAccessor, bool>` predicate. Skips non-matching rows in `MoveNext()`.

```text
Inner cursor → FilteredViewCursor (predicate)
```

### Full Chain Example (3-level subview with filters)

```text
SharcDataReader           ← B-tree scan over "employees"
  └─ SimpleViewCursor     ← table → Level 1 (5 columns)
       └─ FilteredViewCursor  ← Level 1 filter: dept == "eng"
            └─ ProjectedViewCursor  ← Level 1 → Level 2 (3 columns)
                 └─ FilteredViewCursor  ← Level 2 filter: age >= 25
                      └─ ProjectedViewCursor  ← Level 2 → Level 3 (1 column)
```

All cursor methods use `[MethodImpl(AggressiveInlining)]` — the JIT compiles the entire chain into efficient inline code.

---

## Memory & Performance

### Allocation Profile

| Operation | Allocations | Notes |
|-----------|-------------|-------|
| `ViewBuilder.Build()` | 1 `SharcView` + optional `string[]` | Cold path (done once) |
| `view.Open(db)` | Cursor objects + `int[]` projection | Cold path (done per open) |
| `cursor.MoveNext()` | **0 B per row** | All cursor types |
| `cursor.GetInt64(ordinal)` | **0 B per row** | Single array index lookup |
| `cursor.GetString(ordinal)` | String allocation | Same as `SharcDataReader` |
| SubView projection remapping | **0 B per row** | `_projection[ordinal]` lookup |
| Filter evaluation | **0 B per row** | Delegate call, no boxing |

### Performance Characteristics

- **View overhead vs direct table read**: ~2-5% (cursor decorator cost)
- **SubView depth (1/2/3 levels)**: Negligible difference — O(1) per level
- **Filtered view in SQL query**: ~15-25% overhead from pre-materialization
- **Filter-free view in SQL query**: Near-zero overhead — generates inline CTE

---

## Safety: Circular References & Depth Limits

### Circular Dependency Detection

Sharc validates the subview chain every time `Open()` is called. If view A references view B which references view A, an exception is thrown immediately:

```csharp
// This will throw InvalidOperationException on Open():
// "Circular subview dependency detected: view 'A' appears twice in the chain."
```

### Depth Limit

SubView chains are limited to **10 levels** maximum. Exceeding this throws:

```csharp
// InvalidOperationException:
// "Subview chain depth exceeded limit (10). View 'deep_view' has too many nested parent views."
```

---

## Common Patterns

### Reusable View Definitions

Views are immutable — define once, open many times:

```csharp
using Sharc;
using Sharc.Views;

// Define at startup
var contactsView = ViewBuilder
    .From("users")
    .Select("name", "email", "phone")
    .Where(row => !row.IsNull(1))  // must have email
    .Named("contacts")
    .Build();

// Open in multiple places — each call creates a fresh cursor
using var db = SharcDatabase.Open("mydata.db");

using var cursor1 = contactsView.Open(db);
// ... process

using var cursor2 = contactsView.Open(db);
// ... independent iteration
```

### Progressive Narrowing with SubViews

Build a hierarchy of views that progressively filter and narrow data:

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("company.db");

// Tier 1: All employees
var all = ViewBuilder.From("employees")
    .Named("all_employees").Build();

// Tier 2: Active employees (filter)
var active = ViewBuilder.From(all)
    .Where(row => row.GetInt64(5) == 1)
    .Named("active_employees").Build();

// Tier 3: Active engineers (additional filter)
var engineers = ViewBuilder.From(active)
    .Where(row => row.GetString(3) == "engineering")
    .Named("active_engineers").Build();

// Tier 4: Senior active engineers (filter + projection)
var seniors = ViewBuilder.From(engineers)
    .Select("name", "email", "salary")
    .Where(row => row.GetInt64(2) > 150_000)
    .Named("senior_engineers").Build();

// Register the leaf — opening it applies ALL 3 ancestor filters
db.RegisterView(seniors);
using var cursor = db.OpenView("senior_engineers");
```

### Typed Accessor Helper

Use `GetColumnType()` for dynamic column handling:

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");
var view = ViewBuilder.From("mixed_types").Named("dynamic").Build();

using var cursor = view.Open(db);
while (cursor.MoveNext())
{
    for (int i = 0; i < cursor.FieldCount; i++)
    {
        if (cursor.IsNull(i)) { Console.Write("NULL "); continue; }

        string val = cursor.GetColumnType(i) switch
        {
            SharcColumnType.Integral => cursor.GetInt64(i).ToString(),
            SharcColumnType.Real => cursor.GetDouble(i).ToString("F2"),
            SharcColumnType.Text => cursor.GetString(i),
            SharcColumnType.Blob => $"[{cursor.GetBlob(i).Length} bytes]",
            _ => "NULL"
        };
        Console.Write($"{cursor.GetColumnName(i)}={val} ");
    }
    Console.WriteLine();
}
```

---

## Limitations

| Limitation | Workaround |
|------------|------------|
| Views are **read-only** | Use `SharcWriter` for writes |
| SQLite views with JOIN/WHERE **not auto-promotable** | Use `db.Query()` which handles all SQLite views |
| SubView depth limited to **10 levels** | Sufficient for any practical use case |
| Filtered views in SQL queries **pre-materialize** | Filter-free views use efficient CTE path |
| No materialized view caching | Each `Open()` re-scans; use `db.Query()` for cached plans |
| **Single-thread** contract | Same as `SharcDatabase` — no concurrent cursor access |

---

[Home](Home) | [Reading Data](Reading-Data) | [Querying Data](Querying-Data) | [Schema Inspection](Schema-Inspection) | [Performance Guide](Performance-Guide)
