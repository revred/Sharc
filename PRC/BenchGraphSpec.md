# Sharc.Graph vs SurrealDB — Benchmark Specification

## Principles

These benchmarks are designed to compare **Sharc.Graph (Embedded)** against **SurrealDB (Server)**.
While this is an "Embedded vs Server" comparison, it is the primary architectural decision for the user.
The goal is to quantify the performance benefits of avoiding the network loop and serialization overhead.

Every benchmark must:
1.  **Test the same logical operation** on both sides (e.g., "Find all neighbors").
2.  **Use equivalent datasets** — same graph topology, same record counts.
3.  **Optimize both sides** — use SurrealDB's bulk insert / optimal query syntax; use Sharc's direct B-Tree access.
4.  **Acknowledge architecture** — Sharc is in-process (zero network); SurrealDB is out-of-process (network/localhost).
    *   *Note: This effectively benchmarks "Library vs Service".*

### Test Dataset Specification ("MakerGraph")

Create a synthetic graph representing a software project structure (Concepts & Relations).

**Scale Factors:**
*   **Small**: 10k Nodes, 50k Edges
*   **Medium**: 100k Nodes, 500k Edges
*   **Large**: 1M Nodes, 5M Edges

**Schema:**

```sql
-- SurrealDB Schemaless/Flexible
-- Nodes: concept
{
    id: "concept:guid",
    kind: 1, // File
    cvn: 100,
    data: { name: "Program.cs", size: 500 }
}

-- Edges: relation
{
    in: "concept:A",
    out: "concept:B",
    kind: 10, // Defines
    data: { weight: 0.5 }
}
```

```csharp
// Sharc Schema
Nodes Table:
- Id (GUID String)
- Key (Integer, mapped from ID)
- TypeId (Integer)
- Data (JSON)

Edges Table:
- Origin (Integer Key)
- Target (Integer Key)
- Kind (Integer)
- Data (JSON)
```

### Measurement Protocol

*   **SurrealDB**: Run in Docker container (`surrealdb/surrealdb:latest`).
    *   Client: Official .NET Driver.
    *   Connection: WebSocket / HTTP (Localhost).
    *   Measure: Time from "Send Request" to "Deserialized Result".
*   **Sharc**: Run In-Process.
    *   Measure: Time from "Function Call" to "Result Object".
*   **Metric**: Median Latency (ms) and Throughput (ops/sec).
*   **Environment**: Report CPU/RAM.

---

## Category 1: Ingestion (Bulk Write)

**Why it matters:** Initial graph loading and sync performance.

| # | Benchmark | Sharc Operation | SurrealDB Operation |
|---|-----------|-----------------|---------------------|
| 1.1 | Bulk Insert Nodes (10k) | `ConceptStore.Insert` loop (transactional) | `INSERT INTO concept [...]` (batch) |
| 1.2 | Bulk Insert Edges (50k) | `RelationStore.Insert` loop | `RELATE concept:A->relation->concept:B` |

**Expected Outcome:** Sharc should handle >100k ops/sec due to direct B-Tree writing (via SQLite or custom). SurrealDB will be limited by network serialization and transaction coordination.

---

## Category 2: Point Lookups (Node Retrieval)

**Why it matters:** The most basic graph operation.

| # | Benchmark | Sharc Operation | SurrealDB Operation |
|---|-----------|-----------------|---------------------|
| 2.1 | Lookup by ID (GUID) | `ConceptStore.Get(RecordId)` | `SELECT * FROM concept:guid` |
| 2.2 | Lookup by Integer Key | `ConceptStore.Get(NodeKey)` | N/A (Surreal uses RecordID string) |
| 2.3 | Batch Lookup (100) | Loop 100x `Get` | `SELECT * FROM [concept:1, concept:2...]` |

**Expected Outcome:** Sharc `Get(NodeKey)` (O(log N)) should be sub-microsecond. SurrealDB will be sub-millisecond (network RTT). factor of 100x-1000x.

---

## Category 3: 1-Hop Traversal (Get Edges)

**Why it matters:** Core primitive for graph algorithms.

| # | Benchmark | Sharc Operation | SurrealDB Operation |
|---|-----------|-----------------|---------------------|
| 3.1 | Get Outgoing Edges (1 node) | `RelationStore.GetEdges(node)` | `SELECT ->relation->concept FROM concept:A` |
| 3.2 | Get Incoming Edges (1 node) | `RelationStore.GetIncoming(node)` | `SELECT <-relation<-concept FROM concept:A` |
| 3.3 | Filtered Traversal | `GetEdges(node, Kind.Defines)` | `SELECT ->relation[WHERE kind=10]->concept FROM ...` |

**Expected Outcome:** 
*   **Sharc**: Direct B-Tree scan of edge table. Zero allocations if just counting.
*   **SurrealDB**: Index lookup + join. 
*   **Prediction**: Sharc wins heavily on latency.

---

## Category 4: Deep Traversal (Context Expansion)

**Why it matters:** Simulates "Get Context for File" (Imports -> Defines -> Calls). This is the key "Context Graph" workload.

| # | Benchmark | Sharc Operation | SurrealDB Operation |
|---|-----------|-----------------|---------------------|
| 4.1 | 2-Hop Expansion (Neighbors of Neighbors) | BFS/DFS implementation using `RelationStore` | Graph Query: `SELECT ->relation->concept->relation->concept FROM ...` |
| 4.2 | 3-Hop Expansion | BFS Depth 3 | `SELECT ... FROM ...` (nested arrows) |
| 4.3 | Weighted Path Finding (A*) | Custom `IContextGraph` logic | Custom SurrealQL script or multiple queries |

**Expected Outcome:** 
*   SurrealDB's query engine is optimized for this, but network overhead accumulates if multiple round-trips are needed.
*   Sharc executes complex logic *at the data* (in-process). Complex algorithms (A*, Community Detection) are significantly faster in Sharc because traversing millions of edges incurs no serialization cost.

---

## Category 5: Resource Efficiency

**Why it matters:** Running on user devices / vertically constrained environments.

| # | Benchmark | Metric |
|---|-----------|--------|
| 5.1 | Storage Size | Size of `.db` file vs `surreal` directory size. |
| 5.2 | RAM Idle | Memory footprint after loading graph. |
| 5.3 | RAM Peak Traversal | Peak memory during 3-hop traversal. |

**Expected Outcome:** 
*   **Storage**: SQLite/Sharc is extremely compact. SurrealDB (RocksDB/TiKV backend) has overhead.
*   **RAM**: Sharc controls every byte. No VM overhead of separate process.

---

## fairness Checklist

- [ ] SurrealDB running locally (no internet latency).
- [ ] Connect clients before timing.
- [ ] Warm up OS page cache.
- [ ] Ensure SurrealDB indexes are created (e.g., on `kind` field).
- [ ] Verify correctness (same number of edges returned).
