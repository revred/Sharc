# Sharc Cookbook

Actionable recipes for common and advanced tasks in Sharc.

## 1. Reading Data

### Paginate through results
Use `Skip()` and `Take()` on the reader (standard iterator pattern).

```csharp
using var reader = db.CreateReader("events");
int page = 2;
int pageSize = 20;

// Skip to page 2 (manually)
for (int i = 0; i < page * pageSize; i++) reader.Read();

// Read current page
for (int i = 0; i < pageSize && reader.Read(); i++)
{
    Console.WriteLine(reader.GetString("event_name"));
}
```

### Read from a MemoryStream
Handy for cloud blobs or incoming network packets.

```csharp
byte[] buffer = await GetDatabaseFromS3();
using var db = SharcDatabase.OpenMemory(buffer);
```

## 2. Searching & Filtering

### Case-Insensitive Filter
Note: Sharc's FilterStar is case-sensitive by default (binary compare).

```csharp
var reader = db.CreateReader("users")
               .Where("username", FilterOp.Equals, "revanur"); // Exact match
```

### Multiple Filters (AND logic)
Chaining `.Where()` calls applies ALL filters.

```csharp
var reader = db.CreateReader("logs")
               .Where("level", FilterOp.Equals, "Error")
               .Where("timestamp", FilterOp.GreaterThan, "2026-02-14");
```

## 3. High Performance

### Reuse Reader for Multiple Seeks
Don't recreate the reader in a loop. Reuse it to leverage the column metadata cache.

```csharp
using var reader = db.CreateReader("users");
foreach (var id in idsToFind)
{
    if (reader.Seek(id)) 
    {
        // Process row
    }
}
```

### Partial Projection
Only request the columns you actually intend to read. This significantly reduces varint decoding and B-tree leaf traversal work.

```csharp
// Load ONLY the 'email' column
using var reader = db.CreateReader("users", "email"); 
```

## 4. Graph & Trust

### Find all "Owned" entities
Using the graph layer to traverse ownership edges.

```csharp
var edges = graph.GetEdges(userId, TraversalDirection.Outgoing, "owns");
while (edges.MoveNext())
{
    Console.WriteLine($"Owns asset: {edges.TargetKey}");
}
```

### Sign a new data entry
Using a human-controlled ECDSA key to sign a ledger entry.

```csharp
var agent = AgentIdentity.FromPrivateKey(privateKey);
graph.Ledger.RecordEntry(agent, "Updated project status to 'Closed'");
```

---

[View Benchmarks](../docs/BENCHMARKS.md) | [Architecture](../docs/ARCHITECTURE.md)
