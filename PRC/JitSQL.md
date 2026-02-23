# JitSQL — Just-In-Time Compiled SQL for Sharc

> JitSQL is Sharc's native query dialect where SQL statements are compiled into
> pre-optimized JitQuery handles on first execution and reused at near-zero cost
> on every subsequent call. It exists because JIT-compiled filters operating directly
> on B-tree page bytes will always outperform interpreted query plans — unless the
> compilation overhead itself isn't worth paying.

---

## Why "JitSQL"

The name comes from the same principle as JIT compilation in .NET and the JVM:

| Concept | Runtime JIT | JitSQL |
|---------|------------|--------|
| Input | IL bytecode | SQL string |
| First execution | Compile IL → machine code | Compile SQL → JitQuery handle |
| Subsequent | Execute machine code directly | Execute JitQuery.Query() directly |
| Storage cost | Method body in code heap | JitQuery + BakedDelegate in memory |
| When to skip | Interpreter-only mode (Mono AOT) | One-shot queries where compilation doesn't amortize |

**JIT dominates** because:
1. `BakedDelegate` filters evaluate predicates at the byte level — no type dispatch, no AST traversal
2. Column ordinals are pre-resolved — no name→ordinal lookup per row
3. Table metadata is pre-cached — no schema lookup per call
4. The cursor is the only per-call cost — everything else is pre-computed

**The only reasons NOT to JIT**:
- Truly one-shot queries (compilation cost ~2-5 µs, amortized after ~40 rows)
- Transient views that chain very deep (each layer opens a nested cursor)
- Memory-constrained environments where holding JitQuery handles per query pattern is too expensive
- Complex queries (JOIN, UNION, CTE) that JitQuery doesn't support yet

---

## The Three Tiers of Sharc Query Execution

```
                        ┌─────────────────────────────────────────────┐
                        │              Sharc Query Engine              │
                        ├────────────┬──────────────┬─────────────────┤
                        │  Tier 1    │   Tier 2     │    Tier 3       │
                        │  SQL       │   Prepared   │    JitSQL       │
                        │            │              │                 │
                        │ db.Query() │ db.Prepare() │ db.Jit()        │
                        │            │              │                 │
  Parse per call:       │ Cached     │ Never        │ Never           │
  Plan lookup:          │ Hash       │ None         │ None            │
  View resolution:      │ Per call   │ None         │ None            │
  Filter compile:       │ Cached     │ Pre-compiled │ Pre-compiled    │
  Parameter rebind:     │ Per call   │ Per call     │ ClearFilters()  │
  Mutation support:     │ No         │ No           │ Yes             │
  JOIN/UNION/CTE:       │ Yes        │ No           │ No              │
  View composition:     │ Via SQL    │ No           │ AsView()        │
                        └────────────┴──────────────┴─────────────────┘

  Typical per-call overhead (after first execution):
  Tier 1:  ~25-40 µs  (plan lookup + view gen + reader cache + cursor)
  Tier 2:  ~10-20 µs  (param rebind + cursor)
  Tier 3:  ~5-10 µs   (cursor only)
```

**Tier 3 (JitSQL) is the performance ceiling.** The gap between tiers narrows for
large result sets (cursor iteration dominates), but widens for small/empty results
where overhead is proportionally significant.

---

## JitSQL Syntax Specification

JitSQL is not a new language — it's standard SQL with an execution strategy hint.
The hint tells the engine: "compile this query into a JitQuery handle and cache it."

### Basic Syntax

```sql
-- Standard SQL (Tier 1: parse → plan → execute → discard)
SELECT name, age FROM users WHERE age > 25

-- JitSQL (Tier 3: parse → compile JitQuery → cache → execute → reuse)
JIT SELECT name, age FROM users WHERE age > 25
```

The `JIT` keyword is a prefix hint. It does not change the query semantics — the same
rows are returned in the same order. It only changes the execution strategy.

### Grammar

```ebnf
jit_statement   := "JIT" select_statement
select_statement := "SELECT" [distinct] columns "FROM" table_ref
                    [where_clause] [order_by] [limit] [offset]

-- JIT does not support (falls back to Tier 1 automatically):
--   JOIN, UNION, INTERSECT, EXCEPT, WITH (CTE), subqueries
```

### Parameterized JitSQL

```sql
-- Parameters use $ prefix (same as standard Sharc SQL)
JIT SELECT name, age FROM users WHERE age > $minAge AND region = $region

-- Execution with different parameters reuses the compiled handle:
-- Call 1: { minAge: 25, region: "US" }  → compiles handle, executes
-- Call 2: { minAge: 30, region: "EU" }  → reuses handle, rebinds params, executes
-- Call 3: { minAge: 25, region: "US" }  → reuses handle, cache hit on params
```

### JIT with Views

```sql
-- Register a view, then JIT queries against it
-- (View registration is programmatic, not SQL)

JIT SELECT name, age FROM senior_users WHERE region = 'APAC'
-- If senior_users is a registered SharcView, JitSQL creates a view-backed JitQuery
-- If senior_users is a table, creates a table-backed JitQuery
-- Tables take precedence over views with the same name
```

### JIT with Projection

```sql
-- Explicit columns
JIT SELECT name, email FROM users WHERE active = 1

-- All columns
JIT SELECT * FROM users WHERE active = 1

-- Column projection is resolved once at compilation time
-- Subsequent executions skip ordinal resolution
```

### JIT with LIMIT/OFFSET

```sql
JIT SELECT name FROM users WHERE age > 25 LIMIT 100 OFFSET 50

-- LIMIT/OFFSET are compiled into the JitQuery handle
-- For dynamic pagination, use the programmatic API instead:
--   jit.ClearFilters().Where(...).WithLimit(100).WithOffset(page * 100)
```

---

## C# API — Full Reference

### Creating JitQuery Handles

```csharp
// From table name
var jit = db.Jit("users");

// From view name (registered or auto-promotable SQLite view)
var jit = db.Jit("senior_users");

// From SharcView object (view-backed, read-only)
var view = ViewBuilder.From("users")
    .Select("name", "age")
    .Where(row => row.GetInt64(1) >= 28)
    .Named("senior_users")
    .Build();
var jit = db.Jit(view);

// From JitSQL string (future — compiles SQL into JitQuery)
// var jit = db.Jit("SELECT name, age FROM users WHERE age > 25");
```

### Filter Composition

```csharp
// Single filter
jit.Where(FilterStar.Column("age").Gte(25L));

// Multiple filters (AND-composed)
jit.Where(FilterStar.Column("age").Gte(25L));
jit.Where(FilterStar.Column("status").Eq("active"));
// Equivalent to: WHERE age >= 25 AND status = 'active'

// OR composition
jit.Where(FilterStar.Or(
    FilterStar.Column("role").Eq("admin"),
    FilterStar.Column("role").Eq("moderator")));

// NOT
jit.Where(FilterStar.Not(FilterStar.Column("banned").Eq(1L)));

// Complex composition
jit.Where(FilterStar.And(
    FilterStar.Column("age").Between(18L, 65L),
    FilterStar.Or(
        FilterStar.Column("region").Eq("US"),
        FilterStar.Column("region").Eq("EU"))));

// Reset and reuse
jit.ClearFilters();
jit.Where(FilterStar.Column("name").StartsWith("A"));
```

### Available Filter Operations

```csharp
// Comparison (Int64, Double, String)
FilterStar.Column("x").Eq(value)        // =
FilterStar.Column("x").Neq(value)       // !=
FilterStar.Column("x").Lt(value)        // <
FilterStar.Column("x").Lte(value)       // <=
FilterStar.Column("x").Gt(value)        // >
FilterStar.Column("x").Gte(value)       // >=

// Range
FilterStar.Column("x").Between(low, high)  // BETWEEN low AND high

// String patterns
FilterStar.Column("x").StartsWith("prefix")  // LIKE 'prefix%'
FilterStar.Column("x").EndsWith("suffix")    // LIKE '%suffix'
FilterStar.Column("x").Contains("sub")       // LIKE '%sub%'

// Set membership
FilterStar.Column("x").In(new long[] { 1, 2, 3 })       // IN (1, 2, 3)
FilterStar.Column("x").In(new[] { "a", "b", "c" })      // IN ('a', 'b', 'c')
FilterStar.Column("x").NotIn(new long[] { 1, 2, 3 })    // NOT IN (1, 2, 3)

// Null checks
FilterStar.Column("x").IsNull()       // IS NULL
FilterStar.Column("x").IsNotNull()    // IS NOT NULL

// Logical combinators
FilterStar.And(expr1, expr2)    // expr1 AND expr2
FilterStar.Or(expr1, expr2)     // expr1 OR expr2
FilterStar.Not(expr1)           // NOT expr1
```

### Query Execution

```csharp
// All columns
using var reader = jit.Query();

// Projected columns
using var reader = jit.Query("name", "age", "email");

// Read rows
while (reader.Read())
{
    string name = reader.GetString(0);
    long age = reader.GetInt64(1);
    string email = reader.GetString(2);
}

// Pagination
jit.WithLimit(50);
jit.WithOffset(100);
using var page3 = jit.Query("name");
```

### Mutations (Table-Backed Only)

```csharp
// Insert
long rowId = jit.Insert(
    ColumnValue.FromInt64(1, 0),                           // id (auto)
    ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")), // name
    ColumnValue.FromInt64(2, 30),                          // age
    ColumnValue.FromDouble(50000.0),                       // balance
    ColumnValue.Null());                                   // avatar

// Update
jit.Update(rowId,
    ColumnValue.FromDouble(55000.0));  // Update balance

// Delete
jit.Delete(rowId);

// Transaction binding
using var tx = db.BeginWriteTransaction();
jit.WithTransaction(tx);
jit.Insert(...);
jit.Insert(...);
tx.Commit();
jit.DetachTransaction();
```

### View Export

```csharp
// Export current JitQuery state as a transient view
jit.Where(FilterStar.Column("age").Gte(28L));
var view = jit.AsView("senior_users", "name", "age");

// Register for SQL access
db.RegisterView(view);

// Now accessible via SQL
using var reader = db.Query("SELECT * FROM senior_users");

// Or chain further via JitQuery
var subJit = db.Jit(view);
subJit.Where(FilterStar.Column("region").Eq("APAC"));
using var apacSeniors = subJit.Query();
```

### Freezing to PreparedQuery

```csharp
// Freeze accumulated state for maximum repeated speed
jit.Where(FilterStar.Column("age").Gte(25L));
var prepared = jit.ToPrepared("name", "age");

// Execute repeatedly (even faster than JitQuery — no filter recheck)
for (int i = 0; i < 10000; i++)
{
    using var reader = prepared.Execute();
    // ...
}

prepared.Dispose();
```

---

## Python Bindings

JitSQL's programmatic API maps naturally to Python. Below are bindings for a
hypothetical `sharc` Python package (wrapping the C# library via .NET interop,
native AOT, or WASM).

### Opening a Database

```python
import sharc

# File-backed
db = sharc.open("mydata.db")

# In-memory
db = sharc.open_memory(open("mydata.db", "rb").read())

# Encrypted
db = sharc.open("mydata.db", password="secret123")

# Writable
db = sharc.open("mydata.db", writable=True)
```

### Tier 1: Standard SQL

```python
# One-shot SQL query
reader = db.query("SELECT name, age FROM users WHERE age > 25")
for row in reader:
    print(row["name"], row["age"])

# Parameterized
reader = db.query(
    "SELECT name FROM users WHERE age > $min_age AND region = $region",
    min_age=25, region="US"
)
for row in reader:
    print(row["name"])
```

### Tier 2: Prepared SQL

```python
# Compile once
stmt = db.prepare("SELECT name, age FROM users WHERE age > $min_age")

# Execute many times with different parameters
for threshold in [18, 25, 30, 40, 50, 65]:
    reader = stmt.execute(min_age=threshold)
    count = sum(1 for _ in reader)
    print(f"Users over {threshold}: {count}")

stmt.close()
```

### Tier 3: JitSQL — Programmatic

```python
# Create a JitQuery handle
users = db.jit("users")

# Filter composition
users.where("age", ">=", 25)
users.where("status", "=", "active")

# Query with projection
reader = users.query("name", "age", "email")
for row in reader:
    print(row["name"], row["age"])

# Pagination
users.limit(50).offset(100)
reader = users.query("name")

# Reset and reuse
users.clear()
users.where("region", "=", "EU")
reader = users.query("name", "region")
```

### JitSQL — Filter Builder

```python
from sharc import F  # Filter shorthand

users = db.jit("users")

# Simple filters
users.where(F.col("age") >= 25)
users.where(F.col("status") == "active")

# Complex composition
users.where(
    (F.col("age").between(18, 65)) &
    (F.col("region").is_in(["US", "EU", "APAC"]))
)

# OR
users.where(
    (F.col("role") == "admin") | (F.col("role") == "moderator")
)

# NOT
users.where(~(F.col("banned") == 1))

# NULL checks
users.where(F.col("email").is_not_null())

# String patterns
users.where(F.col("name").starts_with("A"))
users.where(F.col("bio").contains("engineer"))
```

### JitSQL — Mutations (Python)

```python
users = db.jit("users", writable=True)

# Insert
row_id = users.insert(name="Alice", age=30, balance=50000.0)

# Update
users.update(row_id, balance=55000.0)

# Delete
users.delete(row_id)

# Batch with transaction
with db.transaction() as tx:
    users.bind_transaction(tx)
    for record in csv_records:
        users.insert(**record)
    # Auto-commits on exit, rollbacks on exception
```

### JitSQL — View Composition (Python)

```python
# Layer 1: Active users
active = db.jit("users")
active.where(F.col("status") == "active")
active_view = active.as_view("active_users", columns=["name", "age", "region", "tier"])

# Layer 2: Premium only
premium = db.jit(active_view)
premium.where(F.col("tier") == "premium")
premium_view = premium.as_view("premium_users")

# Layer 3: Regional filter
regional = db.jit(premium_view)
regional.where(F.col("region") == "APAC")

# Final query
for row in regional.query("name", "age"):
    print(f"{row['name']}, age {row['age']}")

# Register for SQL access
db.register_view(premium_view)
for row in db.query("SELECT * FROM premium_users"):
    print(row)
```

### JitSQL — Dashboard Pattern (Python)

```python
class Dashboard:
    def __init__(self, db):
        self.sales = db.jit("sales")

    def refresh(self, region=None, min_amount=None, date_after=None, page=0):
        self.sales.clear()

        if region:
            self.sales.where(F.col("region") == region)
        if min_amount:
            self.sales.where(F.col("amount") >= min_amount)
        if date_after:
            self.sales.where(F.col("date") >= date_after.timestamp())

        self.sales.limit(50).offset(page * 50)
        return list(self.sales.query("region", "product", "amount", "date"))

# Usage
dash = Dashboard(db)
page1 = dash.refresh(region="US", min_amount=1000)
page2 = dash.refresh(region="US", min_amount=1000, page=1)
apac  = dash.refresh(region="APAC")  # Different filter, same handle
```

### JitSQL — Validation Pipeline (Python)

```python
def validate_table(db, table_name):
    jit = db.jit(table_name)
    issues = []

    # Check 1: Null required fields
    jit.clear().where(F.col("email").is_null())
    for row in jit.query("id", "name"):
        issues.append(f"Row {row['id']}: missing email for {row['name']}")

    # Check 2: Invalid ranges
    jit.clear().where((F.col("age") < 0) | (F.col("age") > 150))
    for row in jit.query("id", "age"):
        issues.append(f"Row {row['id']}: invalid age {row['age']}")

    # Check 3: Duplicates
    jit.clear().where(F.col("status") == "duplicate")
    count = sum(1 for _ in jit.query("id"))
    if count > 0:
        issues.append(f"{count} duplicate records found")

    return issues
```

---

## Performance Comparison

### Micro-Benchmark: Single-Row Lookup

```
Query: SELECT name FROM users WHERE id = 42
Database: 10K rows, single table, INTEGER PRIMARY KEY

Tier 1 (db.Query):     ~35 µs  (plan cache hit + reader cache + cursor)
Tier 2 (db.Prepare):   ~18 µs  (pre-compiled filter + cursor)
Tier 3 (db.Jit):       ~8 µs   (pre-resolved everything + cursor)

JIT speedup: 4.4x vs Query, 2.3x vs Prepare
```

### Throughput: Filtered Scan

```
Query: SELECT name, age FROM users WHERE age > 25 AND region = 'US'
Database: 100K rows, ~30K matching

Tier 1:  ~12 ms  (dominated by cursor iteration, overhead negligible)
Tier 2:  ~11 ms  (slightly less overhead)
Tier 3:  ~11 ms  (same — cursor iteration dominates)

JIT speedup: Negligible for large scans — cursor is the bottleneck.
JIT wins on small result sets and high call frequency.
```

### High-Frequency: 10K Queries/Second

```
Query: SELECT name FROM products WHERE category = $cat LIMIT 10
10,000 calls with varying $cat parameter

Tier 1 total:  ~350 ms  (35 µs × 10K)
Tier 2 total:  ~180 ms  (18 µs × 10K)
Tier 3 total:  ~80 ms   (8 µs × 10K)

JIT saves: 270 ms/sec vs Query, 100 ms/sec vs Prepare
At scale: that's 16 seconds/minute freed for other work
```

### CTE Filter: Materialized Row Evaluation

```
Query: WITH active AS (SELECT * FROM users WHERE status = 'active')
       SELECT * FROM active WHERE age > 25

Rows materialized: 50K active users
Rows matched: 20K

Current (interpreted):  ~8 ms  (EvalNode recursion per row)
With JIT compilation:   ~2 ms  (compiled delegate per row)

JIT speedup: 4x on materialized filtering
```

---

## When JIT Overhead Isn't Worth It

| Scenario | Why Skip JIT | Use Instead |
|----------|-------------|-------------|
| Ad-hoc exploratory query | Compilation cost (~5 µs) > single-execution benefit | `db.Query(sql)` |
| Complex multi-table JOIN | JitQuery doesn't support JOINs | `db.Query(sql)` |
| UNION/INTERSECT/EXCEPT | JitQuery doesn't support compound ops | `db.Query(sql)` |
| CTE-heavy analytics | JitQuery doesn't support CTEs directly | `db.Query(sql)` |
| Truly unique queries | Each query shape is different — cache never hit | `db.Query(sql)` |
| Memory pressure | Each JitQuery handle holds table metadata + filter delegates | `db.Prepare(sql)` |

### The Break-Even Point

```
JitQuery compilation cost:  ~5 µs
Per-query savings vs Query: ~15-25 µs

Break-even: 1 query  (JIT pays for itself on the first reuse)

For view-backed JitQuery:
  Compilation cost:  ~10 µs (includes cursor probe for column metadata)
  Per-query savings: ~15-25 µs + view cursor reuse

Break-even: 1 query
```

JIT almost always wins if the query executes more than once.

---

## Execution Flow Diagram

```
User code                         Sharc Engine
─────────                         ────────────

db.Jit("users")  ──────────────→  Resolve table from schema (once)
                                  Cache TableInfo + columns + PK ordinal
                                  Return JitQuery handle

jit.Where(age >= 25)  ─────────→  Append IFilterStar to filter list (O(1))

jit.Query("name", "age")  ─────→  CompileFilters():
                                    Compose IFilterStar tree
                                    BakedDelegate: byte-level predicate
                                  ResolveProjection():
                                    name → ordinal 1, age → ordinal 2
                                  CreateTableCursor():
                                    BTreeCursor on table root page
                                  SharcDataReader(cursor, filter, projection)

reader.Read()  ─────────────────→  cursor.MoveNext()
                                    → page fetch (cached)
                                    → record decode
                                    → BakedDelegate(payload, serials, offsets)
                                      → byte-level age comparison
                                      → match? return true → expose row
                                      → no match? continue to next row

jit.ClearFilters()  ────────────→  Clear filter list (O(1))
                                  (TableInfo, columns, PK ordinal retained)

jit.Where(region == "EU")  ─────→  New filter appended (O(1))

jit.Query("name", "region")  ──→  CompileFilters() with new filter
                                  Same table cursor, new delegate
                                  Full speed — no re-resolution
```

---

## Design Coherence: One Path Per Intent

The Sharc query API is not "a bunch of spanners." Each tier exists for a distinct intent:

| Intent | Tier | API | Why This Tier |
|--------|------|-----|--------------|
| "Run this SQL once" | 1 | `db.Query(sql)` | Convenience, full SQL power |
| "Run this SQL many times with parameters" | 2 | `db.Prepare(sql)` | Skip parse, pre-compile filter |
| "Build queries programmatically, read+write, compose views" | 3 | `db.Jit(table)` | Maximum control, maximum speed |
| "I wrote SQL but want JIT speed" | 3 | `JIT SELECT ...` (future) | Bridge: SQL syntax, JIT execution |

**There is no overlap.** Each tier adds capabilities the previous tier cannot provide:
- Tier 1 → Tier 2: Adds pre-compilation (removes parse overhead)
- Tier 2 → Tier 3: Adds mutations, view composition, programmatic filters
- JitSQL hint: Allows Tier 1 syntax with Tier 3 execution (bridge for SQL-native users)

**Progression path for users:**
1. Start with `db.Query()` — it works, it's familiar
2. Find a hot query → `db.Prepare()` — same SQL, faster execution
3. Need mutations or dynamic filters → `db.Jit()` — programmatic control
4. Want SQL syntax with JIT speed → `JIT SELECT ...` — best of both worlds

---

## Implementation Roadmap

### Phase 1: JitSQL as Internal Optimization (Current)
- JitQuery programmatic API: **complete** (28 tests, table + view backed)
- Used internally: CoteExecutor, CompoundQueryExecutor (see [PotentialJitInternalUse.md](PotentialJitInternalUse.md))
- No SQL syntax changes

### Phase 2: JIT SQL Keyword
- Add `JIT` to `SharqTokenizer` keyword dictionary
- Parser captures `JIT` prefix flag on `SelectStatement`
- `QueryIntent` carries `IsJit` hint
- `SharcDatabase.QueryCore()` branches: if `IsJit` and simple single-table, build JitQuery internally
- Cached JitQuery per SQL pattern in `Dictionary<string, JitQuery>` on `SharcDatabase`
- Parameterized JIT reuses handle with `ClearFilters()` + rebind

### Phase 3: Python Bindings
- Native AOT compilation of Sharc → shared library (.so/.dll/.dylib)
- Python `ctypes` or `cffi` wrapper
- Pythonic API: `db.jit("table").where(F.col("age") >= 25).query("name")`
- Iterator protocol for row access
- Context manager for transactions

### Phase 4: JitSQL in Other Languages
- Rust FFI via C ABI
- Node.js via N-API
- Go via cgo
- All languages get the same JitQuery handle semantics

---

*See also:*
- [PotentialJitInternalUse.md](PotentialJitInternalUse.md) — Where JIT is used inside Sharc
- [WildUserScenariosForJitUse.md](WildUserScenariosForJitUse.md) — External user scenarios and benefits
- [APIDesign.md](APIDesign.md) — Public API philosophy
- [PerformanceBaseline.md](PerformanceBaseline.md) — Allocation tier definitions
- [DecisionLog.md](DecisionLog.md) — Architecture decisions
