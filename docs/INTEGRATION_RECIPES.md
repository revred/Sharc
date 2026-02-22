# Integration Recipes

Copy-paste patterns for common Sharc integration scenarios.

---

## 1. Replace Microsoft.Data.Sqlite Reads

**Before (SQLite):**
```csharp
using Microsoft.Data.Sqlite;

using var conn = new SqliteConnection("Data Source=data.db");
conn.Open();
using var cmd = new SqliteCommand("SELECT name, email FROM users WHERE id = @id", conn);
cmd.Parameters.AddWithValue("@id", 42);
using var reader = cmd.ExecuteReader();
if (reader.Read())
    Console.WriteLine($"{reader.GetString(0)}, {reader.GetString(1)}");
```

**After (Sharc) -- 95x faster, no native dependency:**
```csharp
using Sharc;

using var db = SharcDatabase.Open("data.db");
using var reader = db.CreateReader("users", "name", "email");
if (reader.Seek(42))
    Console.WriteLine($"{reader.GetString(0)}, {reader.GetString(1)}");
```

---

## 2. Blazor WASM Embedded Database

```csharp
// In your Blazor component or service
@inject HttpClient Http

@code {
    private async Task LoadDatabase()
    {
        byte[] dbBytes = await Http.GetByteArrayAsync("data/myapp.db");
        using var db = SharcDatabase.OpenMemory(dbBytes);
        using var reader = db.CreateReader("config", "key", "value");
        while (reader.Read())
            _config[reader.GetString(0)] = reader.GetString(1);
    }
}
```

No Emscripten. No `Cross-Origin-Opener-Policy` headers. ~40KB total footprint.

---

## 3. AI Agent Memory Store

```csharp
using Sharc;

// Open or create a memory store
using var db = SharcDatabase.Open("agent_memory.db");

// Write context
using var writer = SharcWriter.From(db);
writer.Insert("memories",
    ColumnValue.FromInt64(1, nextId),
    ColumnValue.Text(15, "User prefers dark mode"u8.ToArray()),
    ColumnValue.FromDouble(17, 0.95));  // confidence score

// Retrieve context in < 300ns
using var reader = db.CreateReader("memories", "content", "confidence");
if (reader.Seek(memoryId))
{
    string content = reader.GetString(0);
    double confidence = reader.GetDouble(1);
}
```

---

## 4. Zero-Allocation Filtered Scan

```csharp
// Filter without SQL parsing -- direct B-tree scan with JIT-compiled predicate
using var reader = db.CreateReader("events",
    new SharcFilter("severity", SharcOperator.GreaterOrEqual, 3L),
    "timestamp", "message");  // column projection

while (reader.Read())
{
    // Zero allocation: Span-based access
    ReadOnlySpan<byte> message = reader.GetUtf8Span(1);
    // Process without string allocation...
}
```

Total allocation: **912 B** (cursor construction only). Zero per-row.

---

## 5. SQL Query Pipeline

```csharp
// Full SQL roundtrip: parse, compile, execute, read
using var results = db.Query(
    "SELECT dept, COUNT(*) AS cnt, AVG(score) AS avg_score " +
    "FROM users WHERE age > 25 " +
    "GROUP BY dept ORDER BY cnt DESC LIMIT 10");

while (results.Read())
    Console.WriteLine($"{results.GetString(0)}: {results.GetInt64(1)} users, avg {results.GetDouble(2):F1}");
```

Supports: SELECT, WHERE, JOIN (INNER/LEFT/CROSS), ORDER BY, GROUP BY, HAVING, LIMIT, OFFSET, UNION/INTERSECT/EXCEPT, CTEs (`WITH...AS`), parameterized queries (`$param`).

---

## 6. Encrypted Database

```csharp
using Sharc;
using Sharc.Crypto;

var options = new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions
    {
        Password = "my-secret-password",
        Kdf = SharcKdfAlgorithm.Argon2id,
        Cipher = SharcCipherAlgorithm.Aes256Gcm
    }
};

using var db = SharcDatabase.Open("encrypted.db", options);
// All reads/writes transparently encrypted at page level
using var reader = db.CreateReader("secrets", "key", "value");
```

---

## 7. Graph Traversal (31x Faster than SQLite CTEs)

```csharp
using Sharc.Graph;

using var graph = new SharcContextGraph(db);

// Zero-allocation edge cursor
using var cursor = graph.GetEdgeCursor(new NodeKey(userId));
while (cursor.MoveNext())
{
    long friendId = cursor.TargetKey;
    // Process relationship without allocation
}

// Multi-hop BFS traversal
var results = graph.Traverse(new NodeKey(userId), maxHops: 2);
```

---

## 8. Multi-Agent Change Detection

```csharp
// Agent B holds an open reader
using var reader = db.CreateReader("shared_state");

// Agent A writes...
using (var writer = SharcWriter.From(db))
    writer.Insert("shared_state", ...);

// Agent B detects the change passively
if (reader.IsStale)
{
    // Data changed -- refresh
    using var fresh = db.CreateReader("shared_state");
    while (fresh.Read()) { /* process updated data */ }
}
```

---

## 9. Batch Writes in a Transaction

```csharp
using var writer = SharcWriter.From(db);

// All 1000 inserts in a single ACID transaction
using var txn = writer.BeginTransaction();
for (int i = 0; i < 1000; i++)
{
    writer.Insert("logs",
        ColumnValue.FromInt64(1, i),
        ColumnValue.Text(15, $"Event {i}"u8.ToArray()));
}
txn.Commit();
// 5.24 ms for 1K inserts (1.3x faster than SQLite, 14x less allocation)
```

---

## 10. Read Existing SQLite File (One-Liner)

```csharp
// Any standard SQLite Format 3 file, no conversion needed
using var db = SharcDatabase.Open("/path/to/existing.db");
```

No import step. No format conversion. Sharc reads the same `.db` files that SQLite writes.
