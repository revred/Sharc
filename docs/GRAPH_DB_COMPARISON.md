# Sharc vs Graph & Multi-Model Databases

> Sharc isn't a graph database — it's a relational reader with a graph traversal layer.
> This page compares Sharc's graph capabilities against SurrealDB, ArangoDB, and Neo4j
> to help you decide when Sharc's lightweight approach is sufficient and when you need
> a full graph engine.

---

## Architecture Comparison

| Dimension | Sharc | SurrealDB | ArangoDB | Neo4j |
| :--- | :--- | :--- | :--- | :--- |
| **Category** | Embedded library | Multi-model server | Multi-model server | Graph server |
| **Data model** | Relational + graph overlay | Document + graph + relational | Document + graph + key-value | Native graph (property) |
| **Storage** | SQLite format 3 | RocksDB / TiKV / memory | RocksDB | Native graph store |
| **Deployment** | In-process (~250 KB) | Server process | Server process | Server process (JVM) |
| **Native deps** | **None** | Rust runtime | C++ runtime | JVM |
| **WASM** | **~40 KB** | Not supported | Not supported | Not supported |
| **Query language** | SQL subset + FilterStar | SurrealQL | AQL | Cypher |
| **Graph traversal** | Two-phase BFS | `RELATE` / `->` / `<->` | `GRAPH_TRAVERSAL` / `FOR v IN` | `MATCH (a)-[r]->(b)` |
| **Write model** | Single writer (journal) | Multi-writer (distributed) | Multi-writer (distributed) | Multi-writer (bolt protocol) |
| **Encryption** | AES-256-GCM built-in | At rest (optional) | At rest (enterprise) | At rest (enterprise) |
| **Trust/Audit** | ECDSA + hash-chain ledger | Row-level auth | Collection-level auth | Role-based auth |

---

## When to Use What

### Choose Sharc When

- **Embedded context store** — AI agent needs fast lookups without a server process
- **Edge/mobile/WASM** — No JVM, no server, ~250 KB total footprint
- **Graph-over-relational** — Your data is fundamentally relational but needs occasional traversal
- **Zero-GC read paths** — Sub-microsecond seeks with 0 B allocation
- **Encrypted local storage** — AES-256-GCM with Argon2id, no external plugins
- **Audit trail** — Built-in hash-chain ledger with ECDSA agent attestation

### Choose SurrealDB/ArangoDB/Neo4j When

- **Graph is the primary model** — Relationships outnumber entities
- **Multi-hop traversals are frequent** — 5+ hop BFS/DFS with filter predicates
- **Concurrent writers** — Multiple processes/services writing simultaneously
- **Distributed** — Data spans multiple nodes with replication
- **Complex graph algorithms** — Shortest path, PageRank, community detection
- **Full-text search** — Built-in indexing (SurrealDB, ArangoDB) or Lucene (Neo4j)

---

## Pattern-by-Pattern Comparison

### 1. Create a Relationship

```csharp
// Sharc — RelationStore (concept → concept edge)
using var db = SharcDatabase.Open("knowledge.db", new SharcOpenOptions { Writable = true });
var concepts = new ConceptStore(db);
var relations = new RelationStore(db);

var alice = concepts.GetOrCreate("person:alice");
var bob = concepts.GetOrCreate("person:bob");
relations.Add(alice, bob, "knows");
```

```sql
-- SurrealDB
RELATE person:alice->knows->person:bob;
```

```aql
-- ArangoDB (AQL)
INSERT { _from: "persons/alice", _to: "persons/bob" } INTO knows
```

```cypher
// Neo4j (Cypher)
MATCH (a:Person {name: 'alice'}), (b:Person {name: 'bob'})
CREATE (a)-[:KNOWS]->(b)
```

### 2. Traverse Neighbors (1-hop)

```csharp
// Sharc — SeekFirst (zero-alloc cursor traversal)
using var db = SharcDatabase.Open("knowledge.db");
var relations = new RelationStore(db);
var alice = new NodeKey("person:alice");

using var cursor = relations.SeekFirst(alice, "knows");
while (cursor.MoveNext())
{
    NodeKey neighbor = cursor.Current.Target;
    // Process neighbor
}
```

```sql
-- SurrealDB
SELECT ->knows->person FROM person:alice;
```

```aql
-- ArangoDB
FOR v IN 1..1 OUTBOUND "persons/alice" knows RETURN v
```

```cypher
// Neo4j
MATCH (a:Person {name: 'alice'})-[:KNOWS]->(b) RETURN b
```

### 3. Multi-Hop BFS (friends-of-friends)

```csharp
// Sharc — Two-phase BFS with TraversalPolicy
using var db = SharcDatabase.Open("knowledge.db");
var graph = new GraphEngine(db);

var results = graph.Traverse(
    start: new NodeKey("person:alice"),
    edgeLabel: "knows",
    maxDepth: 3,
    policy: TraversalPolicy.BreadthFirst);

foreach (var node in results)
{
    // node.Key, node.Depth, node.Path
}
```

```sql
-- SurrealDB
SELECT ->knows[WHERE depth <= 3]->person FROM person:alice;
```

```aql
-- ArangoDB
FOR v, e, p IN 1..3 OUTBOUND "persons/alice" knows RETURN v
```

```cypher
// Neo4j
MATCH (a:Person {name: 'alice'})-[:KNOWS*1..3]->(b) RETURN b
```

**Performance note:** Sharc's two-phase BFS was benchmarked at **31x faster** than
the equivalent SQLite recursive CTE for 3-hop traversals on the same data.
However, dedicated graph databases (Neo4j, ArangoDB) with native graph storage
will outperform Sharc on deep traversals (5+ hops) because their adjacency lists
are stored directly rather than encoded in B-tree leaf pages.

### 4. Filtered Traversal

```csharp
// Sharc — TraversalPolicy with edge/node filters
var results = graph.Traverse(
    start: new NodeKey("person:alice"),
    edgeLabel: "knows",
    maxDepth: 2,
    policy: new TraversalPolicy
    {
        Direction = Direction.Outbound,
        Strategy = Strategy.BreadthFirst,
        MaxResults = 100
    });
```

```sql
-- SurrealDB
SELECT ->knows[WHERE since > '2020-01-01']->person[WHERE age > 25]
FROM person:alice LIMIT 100;
```

```aql
-- ArangoDB
FOR v, e IN 1..2 OUTBOUND "persons/alice" knows
  FILTER e.since > "2020-01-01" AND v.age > 25
  LIMIT 100
  RETURN v
```

```cypher
// Neo4j
MATCH (a:Person {name: 'alice'})-[r:KNOWS*1..2]->(b:Person)
WHERE ALL(rel IN r WHERE rel.since > date('2020-01-01'))
AND b.age > 25
RETURN b LIMIT 100
```

---

## Performance Characteristics

| Operation | Sharc | SurrealDB | ArangoDB | Neo4j |
| :--- | :--- | :--- | :--- | :--- |
| **Point lookup** | **272 ns** | ~50 µs (network) | ~50 µs (network) | ~100 µs (network) |
| **1-hop neighbors** | **~1 µs** | ~100 µs | ~100 µs | ~50 µs |
| **3-hop BFS (1K nodes)** | **~50 µs** | ~1 ms | ~500 µs | ~200 µs |
| **Deep traversal (10+ hops)** | Slower (B-tree overhead) | Fast | Fast | **Fastest** |
| **Startup time** | **0 ms** (in-process) | ~1 s (server) | ~2 s (server) | ~5 s (JVM) |
| **Memory footprint** | **~1 MB** | ~100 MB | ~200 MB | ~500 MB |
| **Cold start (WASM)** | **~40 KB load** | N/A | N/A | N/A |

*Sharc times are in-process (no network). Server-based times include ~50 µs TCP round-trip.*

---

## The Hybrid Sweet Spot

Sharc occupies a unique niche: **graph-capable embedded relational storage**.

```
Complexity of graph needs
│
│  Sharc sweet spot
│  ┌─────────────┐
│  │ 1-3 hop BFS │
│  │ Agent memory │     ArangoDB / SurrealDB
│  │ Context DAG  │     ┌──────────────────┐
│  │ Trust chains │     │ Multi-model apps  │
│  │ Small graphs │     │ Medium graphs     │    Neo4j
│  │ (<100K edges)│     │ Filtered traverse │    ┌──────────────┐
│  └─────────────┘     │ Full-text search  │    │ Social graphs │
│                      └──────────────────┘    │ Rec engines   │
│                                              │ Knowledge     │
│                                              │ graphs (M+)   │
│                                              └──────────────┘
└──────────────────────────────────────────────────────────────►
                    Scale / concurrent writes / deep traversal
```

**If your graph has <100K edges and traversals are ≤3 hops deep, Sharc is likely faster
than any server-based graph database** because there's no network round-trip, no query
parsing, and the B-tree cursor operates directly on page bytes in your process's memory.

Beyond that threshold, invest in a dedicated graph engine.
