# Migration Guide

## Moving from Microsoft.Data.Sqlite to Sharc

Sharc is not a drop-in replacement, but migrating your read paths is straightforward.

### 1. The Connection string vs Open()
Standard SQLite uses connection strings. Sharc uses direct opening options.

**Old Way:**
```csharp
using var conn = new SqliteConnection("Data Source=mydb.db");
conn.Open();
```

**New Way:**
```csharp
using var db = SharcDatabase.Open("mydb.db");
```

### 2. Results Collection
Standard SQLite uses `.GetValue()` or `.GetInt64()`. Sharc uses typed methods on the reader.

**Old Way:**
```csharp
while (reader.Read()) {
    var id = (long)reader["id"];
}
```

**New Way:**
```csharp
while (reader.Read()) {
    var id = reader.GetInt64("id"); // O(1) ordinal lookup
}
```

### 3. Error Handling
Sharc uses specialized exceptions for clear performance signals.
- `SharcDatabaseException`: General database or format errors.
- `SharcCryptoException`: Decryption or key derivation failures.
- `SharcGraphException`: Graph traversal or schema mapping errors.

## Version-to-Version Changes

### v1.0.0-beta.1 (Current)
- **Breaking Change**: `TableInfo.GetColumnOrdinal` now uses a case-insensitive dictionary by default.
- **Breaking Change**: `SharcDatabase.CreateFromPageSource` is now internal. Use `SharcDatabase.Open()` or `SharcDatabase.OpenMemory()`.
- **New**: `Sharc.Graph` namespace introduces `SharcContextGraph`.
- **New**: `Sharc.Crypto` namespace for page-level binary encryption.
