# Architecture Overview — Sharc

## 1. Design Philosophy

Sharc is a **layered, interface-driven, read-only SQLite file-format reader**. Each layer has a single responsibility, communicates through well-defined interfaces, and is independently testable.

Core tenets:
- **Composition over inheritance** — layers compose via interfaces
- **Spans over arrays** — data flows as `ReadOnlySpan<byte>` through the stack
- **WASM/AOT First** — Zero-reflection, zero-dynamic-code-gen
- **Zero-Copy Traversal** — Native graph access without managed object churn

## 2. Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  GRAPH LAYER (Sharc.Graph)                                 │
│                                                             │
│  IContextGraph (Orchestrator)                               │
│  ConceptStore / RelationStore (Domain Logic)                │
│  ISchemaAdapter (SQLite to Ontology Mapping)                │
└───────┬─────────────────────────────────────────────────────┘
        │
┌───────┴─────────────────────────────────────────────────────┐
│  CONSUMER LAYER                                             │
│  SharcDatabase ──→ SharcSchema                              │
│  SharcDataReader ──→ Read() / GetInt64()                    │
└───────┬─────────────────────────────────────────────────────┘
        │
┌───────┴─────────────────────────────────────────────────────┐
│  CORE ENGINE (Sharc.Core)                                  │
│  BTreeReader  ←  BTreeCursor  ← CellParser                 │
│  RecordDecoder (IRecordDecoder)                            │
│  DatabaseHeader / PageHeader                               │
└───────┬─────────────────────────────────────────────────────┘
        │
┌───────┴─────────────────────────────────────────────────────┐
│  I/O LAYER                                                 │
│  IPageSource (File, Memory, Cached)                        │
│  IPageTransform (Identity, Decrypting)                     │
└─────────────────────────────────────────────────────────────┘
```

## 3. Data Flow — Graph Traversal

1. **RelationStore.GetEdges(source)**:
   - Queries the `_relations` table for rows where the `source` column matches.
   - Uses `IBTreeReader` to perform a seek/scan.
2. **BTreeReader / BTreeCursor**:
   - Performs a binary search (`Seek`) on interior pages of the relations table.
   - Loads the leaf page containing the first possible edge record.
3. **RecordDecoder**:
   - Decodes the SQLite record into a `RelationRecord`.
4. **GenericSchemaAdapter**:
   - Maps columns (origin, kind, target) back into the domain `GraphEdge`.

## 4. Platform Support & Binary Footprint

*   **Trimming**: All projects have `<IsTrimmable>true</IsTrimmable>` set. The library is optimized for minimum binary size in Blazor WASM.
*   **AOT**: Zero-reflection means full compatibility with Native AOT deployments, enabling ultra-fast startup and execution.

## 5. Threading & Memory

- `SharcDatabase` is thread-safe for reading.
- `Sharc.Graph` stores are transient or scoped workers.
- Page cache is shared system-wide for memory efficiency.
