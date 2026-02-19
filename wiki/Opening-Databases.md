# Opening Databases

## From File

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");
```

With options:

```csharp
var options = new SharcOpenOptions
{
    PageCacheSize = 500,        // LRU cache (default: 200 pages)
    PreloadToMemory = true,     // Read entire file into RAM on open
    FileShareMode = FileShare.Read,
};

using var db = SharcDatabase.Open("mydata.db", options);
```

## From Memory

Load from a byte array (cloud blobs, embedded resources, network):

```csharp
byte[] dbBytes = await File.ReadAllBytesAsync("mydata.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

> **Note:** The buffer is not copied. Keep the array alive for the lifetime of the database.

## Create New Database

```csharp
using var db = SharcDatabase.Create("new.db");
```

Creates a valid SQLite database file with the correct header and schema table.

## Encrypted Databases

```csharp
var options = new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions
    {
        Password = "your-password",
        Kdf = SharcKdfAlgorithm.Argon2id,           // Default
        Cipher = SharcCipherAlgorithm.Aes256Gcm,     // Default, HW-accelerated
    }
};

using var db = SharcDatabase.Open("secure.db", options);
// All reads are transparently decrypted at the page level
```

See [Encryption](Encryption) for details on algorithms and key derivation.

## SharcOpenOptions Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Encryption` | `SharcEncryptionOptions?` | `null` | Encryption configuration. `null` = unencrypted |
| `PageCacheSize` | `int` | `200` | LRU page cache capacity. `0` = disabled |
| `PreloadToMemory` | `bool` | `false` | Read entire file into memory on open |
| `FileShareMode` | `FileShare` | `ReadWrite` | File locking mode |
| `Writable` | `bool` | `false` | Open for writing (required for `SharcWriter`) |

## Database Properties

Once opened, `SharcDatabase` exposes:

```csharp
db.FilePath       // string? — null for in-memory databases
db.Schema         // SharcSchema — tables, indexes, views (lazy-loaded)
db.Header         // DatabaseHeader — page size, format version
db.UsablePageSize // int — PageSize minus reserved bytes
db.Info           // SharcDatabaseInfo — page count, version
db.BTreeReader    // IBTreeReader — for advanced consumers (graph, custom cursors)
```

## Thread Safety

`SharcDatabase` is **not thread-safe**. All operations must be called from a single thread or externally synchronized. Create separate instances for concurrent access.

## Disposal

Always dispose with `using`:

```csharp
using var db = SharcDatabase.Open("mydata.db");
// ... use db ...
// Disposed automatically at end of scope
```

Disposal releases page sources, file handles, and encryption key material (zeroed).
