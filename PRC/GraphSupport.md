# Sharc Graph — Context Compression Engine

## 1. Executive Summary

Sharc Graph is a specialized graph database engine built on top of the Sharc storage primitive. Its primary purpose is **Context Compression for AI Coding Sessions**.

In modern LLM-based coding workflows, "context" (codebase structure, active files, documentation, conversation history, user intent) is the most valuable and scarce resource. Sending raw files or massive conversation logs wastes tokens and degrades model performance.

Sharc Graph solves this by storing a highly optimized **Semantic Ontology** of the codebase and user session. It allows the agent to:
1.  **Deduplicate Context**: Store a concept (e.g., "AuthenticationService") once and reference it by ID.
2.  **Traverse Relationships**: Retrieve only the relevant subgraph (e.g., "Show me functions calling `Login` that were modified yesterday") instead of the whole file.
3.  **Minimize Token Usage**: Use compact integer IDs for internal linkage, expanding to text only when necessary for the LLM.

## 2. Core Philosophy

1.  **Token Frugality**: Every byte stored and retrieved should justify its existence in the prompt. We prioritize dense, semantic graphs over verbose document storage.
2.  **Integer-First Addressing**: Internally, all nodes and edges use 64-bit integers. This reduces index size, improves cache locality, and speeds up traversal by orders of magnitude compared to string-based keys.
3.  **Zero-Copy Traversal**: The engine is designed to walk the graph directly from memory-mapped pages without allocating managed objects for every node visited.
4.  **Ontology over Data**: Unlike a generic document store, Sharc Graph is opinionated about storing *structure* (Types, Methods, Dependencies) and *intent* (Prompts, Goals).

## 3. Architecture

Sharc Graph layers a semantic graph model over the existing Sharc B-Tree storage engine.

```
Layer 6 (Context API)       │  ContextGraph                ← High-level AI context operations
Layer 5 (Graph Engine)      │  GraphQueryEngine            ← Traversal, Pattern Matching
Layer 4 (Graph Model)       │  NodeStore, EdgeStore        ← Concepts, Relations
────────────────────────────┼───────────────────────────────────────────────────
Layer 3 (B-Tree Read)       │  IBTreeReader                ← EXISTING
Layer 2 (Page Transform)    │  IPageTransform              ← EXISTING
Layer 1 (Page Source)       │  IPageSource                 ← EXISTING
```

### 3.1 Storage Schema

The graph is persisted using standard Sharc/SQLite conventions but with a specialized schema design optimized for density.

#### Table: `_concepts` (Nodes)
Represents entities in the context: Files, Classes, Methods, Variables, Prompts, User Messages.

| Column | Type | Description |
|---|---|---|
| `id` | GUID | Primary Key. 128-bit unique identifier. |
| `key` | INTEGER | Internal Key. 64-bit dense integer for fast edge traversal. |
| `kind` | INTEGER | Discriminator (e.g., 1=File, 2=Class, 3=Prompt). |
| `alias` | TEXT | Human-readable ID (e.g., "src/Auth.cs", "func:Login"). Indexed/Unique. |
| `data` | BLOB | Compressed semantic payload (Protobuf/MessagePack or dense JSON). |
| `tokens` | INTEGER | Estimated token count of the payload. |

#### Table: `_relations` (Edges)
Represents typed connections: `Calls`, `InheritsFrom`, `Imports`, `MentionedIn`.

| Column | Type | Description |
|---|---|---|
| `source_key` | INTEGER | FK to `_concepts.key` (Integer for speed). |
| `target_key` | INTEGER | FK to `_concepts.key` (Integer for speed). |
| `kind` | INTEGER | Edge type (e.g., 10=Calls, 11=Defines). |
| `weight` | REAL | Relevance score (0.0 - 1.0) for context ranking. |

*Critical Optimization*: Edges are stored in a clustered index on `(source_key, kind, target_key)` for fast "outgoing" traversal.

## 4. Logical Model

### 4.1 Concept Kinds (The Ontology)
Instead of generic tables, we define a core ontology for code context:

*   **Artifacts**: `File`, `Directory`, `Project`
*   **Symbols**: `Class`, `Interface`, `Method`, `Property`, `Field`
*   **Session**: `UserRequest`, `AssistantResponse`, `Plan`, `Goal`
*   **Knowledge**: `Pattern`, `Rule`, `Constraint`

### 4.2 Relation Kinds
*   **Structural**: `Contains`, `Defines`
*   **Dependency**: `Imports`, `Inherits`, `Implements`
*   **Flow**: `Calls`, `Instantiates`, `Reads`, `Writes`
*   **Contextual**: `Addresses` (Goal → File), `Explains` (Comment → Code)

## 5. API Surface

The API focuses on retrieving *Context* rather than just *Data*.

```csharp
public interface IContextGraph
{
    // --- Ingestion ---
    // Interns a concept. If alias exists, returns ID; else creates.
    Guid Intern(string alias, ConceptKind kind, object data);
    
    // Connects two concepts.
    void Link(Guid sourceId, Guid targetId, RelationKind kind, float weight = 1.0f);

    // --- Traversal ---
    // "Get me everything related to 'Login' within 2 hops of dependency"
    GraphResult Traverse(Guid startId, TraversalPolicy policy);
    
    // "Find the shortest path between 'UserRequest:FixLogin' and 'File:AuthConfig.cs'"
    PathResult ShortestPath(Guid startId, Guid endId);

    // --- Context Retrieval ---
    // "Summarize the context for this ID, staying under 2000 tokens"
    ContextSummary GetContext(Guid rootId, int maxTokens);
}
```

## 6. Token Budgeting Strategy

A unique feature of Sharc Graph is **Token Awareness**.

1.  **Cost Storage**: Every node stores its estimated token cost.
2.  **Budgeted Traversal**: When retrieving context, the traversal engine accepts a `TokenBudget`. It expands the graph using an algorithm (e.g., Spreading Activation or weighted BFS) until the budget is exhausted.
    *   Prioritizes high-weight edges.
    *   Prioritizes "closer" semantic neighbors.
    *   Prunes low-relevance branches dynamically.

## 7. Implementation Roadmap

### Phase 1: Core Graph Storage
1.  Implement `ConceptStore` (GUID generation, Alias lookup, Integer Key mapping).
2.  Implement `RelationStore` (Adjacency lists via B-Tree using Integer Keys).
3.  Basic `Traverse` method (BFS on Integer Keys).

### Phase 2: Ontology & Typing
1.  Define `ConceptKind` and `RelationKind` enums.
2.  Implement strongly-typed attributes for common code symbols.

### Phase 3: Context Query Engine
1.  Implement `TokenBudget` logic.
2.  Implement `WeightedTraversal` to prioritize important context.

## 8. Development Rules

1.  **No External Dependencies**: Use standard .NET libraries only.
2.  **Zero-Allocation Traversal**: Use `Span<T>` and `struct` numerators for graph walking.
3.  **Performance**:
    *   Node lookup by GUID: < 100ns (hot cache).
    *   Edge expansion: < 1μs per 100 edges.
4.  **Inspiration**: We borrow the *concept* of document-graph connecting from other DBs, but the implementation is purely custom, optimized for read-heavy, append-only session context.
