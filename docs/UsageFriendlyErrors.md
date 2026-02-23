# Sharc Error Guide

When something goes wrong, Sharc tells you what happened and why. This guide lists every error you might see, what caused it, and how to fix it.

---

## Opening a Database

### "Invalid SQLite magic string."

The file is not a SQLite database or is corrupted.

```csharp
// Wrong
using var db = SharcDatabase.Open("notes.txt");

// Right — use a .db or .sqlite file created by SQLite or Sharc
using var db = SharcDatabase.Open("notes.db");
```

### "Database buffer is empty."

You passed an empty byte array to `OpenMemory`.

```csharp
// Wrong
using var db = SharcDatabase.OpenMemory(Array.Empty<byte>());

// Right
var data = File.ReadAllBytes("notes.db");
using var db = SharcDatabase.OpenMemory(data);
```

### "Password required for encrypted database."

The database is encrypted but you did not provide a password.

```csharp
// Wrong
using var db = SharcDatabase.Open("secure.db");

// Right
using var db = SharcDatabase.Open("secure.db", new SharcOpenOptions { Password = "mypassword" });
```

### "Wrong password or corrupted encryption header."

The password does not match, or the file was tampered with.

### "The database is opened in read-only mode."

You tried to insert, update, or delete but the database was opened without write access.

```csharp
// Wrong — read-only by default
using var db = SharcDatabase.Open("data.db");
var jit = db.Jit("users");
jit.Insert(...); // throws

// Right — enable writes
using var db = SharcDatabase.Open("data.db", new SharcOpenOptions { Writable = true });
var jit = db.Jit("users");
jit.Insert(...); // works
```

---

## Querying Data

All query methods follow the same patterns — whether you use `db.Query()`, `db.CreateReader()`, `db.Prepare()`, or `db.Jit()`. The errors are the same.

### "Table 'orders' not found."

The table does not exist in the database.

```csharp
// Wrong — typo or table was never created
using var reader = db.Query("SELECT * FROM ordres");

// Also applies to Jit
var jit = db.Jit("ordres"); // throws

// Fix: check what tables exist
var tables = db.Schema.Tables;
foreach (var t in tables)
    Console.WriteLine(t.Name);
```

**MCP / CLI equivalent:**
```
Error: Table 'ordres' not found.
→ Run ListSchema to see available tables.
```

### "Column 'email' not found in table 'users'."

You asked for a column that does not exist on that table.

```csharp
// Wrong
var jit = db.Jit("users");
using var reader = jit.Query("name", "email"); // 'email' doesn't exist

// Fix: check table columns
var table = db.Schema.GetTable("users");
foreach (var col in table.Columns)
    Console.WriteLine($"  {col.Name} ({col.DeclaredType})");
```

### "Filter column 'status' not found in table."

Your `.Where()` filter references a column that does not exist.

```csharp
// Wrong
var jit = db.Jit("users");
jit.Where(FilterStar.Column("status").Eq("active")); // 'status' not in table
using var reader = jit.Query(); // throws at filter compilation

// Fix: use a column that exists in the table
jit.Where(FilterStar.Column("age").Gte(18L));
```

### "Parameter 'minAge' was not provided."

Your SQL uses a `$parameter` but you forgot to pass it (or the key is wrong).

```csharp
// Wrong — key mismatch
using var prepared = db.Prepare("SELECT * FROM users WHERE age > $minAge");
prepared.Execute(new Dictionary<string, object> { ["min_age"] = 18L }); // wrong key

// Right — keys must match (without the $ prefix)
prepared.Execute(new Dictionary<string, object> { ["minAge"] = 18L });
```

### "Sharq parse error at position 25: ..."

Your SQL has a syntax error. The position number tells you where.

```csharp
// Wrong
db.Query("SELCT * FROM users"); // typo in SELECT

// Right
db.Query("SELECT * FROM users");
```

**MCP / CLI equivalent:**
```
Error: Sharq parse error at position 0: Unexpected token 'SELCT'.
→ Check your SQL syntax. The error is near character 0.
```

---

## Using JitQuery

JitQuery is a table handle — the same as `db.CreateReader()` or `db.Query()` but you build the query programmatically instead of writing SQL. It also supports writes.

### Creating a JitQuery

```csharp
var jit = db.Jit("users");  // same as targeting "SELECT * FROM users"
```

If the table doesn't exist, you get the same `"Table 'xxx' not found."` error as any other query method.

### Filtering

```csharp
jit.Where(FilterStar.Column("age").Gte(18L));
jit.Where(FilterStar.Column("name").Eq("Alice"));
```

If a column doesn't exist in the filter, you get the same `"Filter column 'xxx' not found in table."` error as `db.CreateReader()` with a filter.

### Reading

```csharp
using var reader = jit.Query();              // all columns
using var reader = jit.Query("name", "age"); // specific columns
```

If a projected column doesn't exist, you get `"Column 'xxx' not found in table 'yyy'."` — the same as any other query.

### "No current row. Call Read() first."

You tried to read a column value before calling `reader.Read()`.

```csharp
// Wrong
using var reader = jit.Query();
var name = reader.GetString(0); // throws — no current row

// Right
using var reader = jit.Query();
while (reader.Read())
{
    var name = reader.GetString(0); // works
}
```

---

## Writing Data

### Insert / Update / Delete

```csharp
var jit = db.Jit("users");

// Insert
long rowId = jit.Insert(
    ColumnValue.FromInt64(1, 1),
    ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
    ColumnValue.FromInt64(2, 30),
    ColumnValue.FromDouble(100.0),
    ColumnValue.Null());

// Update
jit.Update(rowId, ...newValues...);

// Delete
jit.Delete(rowId);
```

The same operations work through `SharcWriter` — the errors are identical regardless of which API you use.

### "A transaction is already active."

You started a transaction without finishing the previous one.

```csharp
// Wrong
using var tx1 = writer.BeginTransaction();
using var tx2 = writer.BeginTransaction(); // throws

// Right — finish the first transaction before starting another
using var tx1 = writer.BeginTransaction();
tx1.Commit(); // or tx1.Rollback()
using var tx2 = writer.BeginTransaction();
```

### "Transaction already completed."

You called `Commit()` or `Rollback()` and then tried to use the transaction again.

```csharp
// Wrong
tx.Commit();
tx.Insert("users", ...); // throws

// Right — start a new transaction for new work
```

### Using JitQuery with Transactions

```csharp
using var writer = SharcWriter.From(db);
using var tx = writer.BeginTransaction();

var jit = db.Jit("users");
jit.WithTransaction(tx);       // bind to transaction
jit.Insert(...);               // goes through the transaction
jit.Insert(...);               // batched
tx.Commit();                   // both inserts committed atomically
jit.DetachTransaction();       // back to auto-commit mode
```

If you forget `.WithTransaction()`, each insert auto-commits individually (still works, just not atomic).

---

## Using PreparedQuery

### "PreparedQuery does not support compound queries (UNION/INTERSECT/EXCEPT) or CTEs. Use db.Query() for these query types."

`Prepare()` only handles simple single-table SELECT statements.

```csharp
// Wrong
db.Prepare("SELECT * FROM a UNION SELECT * FROM b");

// Right — use Query() for compound queries
db.Query("SELECT * FROM a UNION SELECT * FROM b");

// Or Prepare() for simple queries
db.Prepare("SELECT * FROM users WHERE age > 18");
```

### "PreparedQuery does not support JOIN queries. Use db.Query() for joins."

Same idea — `Prepare()` is for single-table queries. Use `db.Query()` for JOINs.

### Freezing JitQuery to PreparedQuery

```csharp
var jit = db.Jit("users");
jit.Where(FilterStar.Column("age").Gte(18L));

// ToPrepared() captures the current filter state as an immutable handle
using var prepared = jit.ToPrepared("name", "age");

// Now use it like any PreparedQuery
using var reader = prepared.Execute();
```

Same column-not-found errors apply if you pass columns that don't exist.

---

## Views

### "View 'active_users' not found in schema."

The view does not exist. Either it was never created, or the name is misspelled.

### "View 'report' is too complex for native execution (has JOIN, WHERE, or multiple tables). Use db.Query() instead."

The view definition includes JOIN or WHERE clauses that require the full query engine.

```csharp
// Wrong — native view path can't handle JOINs
db.CreateReader("report");

// Right — use the query engine
db.Query("SELECT * FROM report");
```

### "Table or view 'X' not found."

`db.Jit("name")` tried to find a table first, then a view, and neither exists.

```csharp
// Wrong — typo or never created
var jit = db.Jit("usrs"); // throws

// Fix: check tables and views
var tables = db.Schema.Tables;
var views = db.ListRegisteredViews();
```

### Using JitQuery with Views

JitQuery works with views the same way it works with tables — you just can't write to a view.

```csharp
// ── Create a view and query it via JitQuery ──
var view = ViewBuilder.From("users")
    .Select("name", "age")
    .Where(row => row.GetInt64(1) >= 18)
    .Named("adults")
    .Build();

// Option 1: Pass the view directly
var jit = db.Jit(view);
using var reader = jit.Query();

// Option 2: Register and use by name
db.RegisterView(view);
var jit2 = db.Jit("adults"); // resolves to the registered view
using var reader2 = jit2.Query();

// Add more filters on top of the view's filter
jit.Where(FilterStar.Column("age").Lte(30L));
using var reader3 = jit.Query(); // age >= 18 AND age <= 30
```

### "JitQuery backed by a view does not support mutations. Use a table-backed JitQuery."

You tried to insert, update, or delete on a view-backed JitQuery. Views are read-only.

```csharp
// Wrong — views are read-only
var jit = db.Jit(view);
jit.Insert(...); // throws

// Right — use a table-backed JitQuery for writes
var jit = db.Jit("users");
jit.Insert(...); // works
```

### "JitQuery backed by a view cannot be frozen to PreparedQuery. Use Query() directly."

`ToPrepared()` only works on table-backed JitQuery. Use `Query()` directly for views.

### Exporting JitQuery as a View

You can snapshot a JitQuery's accumulated filters into a transient view:

```csharp
var jit = db.Jit("users");
jit.Where(FilterStar.Column("age").Gte(18L));

// Export as a transient view — not registered, GC-collected when unreferenced
var view = jit.AsView("adults", "name", "age");

// Optionally register it for use in SQL queries
db.RegisterView(view);
using var reader = db.Query("SELECT * FROM adults ORDER BY name");
```

---

## Disposed Objects

### "SharcDatabase has been disposed." / "PreparedQuery has been disposed." / "JitQuery has been disposed."

You used an object after calling `.Dispose()` on it (or after a `using` block ended).

```csharp
// Wrong
SharcDataReader reader;
using (var db = SharcDatabase.Open("data.db"))
{
    var jit = db.Jit("users");
    reader = jit.Query();
} // db disposed here
reader.Read(); // throws — db is gone

// Right — keep the database alive while you use its readers
using var db = SharcDatabase.Open("data.db");
var jit = db.Jit("users");
using var reader = jit.Query();
while (reader.Read()) { ... }
```

---

## Agent Trust Errors

These only appear when using the Trust Layer with agent-scoped operations.

### "Agent 'agent-007' does not have write access to table 'secrets'."

The agent's entitlements do not allow writing to this table.

### "Agent 'agent-007' has expired (ended at 1700000000 ms)."

The agent's validity window has passed. Register a new agent or extend the window.

### "Agent 'agent-007' is not yet active (starts at 1800000000 ms)."

The agent's validity window hasn't started yet.

---

## File & Format Errors

### "Unsupported SQLite feature: UTF-16 text encoding"

The database uses a SQLite feature that Sharc does not support. Sharc supports the vast majority of SQLite format 3, but some rarely-used features (UTF-16 encoding, custom collations) are not yet implemented.

### "Page 42: Invalid b-tree page type flag: 0x03"

Internal page corruption. The database file may be damaged.

### "Page 42: Overflow page chain broken: exceeded max chain length"

A large record's overflow chain is damaged. The database file may be corrupted.

---

## Quick Reference

| You see this... | It means... | Fix |
|---|---|---|
| Table 'X' not found | Table doesn't exist | Check `db.Schema.Tables` for available tables |
| Column 'X' not found | Column doesn't exist on that table | Check `db.Schema.GetTable("t").Columns` |
| Filter column 'X' not found | `.Where()` references wrong column | Use a column that exists on the table |
| Parameter 'X' was not provided | Missing `$param` value | Pass all parameters referenced in the SQL |
| Sharq parse error at position N | SQL syntax error | Check syntax near character N |
| database is opened in read-only mode | Can't write without `Writable = true` | Add `new SharcOpenOptions { Writable = true }` |
| transaction already active | Nested transactions not allowed | Commit or rollback the current transaction first |
| transaction already completed | Used tx after commit/rollback | Start a new transaction |
| No current row | Read column before `Read()` | Call `reader.Read()` first in a while loop |
| has been disposed | Used object after dispose | Keep the parent alive (db, writer, etc.) |
| does not support compound queries | `Prepare()` only handles simple SELECT | Use `db.Query()` for UNION/INTERSECT/EXCEPT |
| does not support JOIN queries | `Prepare()` only handles single-table | Use `db.Query()` for JOINs |
| Table or view 'X' not found | Neither table nor view exists | Check `db.Schema.Tables` and `db.ListRegisteredViews()` |
| View 'X' is too complex | View has JOIN/WHERE | Use `db.Query("SELECT * FROM view")` instead |
| does not support mutations | View-backed JitQuery can't write | Use a table-backed JitQuery for writes |
| cannot be frozen to PreparedQuery | View-backed JitQuery | Use `Query()` directly instead of `ToPrepared()` |
| Password required | Database is encrypted | Pass `Password` in `SharcOpenOptions` |
| Wrong password | Bad password or tampered file | Check the password |

---

## Further Reading

- **JitSQL specification**: See `PRC/JitSQL.md` for the full JitQuery API reference, Python bindings, and performance comparison across query tiers.
- **User scenarios**: See `PRC/WildUserScenariosForJitUse.md` for real-world JitQuery usage patterns (REST APIs, dashboards, ETL, view composition).
