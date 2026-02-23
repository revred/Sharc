# JitSQL for Polyglot Developers

> If you know Prisma, SQLAlchemy, Knex, or Mongoose — you already know JitSQL.
> This guide maps JitSQL patterns to familiar APIs in JavaScript/TypeScript, Python, and Go.

---

## The Core Idea

JitSQL compiles a query **once** on first call, then reuses the compiled handle at near-zero cost.
Every database library does this — JitSQL just makes the compilation boundary explicit.

```
First call:   SQL string  →  compile  →  JitQuery handle  →  execute  →  rows
Next calls:                              JitQuery handle  →  execute  →  rows
                                         (skip compile — reuse handle)
```

---

## Quick Reference: C# JitSQL ↔ Your Language

### Point Lookup

```csharp
// C# — Sharc JitSQL
using var db = SharcDatabase.Open("data.db");
using var reader = db.CreateReader("users", "name", "email");
if (reader.Seek(42))
    Console.WriteLine($"{reader.GetString(0)}, {reader.GetString(1)}");
```

```typescript
// TypeScript — Prisma
const user = await prisma.user.findUnique({
  where: { id: 42 },
  select: { name: true, email: true }
});
```

```python
# Python — SQLAlchemy
user = session.query(User.name, User.email).filter(User.id == 42).first()
```

```javascript
// JavaScript — Knex
const user = await knex('users').select('name', 'email').where('id', 42).first();
```

```go
// Go — sqlx
var user User
db.Get(&user, "SELECT name, email FROM users WHERE id = ?", 42)
```

**What's different:** Sharc's `Seek()` does a B-tree binary search directly on page bytes —
no SQL parsing, no query planner. This is why it's 20-95x faster than parameterized SELECT.

---

### Full Table Scan with Column Projection

```csharp
// C# — Sharc
using var reader = db.CreateReader("users", "id", "name", "age");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    long age = reader.GetInt64(2);
}
```

```typescript
// TypeScript — Prisma
const users = await prisma.user.findMany({
  select: { id: true, name: true, age: true }
});
for (const u of users) { /* u.id, u.name, u.age */ }
```

```python
# Python — SQLAlchemy
for id, name, age in session.query(User.id, User.name, User.age):
    pass
```

```javascript
// JavaScript — Knex
const users = await knex('users').select('id', 'name', 'age');
for (const u of users) { /* u.id, u.name, u.age */ }
```

**What's different:** Sharc skips decoding columns you didn't request.
If a row has 20 columns and you request 3, only those 3 are decoded from the page bytes.

---

### Filtered Query (JitQuery + FilterStar)

```csharp
// C# — Sharc JitQuery with FilterStar
var jit = db.Jit("users");
jit.Where(FilterStar.Column("age").Gt(25));
using var reader = jit.Query("id", "name");
while (reader.Read()) { /* reader.GetInt64(0), reader.GetString(1) */ }
```

```typescript
// TypeScript — Prisma
const users = await prisma.user.findMany({
  where: { age: { gt: 25 } },
  select: { id: true, name: true }
});
```

```python
# Python — SQLAlchemy
users = session.query(User.id, User.name).filter(User.age > 25).all()
```

```javascript
// JavaScript — Knex
const users = await knex('users').select('id', 'name').where('age', '>', 25);
```

```go
// Go — GORM
var users []User
db.Select("id", "name").Where("age > ?", 25).Find(&users)
```

**What's different:** `FilterStar.Column("age").Gt(25)` compiles to a `BakedDelegate` —
a pre-compiled predicate that evaluates at the byte level without type dispatch or AST walking.
After the first call, the filter is a direct function pointer.

---

### Prepared / Parameterized Queries

```csharp
// C# — Sharc PreparedQuery (compile once, run N times)
using var prepared = db.Prepare("SELECT id, name FROM users WHERE age > $minAge");
var params1 = new Dictionary<string, object> { ["minAge"] = 25L };
using var reader = prepared.Execute(params1);
while (reader.Read()) { /* ... */ }

// Same handle, different params — zero recompilation
var params2 = new Dictionary<string, object> { ["minAge"] = 50L };
using var reader2 = prepared.Execute(params2);
```

```typescript
// TypeScript — Prisma (implicit prepared statements)
const getOlderUsers = (minAge: number) =>
  prisma.user.findMany({ where: { age: { gt: minAge } }, select: { id: true, name: true } });

await getOlderUsers(25);
await getOlderUsers(50); // driver-level prepared statement reuse
```

```python
# Python — SQLAlchemy (implicit prepared)
stmt = select(User.id, User.name).where(User.age > bindparam('min_age'))
session.execute(stmt, {"min_age": 25})
session.execute(stmt, {"min_age": 50})
```

```javascript
// JavaScript — better-sqlite3 (explicit prepared)
const stmt = db.prepare('SELECT id, name FROM users WHERE age > ?');
stmt.all(25);
stmt.all(50); // reuses compiled statement
```

**What's different:** `db.Prepare()` returns a handle you own.
You control when it's created, reused, and disposed.
After the first `Execute()`, subsequent calls allocate 0 bytes on the managed heap.

---

### PreparedReader (Hot-Path Zero-Alloc)

```csharp
// C# — Sharc PreparedReader (zero allocation after first call)
using var prepared = db.PrepareReader("users", "name", "age");

// Hot loop — each Execute() reuses the same cursor and reader buffers
for (int i = 0; i < 1000; i++)
{
    using var reader = prepared.Execute();
    if (reader.Seek(i + 1))
    {
        string name = reader.GetString(0);
        long age = reader.GetInt64(1);
    }
}
```

**No equivalent in other languages.** This is unique to Sharc. The `PreparedReader`
pre-resolves table metadata, column ordinals, and cursor state. After the first call,
`Execute()` returns a reader that reuses all internal buffers — zero GC pressure.

In other ecosystems, the closest pattern is connection pooling + prepared statements,
but those still allocate reader/cursor objects per query execution.

---

### Mutation: Insert / Update / Delete

```csharp
// C# — Sharc JitQuery mutations (auto-commit)
var jit = db.Jit("users");
long rowId = jit.Insert(
    ColumnValue.FromInt64(1, 42),
    ColumnValue.Text(25, Encoding.UTF8.GetBytes("Alice")),
    ColumnValue.FromInt64(2, 30),
    ColumnValue.FromDouble(100.0),
    ColumnValue.Null());

jit.Update(rowId,
    ColumnValue.FromInt64(1, 42),
    ColumnValue.Text(23, Encoding.UTF8.GetBytes("Bob")),
    ColumnValue.FromInt64(2, 31),
    ColumnValue.FromDouble(200.0),
    ColumnValue.Null());

jit.Delete(rowId);
```

```typescript
// TypeScript — Prisma
const user = await prisma.user.create({ data: { id: 42, name: 'Alice', age: 30, balance: 100.0 } });
await prisma.user.update({ where: { id: 42 }, data: { name: 'Bob', age: 31, balance: 200.0 } });
await prisma.user.delete({ where: { id: 42 } });
```

```python
# Python — SQLAlchemy
user = User(id=42, name='Alice', age=30, balance=100.0)
session.add(user); session.commit()
user.name = 'Bob'; session.commit()
session.delete(user); session.commit()
```

```javascript
// JavaScript — Knex
await knex('users').insert({ id: 42, name: 'Alice', age: 30, balance: 100.0 });
await knex('users').where('id', 42).update({ name: 'Bob', age: 31, balance: 200.0 });
await knex('users').where('id', 42).del();
```

---

### Explicit Transactions

```csharp
// C# — Sharc transaction with rollback
using var writer = SharcWriter.From(db);
using var tx = writer.BeginTransaction();

var jit = db.Jit("users");
jit.WithTransaction(tx);

jit.Insert(/* ... */);
jit.Insert(/* ... */);

tx.Commit();          // or tx.Rollback() to discard
jit.DetachTransaction();
```

```typescript
// TypeScript — Prisma interactive transaction
await prisma.$transaction(async (tx) => {
  await tx.user.create({ data: { name: 'Alice' } });
  await tx.user.create({ data: { name: 'Bob' } });
  // auto-commits on success, rolls back on throw
});
```

```python
# Python — SQLAlchemy session transaction
with session.begin():
    session.add(User(name='Alice'))
    session.add(User(name='Bob'))
    # auto-commits on exit, rolls back on exception
```

```javascript
// JavaScript — Knex transaction
await knex.transaction(async (trx) => {
  await trx('users').insert({ name: 'Alice' });
  await trx('users').insert({ name: 'Bob' });
});
```

---

### Execution Hints (CACHED / JIT / DIRECT)

```csharp
// C# — Sharc execution hints in SQL strings
using var r1 = db.Query("CACHED SELECT id FROM users WHERE age > 25");  // auto-Prepare
using var r2 = db.Query("JIT SELECT id FROM users WHERE age > 25");     // auto-Jit
using var r3 = db.Query("SELECT id FROM users WHERE age > 25");         // default path
```

**No direct equivalent.** In other ecosystems, the driver decides whether to
cache/prepare statements transparently. Sharc's hints give you explicit control:

| Hint | Behavior | Analogous to |
|------|----------|-------------|
| `CACHED` | Auto-prepares query, reuses on repeat calls | `better-sqlite3`'s `.prepare()` |
| `JIT` | Compiles to FilterStar + BakedDelegate | No equivalent — unique to Sharc |
| (none) | Full SQL parse + plan each call | Default mode in most ORMs |

---

### ToPrepared: Freeze a JitQuery

```csharp
// C# — Sharc: build query fluently, then freeze for hot-path reuse
var jit = db.Jit("users");
jit.Where(FilterStar.Column("age").Gt(25));

// Freeze: captures filter + projection as immutable handle
using var prepared = jit.ToPrepared("id", "name");

// Hot loop — zero allocation per Execute()
for (int i = 0; i < 1000; i++)
{
    using var reader = prepared.Execute();
    while (reader.Read()) { /* ... */ }
}
```

**Closest equivalents:**
- **Prisma**: No equivalent (all queries are dynamic)
- **SQLAlchemy**: `stmt = select(...).where(...).compile()` — but still allocates per execution
- **Knex**: `.toSQL()` gives you the string, but no cached execution handle
- **better-sqlite3**: `.prepare()` is the closest — compiles SQL once, reuses handle

---

## Summary: Where Sharc Excels vs Other Ecosystems

| Operation | Sharc Advantage | Why |
|-----------|----------------|-----|
| Point lookup | 20-95x faster | B-tree Seek on page bytes, no SQL parsing |
| Schema read | 8-10x faster | Direct struct parse, no PRAGMA round-trips |
| Prepared hot path | Zero allocation | Cursor + reader buffer reuse via `PreparedReader` |
| Column projection | Skip unused columns | Decode only requested columns from page bytes |
| Filtered scan | Compile-once filter | `BakedDelegate` evaluates at byte level |

| Operation | SQLite Advantage | Why |
|-----------|-----------------|-----|
| Complex queries | SQL optimizer | Sharc doesn't have a cost-based query planner |
| JOINs | Native support | Sharc requires manual join via multiple readers |
| Concurrent writes | WAL mode | Sharc uses rollback journal (single writer) |
| Full-text search | FTS5 extension | Sharc has no FTS support |
