# Sharc.Graph.Surface

**Graph interfaces and models for the Sharc database engine.**

Shared contracts, schema types, and traversal model definitions used by `Sharc.Graph` and downstream consumers. Pure C#, zero native dependencies.

## Features

- **IContextGraph**: Core graph query interface for traversal and node lookup.
- **IEdgeCursor**: Zero-allocation cursor interface for iterating edges.
- **Schema Types**: `ConceptKind`, `RelationKind`, and schema adapter abstractions.
- **Traversal Models**: `TraversalPolicy`, `TraversalResult`, and direction types.
- **Graph Records**: `GraphNode`, `GraphEdge`, `NodeKey`, and `RecordId` value types.

## Note

This is an interface package. Most users should reference [`Sharc.Graph`](https://www.nuget.org/packages/Sharc.Graph) instead, which provides the full graph engine implementation.

[Full Documentation](https://github.com/revred/Sharc)
