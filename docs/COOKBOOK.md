# Sharc Cookbook

15 recipes for common patterns. All examples use the correct public API.

---

## Reading Data

### 1. Open and Scan a Table

```csharp
using Sharc;

using var db = SharcDatabase.Open("mydata.db");
using var reader = db.CreateReader("users");

while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    Console.WriteLine($"{id}: {name}");
}
```

### 2. Point Lookup (Seek)

Sub-microsecond O(log N) B-tree seek.

```csharp
using var reader = db.CreateReader("users");

if (reader.Seek(42))
    Console.WriteLine($"Found: {reader.GetString(1)}");
```

### 3. Column Projection

Decode only the columns you need. Unneeded columns are skipped at the byte level.

```csharp
using var reader = db.CreateReader("users", "id", "email");

while (reader.Read())
{
    long id = reader.GetInt64(0);     // "id" maps to index 0
    string email = reader.GetString(1); // "email" maps to index 1
}
```

### 4. In-Memory Database

Load from a byte array (cloud blobs, network packets, embedded resources).

```csharp
byte[] dbBytes = await File.ReadAllBytesAsync("mydata.db");
using var db = SharcDatabase.OpenMemory(dbBytes);
```

### 5. Batch Seeks (Reuse Reader)

Reuse a single reader for multiple seeks to leverage LRU page cache locality.

```csharp
using var reader = db.CreateReader("users");

long[] idsToFind = [10, 42, 99, 500, 1234, 5000];
foreach (long id in idsToFind)
{
    if (reader.Seek(id))
        Console.WriteLine($"[{id}] {reader.GetString(1)}");
}
```

---

## Filtering

### 6. Simple Filter (SharcFilter)

Apply a single WHERE-style predicate.

```csharp
using Sharc.Core.Query; // for SharcOperator

using var reader = db.CreateReader("users",
    new SharcFilter("age", SharcOperator.GreaterOrEqual, 18L));

while (reader.Read())
    Console.WriteLine($"{reader.GetString(1)}, age {reader.GetInt64(2)}");
```

### 7. Composable Filters (FilterStar)

Build complex predicates with AND/OR/NOT.

```csharp
var filter = FilterStar.And(
    FilterStar.Column("status").Eq("active"),
    FilterStar.Column("age").Between(21L, 65L)
);

using var reader = db.CreateReader("users", filter);
```

### 8. Filter with Projection

Combine column projection and filtering for maximum efficiency.

```csharp
var filter = FilterStar.Column("level").Gte(3L);

using var reader = db.CreateReader("logs", ["timestamp", "message"], filter);
while (reader.Read())
    Console.WriteLine($"[{reader.GetString(0)}] {reader.GetString(1)}");
```

### 9. String Prefix Search

Find rows where a column starts with a given prefix.

```csharp
var filter = FilterStar.Column("email").StartsWith("admin@");
using var reader = db.CreateReader("users", filter);
```

---

## Schema & Metadata

### 10. Schema Introspection

List all tables and their columns.

```csharp
foreach (var table in db.Schema.Tables)
{
    Console.WriteLine($"Table: {table.Name}");
    foreach (var col in table.Columns)
        Console.WriteLine($"  {col.Name} ({col.TypeAffinity})");
}
```

### 11. Row Count

Get the number of rows in a table.

```csharp
long count = db.GetRowCount("users");
Console.WriteLine($"Users: {count} rows");
```

---

## Graph Traversal

Requires `Sharc.Graph` package.

### 12. Graph Setup and Node Lookup

```csharp
using Sharc.Graph;
using Sharc.Graph.Model;
using Sharc.Graph.Schema;

using var db = SharcDatabase.Open("graph.db");
using var graph = new SharcContextGraph(db.BTreeReader, new NativeSchemaAdapter());
graph.Initialize();

var node = graph.GetNode(new NodeKey(42));
if (node != null)
    Console.WriteLine($"Node 42: {node.Value.JsonData}");
```

### 13. Zero-Allocation Edge Cursor

```csharp
// Outgoing edges — zero-allocation cursor (preferred over deprecated GetEdges)
using var cursor = graph.GetEdgeCursor(new NodeKey(1));
while (cursor.MoveNext())
    Console.WriteLine($"  -> {cursor.TargetKey} (kind={cursor.Kind}, weight={cursor.Weight})");

// Multi-hop: reuse cursor with Reset() — no new allocation
cursor.Reset(42);
while (cursor.MoveNext())
    Console.WriteLine($"  -> {cursor.TargetKey}");
```

### 14. BFS Traversal with TraversalPolicy

```csharp
var policy = new TraversalPolicy
{
    Direction = TraversalDirection.Both,
    MaxDepth = 2,
    MaxFanOut = 20,          // Hub capping
    MaxTokens = 4096,        // Stop when token budget exhausted
    IncludePaths = true,     // Track paths from start to each node
};

var result = graph.Traverse(new NodeKey(1), policy);
Console.WriteLine($"Reached {result.Nodes.Count} nodes:");
foreach (var n in result.Nodes)
{
    string path = n.Path != null ? string.Join("->", n.Path.Select(k => k.Value)) : "";
    Console.WriteLine($"  Key={n.Record.Key}, Depth={n.Depth}, Path={path}");
}
```

---

## Trust Layer

### 15. Register Agent, Append Ledger, Verify Chain

```csharp
using Sharc.Trust;
using Sharc.Core.Trust;

// Register an agent
var signer = new SharcSigner("agent-001");
var agent = new AgentInfo(
    AgentId: "agent-001",
    Class: AgentClass.Human,
    PublicKey: signer.GetPublicKey(),
    AuthorityCeiling: 10000,
    WriteScope: "*",
    ReadScope: "*",
    ValidityStart: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    ValidityEnd: DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(),
    ParentAgent: "",
    CoSignRequired: false,
    Signature: Array.Empty<byte>()
);

var registry = new AgentRegistry(db);
registry.RegisterAgent(agent);

// Append to the ledger
var ledger = new LedgerManager(db);
ledger.Append("Patient record updated by agent-001", signer);

// Verify chain integrity
bool valid = ledger.VerifyIntegrity();
Console.WriteLine(valid ? "Chain intact" : "Chain compromised!");
```

---

### 16. Enforcing Authority Limits

Demonstrates how the trust layer rejects transactions that exceed an agent's authority.

```csharp
// Agent has a ceiling of 10,000 units
var limitedAgent = new AgentInfo(..., AuthorityCeiling: 10000, ...);
registry.RegisterAgent(limitedAgent);

// Try to append a high-value transaction
var bigSpend = new TrustPayload(
    PayloadType.Financial, 
    "Transfer 50,000", 
    EconomicValue: 50000
);

try 
{
    ledger.Append(bigSpend, signer);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Blocked: {ex.Message}"); // "Authority ceiling exceeded."
}
```

### 17. Evidence Linking (Anti-Hallucination)

Link a decision to specific rows in the database to provide an audit trail.

```csharp
var evidence = new List<EvidenceRef>
{
    new EvidenceRef("lab_results", 1042, HashRow("lab_results", 1042))
};

var diagnosis = new TrustPayload(
    PayloadType.Approval,
    "Patient diagnosis: Positive",
    Evidence: evidence
);

ledger.Append(diagnosis, signer);
```

### 18. Co-Signatures (Multi-Party Approval)

Require a second agent to sign a payload before it is accepted.

```csharp
// 1. Primary agent creates payload and signs base
var proposal = new TrustPayload(PayloadType.Approval, "Deploy to Prod");
byte[] baseHash = SharcHash.Compute(proposal.ToBytes());

// 2. Secondary agent (approver) signs the base hash
var approverSigner = new SharcSigner("admin-01");
long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
byte[] toSign = [..baseHash, ..BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(timestamp))];
byte[] signature = approverSigner.Sign(toSign);

// 3. Attach co-signature
proposal = proposal with 
{ 
    CoSignatures = new List<CoSignature> 
    { 
        new CoSignature("admin-01", timestamp, signature) 
    } 
};

// 4. Submit to ledger
ledger.Append(proposal, primarySigner);
```

---

[Getting Started](GETTING_STARTED.md) | [Benchmarks](BENCHMARKS.md) | [Architecture](ARCHITECTURE.md)
