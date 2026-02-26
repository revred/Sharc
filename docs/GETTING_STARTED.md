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

## Pattern 5: Typed 128-bit Columns (GUID/UUID + FIX128)

```csharp
using Sharc.Core;
using Sharc.Core.Query;

using var db = SharcDatabase.Open("typed.db", new SharcOpenOptions { Writable = true });
using var writer = SharcWriter.From(db);
// Schema expected:
// CREATE TABLE accounts (id INTEGER PRIMARY KEY, account_guid UUID NOT NULL, balance FIX128 NOT NULL);

writer.Insert("accounts",
    ColumnValue.FromInt64(1, 1),
    ColumnValue.FromGuid(Guid.NewGuid()),                 // GUID/UUID column
    ColumnValue.FromDecimal(1234567890123.12345678m));   // FIX128 decimal

using var reader = db.CreateReader("accounts",
    FilterStar.Column("balance").Gt(1000m));

while (reader.Read())
{
    Guid accountId = reader.GetGuid(1);      // strict: GUID/UUID only
    decimal balance = reader.GetDecimal(2);   // strict: FIX128/DECIMAL* only
    Console.WriteLine($"{accountId} => {balance}");
}
```

## Pattern 6: Encrypted Database

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

## Pattern 7: In-Memory

```csharp
byte[] dbBytes = await File.ReadAllBytesAsync("mydata.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

## Pattern 8: Trust Layer (Agents & Ledger)

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

## Pattern 9: Graph Traversal

Traverse relationships with the `Sharc.Graph` extension. Uses two-phase BFS: edge-only discovery then batch node lookup (31x faster than SQLite).

```csharp
using Sharc.Graph;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;

// Initialize graph engine
using var graph = new SharcContextGraph(db.BTreeReader, new NativeSchemaAdapter());
graph.Initialize();

// BFS traversal: expand 2-hop neighborhood from node 1
var policy = new TraversalPolicy
{
    Direction = TraversalDirection.Outgoing,
    MaxDepth = 2,
    MaxFanOut = 20,
};

var result = graph.Traverse(new NodeKey(1), policy);
foreach (var node in result.Nodes)
    Console.WriteLine($"Node {node.Record.Id} at depth {node.Depth}");

// Zero-allocation edge cursor (fastest path)
using var cursor = graph.GetEdgeCursor(new NodeKey(1));
while (cursor.MoveNext())
    Console.WriteLine($"  -> {cursor.TargetKey} (kind={cursor.Kind})");
```

## Next Steps

- [Cookbook](COOKBOOK.md) — 15 recipes for common patterns
- [Benchmarks](BENCHMARKS.md) — Full performance comparison with SQLite and IndexedDB
- [Architecture](ARCHITECTURE.md) — How Sharc achieves zero-allocation reads
- [When NOT to Use Sharc](WHEN_NOT_TO_USE.md) — Honest limitations
