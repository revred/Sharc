# Wild User Scenarios for JitQuery Use

> Every scenario where JitQuery changes what's possible — from obvious to unexpected.
> Each scenario includes the expected benefit, the alternative without JIT, and why JIT wins.

---

## The Decision Framework

Before the scenarios, the question every user faces:

```
Should I use db.Query(), db.Prepare(), or db.Jit()?

┌────────────────────────────────────────────────────────────┐
│ Is this a one-shot query I'll never run again?             │
│   YES → db.Query(sql)                                     │
│   NO  ↓                                                   │
│                                                            │
│ Will I reuse the exact SQL with different parameters?      │
│   YES → db.Prepare(sql)                                   │
│   NO  ↓                                                   │
│                                                            │
│ Do I need any of these?                                    │
│   • Programmatic filter composition (no SQL strings)       │
│   • Read + Write through the same handle                   │
│   • View export / composition chains                       │
│   • Dynamic filter changes at runtime                      │
│   YES → db.Jit("table") or db.Jit(view)                   │
│                                                            │
│ Still unsure?                                              │
│   → Start with db.Query(). Move to Jit when you feel      │
│     the friction of string manipulation or need writes.    │
└────────────────────────────────────────────────────────────┘
```

**The speed truth**: JitQuery is the fastest read path in Sharc. But `db.Query()`
with plan caching is only ~5-15 µs slower per call. Choose JIT for the programming
model first, speed second.

---

## Scenario 1: REST API with Fixed Query Shapes

**Pattern**: Each API endpoint has a known query shape. Parameters change per request.

```csharp
// Startup: create JitQuery handles per endpoint
var usersJit = db.Jit("users");
var ordersJit = db.Jit("orders");

// GET /api/users?minAge=25&maxAge=40
app.MapGet("/api/users", (int minAge, int maxAge) =>
{
    usersJit.ClearFilters()
        .Where(FilterStar.Column("age").Gte((long)minAge))
        .Where(FilterStar.Column("age").Lte((long)maxAge))
        .WithLimit(100);

    using var reader = usersJit.Query("name", "age", "email");
    return ReadToJson(reader);
});

// GET /api/orders?status=active&limit=50
app.MapGet("/api/orders", (string status, int limit) =>
{
    ordersJit.ClearFilters()
        .Where(FilterStar.Column("status").Eq(status))
        .WithLimit(limit);

    using var reader = ordersJit.Query("id", "product", "total");
    return ReadToJson(reader);
});
```

**Without JIT**: Build SQL strings per request: `$"SELECT name, age, email FROM users WHERE age >= {minAge} AND age <= {maxAge} LIMIT 100"` — SQL injection risk, string allocations, parse overhead.

**Expected benefit**:
- Zero SQL parsing per request (filters built programmatically)
- Zero string allocation for query construction
- Type-safe filter values (no SQL injection possible)
- ~15 µs saved per request vs `db.Query(sql)` (plan cache hit path)
- At 10K req/sec: **150 ms/sec** saved in aggregate

---

## Scenario 2: Real-Time Dashboard with Live Filters

**Pattern**: User adjusts filters in a UI. Each change triggers a re-query.

```csharp
public class DashboardService
{
    private readonly JitQuery _salesJit;

    public DashboardService(SharcDatabase db)
    {
        _salesJit = db.Jit("sales");
    }

    public DashboardData Refresh(DashboardFilters filters)
    {
        _salesJit.ClearFilters();

        if (filters.Region != null)
            _salesJit.Where(FilterStar.Column("region").Eq(filters.Region));
        if (filters.MinAmount.HasValue)
            _salesJit.Where(FilterStar.Column("amount").Gte(filters.MinAmount.Value));
        if (filters.DateAfter.HasValue)
            _salesJit.Where(FilterStar.Column("date").Gte(filters.DateAfter.Value.Ticks));

        _salesJit.WithLimit(filters.PageSize);
        _salesJit.WithOffset(filters.Page * filters.PageSize);

        using var reader = _salesJit.Query("region", "product", "amount", "date");
        return MapToDashboard(reader);
    }
}
```

**Without JIT**: Reconstruct SQL string on every filter change. With 5 optional filters, the string builder logic gets messy:
```csharp
var sb = new StringBuilder("SELECT region, product, amount, date FROM sales WHERE 1=1");
if (filters.Region != null) sb.Append($" AND region = '{filters.Region}'");
// ... more string concat, injection risk, readability nightmare
```

**Expected benefit**:
- Clean conditional filter composition (no string concatenation)
- No SQL injection vectors
- Instant re-query with ClearFilters() — no re-parse
- Pagination via WithLimit/WithOffset (not SQL string manipulation)
- Sub-millisecond filter rebuild for rapid UI interactions

---

## Scenario 3: ETL Pipeline with Typed Filtering

**Pattern**: Batch processing millions of rows through typed validation rules.

```csharp
public class DataValidator
{
    public ValidationReport Validate(SharcDatabase db, string tableName)
    {
        var jit = db.Jit(tableName);
        var report = new ValidationReport();

        // Check 1: Find rows with null required fields
        jit.ClearFilters()
           .Where(FilterStar.Column("email").IsNull());
        using (var reader = jit.Query("id", "name"))
        {
            while (reader.Read())
                report.AddIssue(reader.GetInt64(0), "Missing email");
        }

        // Check 2: Find rows with invalid age ranges
        jit.ClearFilters()
           .Where(FilterStar.Or(
               FilterStar.Column("age").Lt(0L),
               FilterStar.Column("age").Gt(150L)));
        using (var reader = jit.Query("id", "name", "age"))
        {
            while (reader.Read())
                report.AddIssue(reader.GetInt64(0), $"Invalid age: {reader.GetInt64(2)}");
        }

        // Check 3: Find duplicate detection via self-comparison
        jit.ClearFilters()
           .Where(FilterStar.Column("status").Eq("duplicate"));
        report.DuplicateCount = CountRows(jit.Query("id"));

        return report;
    }
}
```

**Without JIT**: Three separate `db.Query()` calls with SQL strings, or three `db.CreateReader()` calls with `SharcFilter[]` arrays. Each creates a new reader config.

**Expected benefit**:
- Single JitQuery handle reused across all validation checks
- Table metadata resolved once at `db.Jit()` time, reused for all scans
- ClearFilters() between checks — no re-resolution
- Type-safe filter construction prevents "WHERE age > 'abc'" typos
- Schema validated at JitQuery creation, not at each query

---

## Scenario 4: Dynamic Report Builder (UI → JitQuery, No SQL)

**Pattern**: User picks columns, filters, and sort from a UI. App builds the query.

```csharp
public SharcDataReader BuildReport(SharcDatabase db, ReportConfig config)
{
    var jit = db.Jit(config.TableName);

    foreach (var filter in config.Filters)
    {
        var col = FilterStar.Column(filter.Column);
        IFilterStar expr = filter.Operator switch
        {
            "="  => col.Eq(filter.Value),
            ">"  => col.Gt(Convert.ToInt64(filter.Value)),
            ">=" => col.Gte(Convert.ToInt64(filter.Value)),
            "<"  => col.Lt(Convert.ToInt64(filter.Value)),
            "<=" => col.Lte(Convert.ToInt64(filter.Value)),
            "contains" => col.Contains(filter.Value.ToString()!),
            "is null"  => col.IsNull(),
            _ => throw new ArgumentException($"Unknown operator: {filter.Operator}")
        };
        jit.Where(expr);
    }

    if (config.Limit > 0) jit.WithLimit(config.Limit);
    if (config.Offset > 0) jit.WithOffset(config.Offset);

    return jit.Query(config.Columns);
}
```

**Without JIT**: Build a SQL string from UI selections. This is the classic
SQL injection vulnerability: user-controlled column names and values inserted
into a query string. Parameterized queries help but require managing `$param` names.

**Expected benefit**:
- Zero SQL injection surface — column names validated at schema resolution
- Operator dispatch is a simple switch, not string formatting
- Column validation happens at `jit.Query(columns)` call, not at SQL parse time
- No parameter naming/numbering complexity

---

## Scenario 5: Multi-Tenant Filtering with Composable Isolation

**Pattern**: Base query shared across tenants, with tenant-specific row isolation.

```csharp
public class TenantQueryService
{
    private readonly SharcDatabase _db;

    // Each tenant gets a view that isolates their data
    public void OnboardTenant(string tenantId)
    {
        var tenantView = ViewBuilder.From("orders")
            .Select("id", "product", "amount", "date")
            .Where(row => row.GetString(4).Equals(tenantId, StringComparison.Ordinal))
            .Named($"orders_{tenantId}")
            .Build();

        _db.RegisterView(tenantView);
    }

    // Queries automatically scoped to tenant
    public SharcDataReader QueryOrders(string tenantId, long minAmount)
    {
        var jit = _db.Jit($"orders_{tenantId}");
        jit.Where(FilterStar.Column("amount").Gte(minAmount));
        return jit.Query();
    }
}
```

**Without JIT**: Every query needs `AND tenant_id = $tenantId` appended.
Easy to forget, leading to data leaks. With views + JIT, isolation is structural.

**Expected benefit**:
- Tenant isolation is guaranteed by the view definition, not per-query discipline
- JitQuery composes the tenant filter with any additional filters automatically
- New tenants onboarded without schema changes
- Tenant views can be garbage-collected when tenant session ends

---

## Scenario 6: View Composition Chains (Layered Analytics)

**Pattern**: Build analysis layers where each step narrows the dataset.

```csharp
// Layer 1: All active users
var active = db.Jit("users");
active.Where(FilterStar.Column("status").Eq("active"));
var activeView = active.AsView("active_users", "id", "name", "age", "region", "tier");

// Layer 2: Premium tier only
var premium = db.Jit(activeView);
premium.Where(FilterStar.Column("tier").Eq("premium"));
var premiumView = premium.AsView("premium_users");

// Layer 3: Specific region
var regional = db.Jit(premiumView);
regional.Where(FilterStar.Column("region").Eq("APAC"));

// Final query: premium active users in APAC
using var reader = regional.Query("name", "age");
while (reader.Read())
    Console.WriteLine($"{reader.GetString(0)}, age {reader.GetInt64(1)}");
```

This creates a chain: `users` → `active_users` → `premium_users` → final query.
Each layer's filter is composed automatically through the view chain.

**Without JIT**: Either write one complex SQL query with nested subqueries,
or manually manage intermediate result sets. The composition semantics are implicit
in string manipulation.

**Expected benefit**:
- Each layer is independently testable and reusable
- Filters compose correctly through the view chain (AND semantics)
- Views can be registered for SQL access: `db.RegisterView(premiumView)` →
  `db.Query("SELECT * FROM premium_users WHERE region = 'APAC'")`
- Transient views (not registered) are garbage-collected when unreferenced
- The deepest chain still opens one cursor at the root and filters through all layers

---

## Scenario 7: Read-Write Through Single Handle (CRUD Operations)

**Pattern**: Same JitQuery handle for reading, inserting, updating, and deleting.

```csharp
var users = db.Jit("users");

// Read
users.Where(FilterStar.Column("name").Eq("Alice"));
using var reader = users.Query("id", "name", "balance");
reader.Read();
long aliceId = reader.GetInt64(0);
double balance = reader.GetDouble(2);
reader.Dispose();

// Update
users.Update(aliceId,
    ColumnValue.FromDouble(balance + 100.0));  // Give Alice a raise

// Delete (different filter)
users.ClearFilters()
     .Where(FilterStar.Column("status").Eq("inactive"));
using var inactive = users.Query("id");
while (inactive.Read())
    users.Delete(inactive.GetInt64(0));

// Insert
long newId = users.Insert(
    ColumnValue.FromInt64(1, 0),  // auto-id
    ColumnValue.Text(25, Encoding.UTF8.GetBytes("Bob")),
    ColumnValue.FromInt64(2, 30),
    ColumnValue.FromDouble(50000.0),
    ColumnValue.Null());
```

**Without JIT**: Separate `SharcWriter` for mutations and `db.Query()` for reads.
Two different APIs, two different mental models, two handles to manage.

**Expected benefit**:
- Single handle for all CRUD operations on one table
- Table metadata resolved once, reused for reads and writes
- Transaction binding via `jit.WithTransaction(tx)` for atomic batches
- Freeze to `PreparedQuery` via `jit.ToPrepared()` when mutation flexibility isn't needed

---

## Scenario 8: Search Autocomplete with Rapid-Fire Queries

**Pattern**: User types in a search box. Each keystroke triggers a query.

```csharp
public class SearchService
{
    private readonly JitQuery _searchJit;

    public SearchService(SharcDatabase db)
    {
        _searchJit = db.Jit("products");
    }

    public List<string> Suggest(string prefix)
    {
        _searchJit.ClearFilters()
            .Where(FilterStar.Column("name").StartsWith(prefix))
            .WithLimit(10);

        using var reader = _searchJit.Query("name");
        var results = new List<string>();
        while (reader.Read())
            results.Add(reader.GetString(0));
        return results;
    }
}
```

At 150ms between keystrokes and 5 characters typed, that's 5 queries in 750ms.
Each needs to execute in under 150ms to feel instant.

**Without JIT**: `db.Query($"SELECT name FROM products WHERE name LIKE '{prefix}%' LIMIT 10")` —
parse + plan on each keystroke. With plan caching the parse is amortized, but the
SQL string still needs construction and the LIKE pattern needs escaping.

**Expected benefit**:
- Zero SQL string construction per keystroke
- StartsWith filter compiled to byte-level prefix match (no LIKE pattern parsing)
- ~10-20 µs per call (JitQuery path) vs ~25-40 µs (Query path)
- No SQL escaping needed for user-typed input

---

## Scenario 9: Batch Transaction with Validation

**Pattern**: Insert many rows, validate each against business rules, rollback on failure.

```csharp
public int ImportBatch(SharcDatabase db, IEnumerable<CustomerRecord> records)
{
    var jit = db.Jit("customers");
    using var tx = db.BeginWriteTransaction();
    jit.WithTransaction(tx);

    int imported = 0;
    foreach (var record in records)
    {
        // Validate: no duplicate email
        jit.ClearFilters()
           .Where(FilterStar.Column("email").Eq(record.Email));
        using var existing = jit.Query("id");
        if (existing.Read())
        {
            // Duplicate — update instead of insert
            jit.Update(existing.GetInt64(0),
                ColumnValue.Text(25, Encoding.UTF8.GetBytes(record.Name)),
                ColumnValue.FromInt64(2, record.Age));
        }
        else
        {
            jit.Insert(
                ColumnValue.FromInt64(1, 0),
                ColumnValue.Text(25, Encoding.UTF8.GetBytes(record.Name)),
                ColumnValue.Text(25, Encoding.UTF8.GetBytes(record.Email)),
                ColumnValue.FromInt64(2, record.Age));
        }
        imported++;
    }

    tx.Commit();
    return imported;
}
```

**Without JIT**: Separate reader creation per validation check, separate writer for mutations.
Transaction management split across two APIs.

**Expected benefit**:
- Upsert pattern through single handle (read → decide → write)
- Transaction spans entire batch — atomic success or rollback
- ClearFilters() between records — no re-resolution of table schema
- Root page cache inside JitQuery avoids repeated `sqlite_master` lookups

---

## Scenario 10: Agent-Scoped Queries (Trust Layer)

**Pattern**: AI agents with entitlements query through JitQuery with scope enforcement.

```csharp
// Agent with limited read scope
var agentInfo = db.GetAgent("data-analyst");
// agentInfo.ReadScope = ["users:name,age", "orders:total"]

var jit = db.Jit("users");
jit.Where(FilterStar.Column("age").Gte(18L));

// Query only the columns the agent is entitled to read
using var reader = jit.Query("name", "age");  // OK — within scope
// jit.Query("name", "age", "ssn");           // Would throw UnauthorizedAccessException
```

**Expected benefit**:
- Entitlement enforcement at the JitQuery level, not per-SQL-string
- Agent scope validated against column projection at Query() time
- Programmatic filter composition prevents agents from injecting WHERE clauses
  that access unauthorized columns

---

## Expected Benefits Summary

### Performance Benefits

| Scenario | Metric | JIT Advantage |
|----------|--------|--------------|
| REST API (10K req/sec) | Aggregate parse savings | ~150 ms/sec |
| Dashboard (rapid filter changes) | Per-query latency | ~15 µs saved |
| ETL (1M rows, 3 passes) | Schema resolution | 2 resolutions saved |
| Autocomplete (5 queries/sec) | Keystroke-to-result | ~15 µs per call |
| Batch import (10K rows) | Per-row validation | Zero re-resolution |

### Developer Experience Benefits

| Benefit | Without JIT | With JIT |
|---------|-------------|----------|
| SQL injection safety | Parameterized queries required | Structural (no SQL strings) |
| Type safety | Runtime SQL parse errors | Compile-time column validation |
| Filter composition | String concatenation | Fluent API |
| Read + Write | Two APIs (Reader + Writer) | One handle |
| View composition | Nested SQL subqueries | Chained AsView() |
| Tenant isolation | Per-query WHERE clause | Structural via views |
| Transaction scope | External coordination | WithTransaction() binding |

### When NOT to Use JitQuery

| Situation | Use Instead | Why |
|-----------|------------|-----|
| One-shot exploratory query | `db.Query(sql)` | JitQuery creation overhead not amortized |
| Complex JOIN/UNION/CTE | `db.Query(sql)` | JitQuery only supports single-table or view |
| Parameterized SQL shared across teams | `db.Prepare(sql)` | SQL is the lingua franca |
| SQL already written and working | `db.Query(sql)` | Don't rewrite what works |
| Schema-agnostic tools | `db.Query(sql)` | JitQuery requires knowing table/column names |

---

*See also:*
- [PotentialJitInternalUse.md](PotentialJitInternalUse.md) — Internal JIT optimization opportunities
- [JitSQL.md](JitSQL.md) — Full JitSQL syntax specification and Python bindings
- [APIDesign.md](APIDesign.md) — Public API philosophy
