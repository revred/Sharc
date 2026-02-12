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

## 2. Public API Surface (Core)

### 2.1 SharcDatabase — Core Object
Static factory methods instead of constructors ensure construction safety.

```csharp
public sealed class SharcDatabase : IDisposable
{
    public static SharcDatabase Open(string path, SharcOpenOptions? options = null);
    public SharcDataReader CreateReader(string tableName);
}
```

### 2.2 SharcDataReader — Row Iterator
The `SharcDataReader` provides high-performance, typed access to rows. `GetBlobSpan()` is the zero-copy escape hatch for BLOB data.

---

## 3. Public API Surface (Graph)

The `Sharc.Graph` library extends the core reader with a semantic layer for AI context storage.

### 3.1 IContextGraph
The high-level orchestrator for graph operations.

```csharp
public interface IContextGraph : IDisposable
{
    // Node/Edge storage access
    IConceptStore Concepts { get; }
    IRelationStore Relations { get; }

    // High-level traversal
    IEnumerable<GraphEdge> GetOutgoing(NodeKey source);
    GraphRecord? GetNode(NodeKey key);
}
```

### 3.2 Concept & Relation Stores
Specialized workers for graph elements.

```csharp
public interface IConceptStore
{
    // O(log N) lookup by internal integer key
    GraphRecord? Get(NodeKey key);
    // Future: lookup by Alias or GUID
}

public interface IRelationStore
{
    // Retrieve edges by source and kind
    IEnumerable<GraphEdge> GetEdges(NodeKey source, RelationKind kind);
}
```

## 4. API Patterns NOT Used (and Why)

### 4.1 No LINQ / ADO.NET
Full ADO.NET and LINQ providers add massive surface and complexity. Sharc is optimized for low-latency, zero-copy reads in resource-constrained environments (WASM/AOT), so a direct, imperative API is preferred.

## 5. Metadata and Versioning
Sharc follows SemVer. Public API is restricted to the `Sharc` and `Sharc.Graph` root namespaces. Internal namespaces are subject to change without notice.
