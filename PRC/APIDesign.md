# API Design — Sharc

## 1. Design Principles

### 1.1 Pit of Success
The API should make correct usage easy and incorrect usage difficult. Opening a database, reading rows, and disposing resources should each be a single obvious call.

### 1.2 Familiarity
Developers familiar with `System.Data.IDataReader`, `Microsoft.Data.Sqlite`, or `Dapper` should feel at home. Method names like `Read()`, `GetInt64()`, `GetString()` are deliberate mirrors.

### 1.3 Zero-Surprise Disposal
All resource-owning types implement `IDisposable`. The `using` pattern is the expected usage:
```csharp
using var db = SharcDatabase.Open("data.db");
using var reader = db.CreateReader("users");
```

### 1.4 Progressive Disclosure
Simple things are simple; advanced things are possible:
- **Simple**: `SharcDatabase.Open(path)` — no options needed
- **Intermediate**: `SharcDatabase.Open(path, options)` — cache tuning, file sharing
- **Advanced**: `SharcDatabase.OpenMemory(buffer, options)` — in-memory, encrypted

## 2. Public API Surface

### 2.1 Entry Points

```csharp
// Primary: file-backed
SharcDatabase.Open(string path, SharcOpenOptions? options = null)

// Primary: memory-backed
SharcDatabase.OpenMemory(ReadOnlyMemory<byte> data, SharcOpenOptions? options = null)
```

**Decision**: Static factory methods instead of constructors. Rationale:
- Construction involves I/O and validation — too much for a constructor
- Factory methods can return different internal configurations
- Clearer intent than `new SharcDatabase(path)`

### 2.2 SharcDatabase — Core Object

```csharp
public sealed class SharcDatabase : IDisposable
{
    // Schema access (immutable after construction)
    SharcSchema Schema { get; }
    SharcDatabaseInfo Info { get; }

    // Reader creation
    SharcDataReader CreateReader(string tableName)
    SharcDataReader CreateReader(string tableName, params string[]? columns)

    // Utility
    long GetRowCount(string tableName)
}
```

**Why sealed**: No valid reason to subclass a database handle. Sealing enables JIT optimizations and communicates intent.

**Why no `ISharcDatabase` interface**: YAGNI. If testing requires mocking, consumers can wrap `SharcDatabase` in their own interface. Adding one now creates a maintenance surface for zero current benefit.

### 2.3 SharcDataReader — Row Iterator

```csharp
public sealed class SharcDataReader : IDisposable
{
    bool Read()                          // Advance to next row
    int FieldCount { get; }              // Column count
    long RowId { get; }                  // SQLite rowid

    // Typed accessors (zero-allocation for primitives)
    bool IsNull(int ordinal)
    long GetInt64(int ordinal)
    int GetInt32(int ordinal)
    double GetDouble(int ordinal)
    string GetString(int ordinal)        // Allocates string
    byte[] GetBlob(int ordinal)          // Allocates byte[]
    ReadOnlySpan<byte> GetBlobSpan(int ordinal)  // Zero-copy

    // Metadata
    string GetColumnName(int ordinal)
    SharcColumnType GetColumnType(int ordinal)

    // Boxing accessor (for generic code)
    object GetValue(int ordinal)
}
```

**Design choices**:
- **Ordinal-based access**: Faster than name-based. Name→ordinal lookup can be layered on top.
- **No `IDataReader`**: The full `System.Data.IDataReader` contract includes things like `GetSchemaTable()`, `NextResult()`, `Depth` — none of which apply. Implementing it would require dead code.
- **`GetBlobSpan()`**: The zero-copy escape hatch. Returns a span into the page buffer, valid only until the next `Read()` call. Documented prominently.
- **No async**: File reads are page-sized (typically 4 KiB). Async overhead exceeds I/O cost for sequential scans. If needed later, `SharcDataReaderAsync` can be added without breaking changes.

### 2.4 Schema Models

```csharp
SharcSchema
  ├── Tables: IReadOnlyList<TableInfo>
  ├── Indexes: IReadOnlyList<IndexInfo>
  ├── Views: IReadOnlyList<ViewInfo>
  └── GetTable(string name): TableInfo

TableInfo
  ├── Name, RootPage, Sql
  ├── Columns: IReadOnlyList<ColumnInfo>
  └── IsWithoutRowId

ColumnInfo
  ├── Name, DeclaredType, Ordinal
  ├── IsPrimaryKey, IsNotNull

IndexInfo
  ├── Name, TableName, RootPage, Sql, IsUnique

ViewInfo
  ├── Name, Sql
```

**Decision**: Schema models are **classes with `required init` properties**, not records. Rationale:
- Records generate `Equals`/`GetHashCode` based on all properties — unnecessary overhead
- `required init` gives the same construction safety without record ceremony
- `IReadOnlyList<T>` for collections — prevents mutation without runtime overhead of immutable collections

### 2.5 Options

```csharp
SharcOpenOptions
  ├── Encryption: SharcEncryptionOptions?
  ├── PageCacheSize: int (default 2000)
  ├── PreloadToMemory: bool (default false)
  └── FileShareMode: FileShare (default ReadWrite)

SharcEncryptionOptions
  ├── Password: string (required)
  ├── Kdf: SharcKdfAlgorithm (default Argon2id)
  └── Cipher: SharcCipherAlgorithm (default Aes256Gcm)
```

**Decision**: Mutable options objects, not builder pattern. Rationale:
- Options are short-lived configuration — set once, passed to factory method
- Object initializer syntax is clean enough: `new SharcOpenOptions { PageCacheSize = 500 }`
- Builders add complexity without meaningful benefit for 4-5 properties

### 2.6 Exceptions

```
SharcException (base)
  ├── InvalidDatabaseException     — bad magic, invalid header
  ├── CorruptPageException         — page-level integrity failure
  ├── SharcCryptoException         — wrong password, tampered data
  └── UnsupportedFeatureException  — valid but unsupported SQLite feature
```

**Decision**: Custom exception hierarchy rather than reusing `IOException`, `FormatException`, etc. Rationale:
- Consumers can catch `SharcException` for all Sharc errors
- `CorruptPageException.PageNumber` carries domain-specific context
- Distinguishing "invalid file" from "corrupt page" helps diagnostics

## 3. API Patterns NOT Used (and Why)

### 3.1 No `IDbConnection` / ADO.NET
Full ADO.NET contract (`DbConnection`, `DbCommand`, `DbDataReader`) adds massive surface area. Sharc is not a database provider — it's a file reader. The simplicity of `Open → CreateReader → Read` is the point.

### 3.2 No LINQ Provider
`IQueryable<T>` requires expression tree translation into a query engine. Sharc has no query engine. If consumers want LINQ, they can materialize rows into objects and use LINQ-to-Objects.

### 3.3 No Async/Await
Sequential page reads are 4 KiB — async overhead dominates. Sharc is CPU-bound during decoding and makes small sequential I/Os. If async is needed later (network-backed page sources?), it's additive.

### 3.4 No Dependency Injection
No `IServiceCollection` registration. No constructor injection. `SharcDatabase.Open()` is the composition root. Internally, layers compose via constructor parameters, but consumers never see this.

## 4. Versioning Strategy

Sharc follows SemVer. The public API surface is:
- Everything in the `Sharc` namespace
- Everything in the `Sharc.Schema` namespace
- Everything in the `Sharc.Exceptions` namespace

The `Sharc.Core` and `Sharc.Crypto` namespaces are **internal** — changes there do not constitute breaking changes.

## 5. API Extensions Status

### Implemented

| Feature           | API                                               | Status                                    |
| ----------------- | ------------------------------------------------- | ----------------------------------------- |
| WHERE filtering   | `SharcFilter` + `db.CreateReader(table, filter)`  | **M7 COMPLETE** — 6 operators, all types  |
| Index lookup      | `IndexBTreeCursor` + `WithoutRowIdCursorAdapter`  | **M7 COMPLETE** — index b-tree traversal  |

### Future (Non-Breaking)

| Feature | API Addition |
|---------|-------------|
| Column name indexing | `reader.GetOrdinal(string name)` |
| Row-as-dictionary | `reader.GetValues(): IDictionary<string, object>` |
| Typed enumeration | `db.Enumerate<T>(tableName)` via source generator |
| Async reads | `SharcDataReaderAsync` wrapper |

All are additive — no breaking changes to existing API.
