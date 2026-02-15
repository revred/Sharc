# API Design — Sharc

## 1. Design Principles

### 1.1 Pit of Success
The API should make correct usage easy and incorrect usage difficult. Opening a database, reading rows, and disposing resources should each be a single obvious call.

### 1.2 Familiarity
Developers familiar with `System.Data.IDataReader`, `Microsoft.Data.Sqlite`, or `Dapper` should feel at home. Method names like `Read()`, `GetInt64()`, `GetString()` are deliberate mirrors.

### 1.3 Zero-Surprise Disposal
All resource-owning types implement `IDisposable`. The `using` pattern is the expected usage.

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

// Primary: create new (Write mode)
SharcDatabase.Create(string path)
```

### 2.2 SharcDatabase — Core Object

```csharp
public sealed class SharcDatabase : IDisposable
{
    // Schema access
    SharcSchema Schema { get; }
    SharcDatabaseInfo Info { get; }

    // Reader creation (Low level)
    SharcDataReader CreateReader(string tableName)
    SharcDataReader CreateReader(string tableName, params string[]? columns)

    // Querying (Sharq support)
    SharcDataReader Query(string sharqQuery)
    SharcDataReader Query(IReadOnlyDictionary<string, object>? parameters, string sharqQuery)
    SharcDataReader Query(string sharqQuery, AgentInfo agent) // Entitled Query
}
```

### 2.3 SharcDataReader — Row Iterator

```csharp
public sealed class SharcDataReader : IDisposable
{
    bool Read()                          // Advance to next row
    long RowId { get; }                  // SQLite rowid

    // Typed accessors (zero-allocation for primitives)
    bool IsNull(int ordinal)
    long GetInt64(int ordinal)
    string GetString(int ordinal)        // Allocates string
    ReadOnlySpan<byte> GetBlobSpan(int ordinal)  // Zero-copy escape hatch

    // Metadata
    string GetColumnName(int ordinal)
    SharcColumnType GetColumnType(int ordinal)
}
```

### 2.4 SharcWriter — Write Engine (Experimental)

```csharp
public sealed class SharcWriter : IDisposable
{
    // Factory
    static SharcWriter Open(string path)
    static SharcWriter From(SharcDatabase db)

    // Operations
    long Insert(string tableName, params ColumnValue[] values)
    long Insert(AgentInfo agent, string tableName, params ColumnValue[] values) // Entitled Write
    long[] InsertBatch(string tableName, IEnumerable<ColumnValue[]> records)

    // Transactions
    SharcWriteTransaction BeginTransaction()
}
```

### 2.5 Graph API — Context Traversal

```csharp
public sealed class SharcContextGraph : IDisposable
{
    GraphRecord? GetNode(NodeKey key)
    
    // Traversal
    IEnumerable<GraphEdge> GetEdges(NodeKey origin, RelationKind? kind = null)
    GraphResult Traverse(NodeKey startKey, TraversalPolicy policy)
}

public record TraversalPolicy
{
    TraversalDirection Direction { get; init; }
    int? MaxDepth { get; init; }
    int? MaxFanOut { get; init; }
    RelationKind? Kind { get; init; }
}
```

### 2.6 Trust Layer — Agent Identity

```csharp
public record AgentInfo
{
    string AgentId { get; init; }
    string PublicKey { get; init; } // ECDSA P-256
    string[] ReadScope { get; init; }
    string[] WriteScope { get; init; }
}
```

## 3. API Patterns NOT Used (and Why)

### 3.1 No `IDbConnection`
Sharc is not a generic ADO.NET provider. It is a specialized context engine. Implementing the full ADO.NET contract adds massive surface area for little benefit.

### 3.2 No Full SQL Parser
We allow `SELECT ... FROM ... WHERE` via **Sharq**, but we deliberately omit `GROUP BY`, `HAVING`, `JOIN`, and Aggregates. Sharc is for **finding** data, not summarizing it.

### 3.3 No Async/Await
Page reads are 4KB sequential I/O, which is faster synchronously on modern SSDs/NVMe than the overhead of the Task state machine.

## 4. Versioning Strategy
Sharc follows SemVer.
*   **Public**: `Sharc.*` (Root, Schema, Exceptions, Graph)
*   **Internal**: `Sharc.Core`, `Sharc.Crypto`

## 5. Implementation Status
| Feature | API | Status |
| :--- | :--- | :--- |
| **Reads** | `CreateReader`, `Read` | Stable |
| **Queries** | `Query("SELECT ...")` | Stable (Sharq) |
| **Graph** | `Traverse`, `GetEdges` | Stable |
| **Trust** | `AgentInfo`, `Insert(Agent...)` | Stable |
| **Writes** | `SharcWriter.Insert` | **Experimental** (Append-Only) |

