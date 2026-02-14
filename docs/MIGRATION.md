# Migration Guide

## Moving Read Workloads from Microsoft.Data.Sqlite to Sharc

Sharc is not a drop-in replacement, but migrating read paths is straightforward.

### 1. Opening the Database

**Before (Microsoft.Data.Sqlite):**

```csharp
using var conn = new SqliteConnection("Data Source=mydb.db");
conn.Open();
```

**After (Sharc):**

```csharp
using var db = SharcDatabase.Open("mydb.db");
```

No connection strings. No `Open()` call. `SharcDatabase.Open()` reads the header and is ready immediately.

### 2. Reading Rows

**Before:**

```csharp
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT id, name FROM users";
using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
}
```

**After:**

```csharp
using var reader = db.CreateReader("users", "id", "name");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
}
```

Column access uses ordinal indices (0-based), matching the order of the projection you specified. No SQL parsing, no prepared statements.

### 3. Point Lookups

**Before:**

```csharp
cmd.CommandText = "SELECT * FROM users WHERE id = $id";
cmd.Parameters.AddWithValue("$id", 42);
using var reader = cmd.ExecuteReader();
if (reader.Read()) { /* found */ }
```

**After:**

```csharp
using var reader = db.CreateReader("users");
if (reader.Seek(42)) { /* found in < 1 us */ }
```

This is where Sharc's advantage is largest: 7-61x faster than the SQL path.

### 4. Filtering

**Before:**

```csharp
cmd.CommandText = "SELECT * FROM users WHERE age >= 18 AND status = 'active'";
```

**After:**

```csharp
var filter = FilterStar.And(
    FilterStar.Column("age").Gte(18L),
    FilterStar.Column("status").Eq("active")
);
using var reader = db.CreateReader("users", filter);
```

### 5. Schema Introspection

**Before:**

```csharp
cmd.CommandText = "PRAGMA table_info('users')";
using var reader = cmd.ExecuteReader();
```

**After:**

```csharp
var table = db.Schema.Tables.First(t => t.Name == "users");
foreach (var col in table.Columns)
    Console.WriteLine($"{col.Name}: {col.TypeAffinity}");
```

### 6. Error Handling

Sharc uses specific exception types instead of generic `SqliteException`:

| Exception | When |
| :--- | :--- |
| `InvalidDatabaseException` | File is not a valid SQLite format 3 database |
| `CorruptPageException` | B-tree page has invalid structure |
| `SharcCryptoException` | Decryption failed (wrong password, tampered data) |
| `UnsupportedFeatureException` | Valid SQLite feature not supported by Sharc |

All are in the `Sharc.Exceptions` namespace.

### 7. In-Memory Databases

**Before:**

```csharp
using var conn = new SqliteConnection("Data Source=:memory:");
```

**After:**

```csharp
byte[] dbBytes = File.ReadAllBytes("mydb.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

Sharc's in-memory mode reads from a byte array, not a connection string. This is useful for cloud blobs, embedded resources, or network-delivered databases.

## What Stays the Same

- **File format**: Sharc reads the same `.db` files that SQLite creates. No conversion needed.
- **Column types**: INTEGER, REAL, TEXT, BLOB, NULL — same type system.
- **Threading**: Multiple readers work in parallel, same as SQLite in WAL mode.

## What Changes

| Aspect | Microsoft.Data.Sqlite | Sharc |
| :--- | :--- | :--- |
| SQL queries | Full SQL support | No SQL — raw B-tree reads |
| Dependencies | Requires `e_sqlite3` native DLL | Zero native dependencies |
| Column access | By ordinal or name | By ordinal only |
| Seek performance | ~20-40 us (via SQL) | ~0.4-1.5 us (direct B-tree) |
| Package size | ~2 MB | ~250 KB |

## Performance Expectations

After migrating read workloads:

| Operation | Expected Improvement |
| :--- | :--- |
| Point lookups | 7-61x faster |
| Sequential scans | 2-4x faster |
| Schema reads | 5-13x faster |
| Batch lookups | 40-66x faster |
| Memory allocation | Parity or less |
