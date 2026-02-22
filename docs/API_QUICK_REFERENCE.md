# API Quick Reference

The 10 operations you'll use most, with copy-paste code.

---

## 1. Open a Database

```csharp
// From file
using var db = SharcDatabase.Open("data.db");

// From memory (WASM, tests)
using var db = SharcDatabase.OpenMemory(byteArray);

// With options (cache, encryption)
using var db = SharcDatabase.Open("data.db", new SharcOpenOptions { PageCacheSize = 500 });
```

## 2. Read All Rows

```csharp
using var reader = db.CreateReader("users");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
}
```

## 3. Column Projection (Faster)

```csharp
// Only decode these 2 columns — skip the rest at byte level
using var reader = db.CreateReader("users", "name", "email");
// reader[0] = name, reader[1] = email
```

## 4. Point Lookup (272ns)

```csharp
using var reader = db.CreateReader("users");
if (reader.Seek(42))  // O(log N) B-tree seek
    Console.WriteLine(reader.GetString(1));
```

## 5. Filtered Scan (Zero-Alloc)

```csharp
using var reader = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 18L),
    "name", "age");
while (reader.Read()) { /* ... */ }
```

## 6. SQL Query

```csharp
using var results = db.Query("SELECT name FROM users WHERE age > 25 ORDER BY name LIMIT 10");
while (results.Read())
    Console.WriteLine(results.GetString(0));
```

## 7. Insert

```csharp
using var writer = SharcWriter.From(db);
writer.Insert("users",
    ColumnValue.FromInt64(1, nextId),
    ColumnValue.Text(15, "Alice"u8.ToArray()),
    ColumnValue.FromInt64(3, 30));
```

## 8. Update / Delete

```csharp
using var writer = SharcWriter.From(db);
writer.Update("users", 42, ColumnValue.Text(15, "Bob"u8.ToArray()));
writer.Delete("users", 42);
```

## 9. Transaction

```csharp
using var writer = SharcWriter.From(db);
using var txn = writer.BeginTransaction();
writer.Insert("logs", ...);
writer.Insert("logs", ...);
txn.Commit();  // atomic
```

## 10. Schema Inspection

```csharp
var schema = db.Schema;
foreach (var table in schema.Tables)
{
    Console.WriteLine($"Table: {table.Name} (root page: {table.RootPage})");
    foreach (var col in table.Columns)
        Console.WriteLine($"  {col.Name}: {col.TypeAffinity}");
}
```

---

## Accessor Methods

| Method | Returns | Allocation |
|--------|---------|------------|
| `GetInt64(i)` | `long` | 0 B |
| `GetDouble(i)` | `double` | 0 B |
| `GetString(i)` | `string` | 1 alloc |
| `GetUtf8Span(i)` | `ReadOnlySpan<byte>` | 0 B |
| `GetBlob(i)` | `byte[]` | 1 alloc |
| `GetBlobSpan(i)` | `ReadOnlySpan<byte>` | 0 B |
| `GetGuid(i)` | `Guid` | 0 B (merged path) |
| `IsNull(i)` | `bool` | 0 B |

## Key Types

| Type | Purpose |
|------|---------|
| `SharcDatabase` | Entry point — open, schema, create readers |
| `SharcDataReader` | Row iterator — Read(), Seek(), accessors |
| `SharcWriter` | Write operations — Insert, Update, Delete |
| `SharcFilter` | Predicate for filtered scans |
| `SharcOpenOptions` | Configuration (cache, encryption, file share) |
| `ColumnValue` | Typed value for write operations |
