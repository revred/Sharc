# Getting Started with Sharc

Sharc reads SQLite files 2-56x faster than Microsoft.Data.Sqlite, in pure C#, with zero native dependencies. This guide gets you from zero to working code in under 5 minutes.

## Install

```bash
dotnet add package Sharc
```

Optional packages:

```bash
dotnet add package Sharc.Crypto   # AES-256-GCM encryption
dotnet add package Sharc.Graph    # Graph traversal + trust layer
```

## Pattern 1: Open and Scan

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");

foreach (var table in db.Schema.Tables)
    Console.WriteLine($"{table.Name}: {table.Columns.Count} columns");

using var reader = db.CreateReader("users");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    Console.WriteLine($"{id}: {name}");
}
```

## Pattern 2: Point Lookup (Seek)

Sub-microsecond O(log N) B-tree seek — **40-60x faster** than SQLite.

```csharp
using var reader = db.CreateReader("users");

if (reader.Seek(42))
{
    Console.WriteLine($"Found: {reader.GetString(1)}");
}
```

## Pattern 3: Column Projection

Decode only the columns you need. Everything else is skipped at the byte level.

```csharp
using var reader = db.CreateReader("users", "id", "email");
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string email = reader.GetString(1);
}
```

## Pattern 4: Filtered Scan

Apply WHERE-style predicates that evaluate directly on raw page bytes.

```csharp
using var reader = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 18L));

while (reader.Read())
{
    Console.WriteLine($"{reader.GetString(1)}, age {reader.GetInt64(2)}");
}
```

For complex filters, use FilterStar:

```csharp
var filter = FilterStar.And(
    FilterStar.Column("age").Gte(18L),
    FilterStar.Column("status").Eq("active")
);

using var reader = db.CreateReader("users", filter);
```

## Pattern 5: Encrypted Database

```csharp
using Sharc;

var options = new SharcOpenOptions
{
    Encryption = new SharcEncryptionOptions { Password = "your-password" }
};

using var db = SharcDatabase.Open("secure.db", options);
using var reader = db.CreateReader("secrets");
// Reads are transparently decrypted at the page level
```

## Pattern 6: In-Memory

```csharp
byte[] dbBytes = await File.ReadAllBytesAsync("mydata.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

## Pattern 7: Trust Layer (Agents & Ledger)

Sharc includes a cryptographic ledger for provenance and multi-agent coordination.

```csharp
// 1. Create a database with system tables
using var db = SharcDatabase.Create("trusted.db");
var registry = new AgentRegistry(db);
var ledger = new LedgerManager(db);

// 2. Register an Agent Identity
var signer = new SharcSigner("agent-007");
var agentInfo = new AgentInfo(
    AgentId: "agent-007",
    Class: AgentClass.User,
    PublicKey: signer.GetPublicKey(),
    // ... validation rules ...
    Signature: signer.Sign(...) 
);
registry.RegisterAgent(agentInfo);

// 3. Append to the Immutable Ledger
var payload = new TrustPayload(PayloadType.Text, "Mission Complete");
ledger.Append(payload, signer);
```

## Pattern 8: Graph Traversal

Traverse relationships with the `Sharc.Graph` extension.

```csharp
using Sharc.Graph;

// Initialize graph engine
var graph = new SharcContextGraph(db);

// Traverse: Find all "papers" cited by "paper:123" (1 hop)
var citedPapers = graph.Traverse(
    startNode: "papers:123",
    edgeLabel: "cites",
    direction: ArrowDirection.Forward
);

foreach (var node in citedPapers)
{
    Console.WriteLine($"Cited: {node.Id}");
}
```

## Next Steps

- [Cookbook](COOKBOOK.md) — 15 recipes for common patterns
- [Benchmarks](BENCHMARKS.md) — Full performance comparison with SQLite and IndexedDB
- [Architecture](ARCHITECTURE.md) — How Sharc achieves zero-allocation reads
- [When NOT to Use Sharc](WHEN_NOT_TO_USE.md) — Honest limitations
