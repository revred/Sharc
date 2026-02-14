# Sharc.Graph

**Graph reasoning and trust layer for the Sharc database engine.**

This package enables Context Space Engineering by overlaying a high-performance graph model and a cryptographic trust layer on standard SQLite files.

## Features

- **Context Graph**: B-tree backed relationship store for fast O(log N) node and edge traversal.
- **Trust Ledger**: Cryptographically signed, hash-chained audit trails for data provenance.
- **Agent Identity**: ECDSA-based registry for attributing every data mutation to a specific agent.
- **Token Efficiency**: Designed to deliver precise, context-rich subgraphs to LLMs, reducing token waste.

## Quick Start

```csharp
using Sharc.Graph;

// Load a context graph from a Sharc database
var graph = SharcContextGraph.Create(db);

// Traverse relationships with zero-allocation cursors
var edges = graph.GetEdges(nodeKey, TraversalDirection.Outgoing);
while (edges.MoveNext())
{
    Console.WriteLine($"Found: {edges.TargetKey} (Kind: {edges.Kind})");
}

// Verify the cryptographic integrity of the data ledger
bool isTrusted = graph.Ledger.VerifyIntegrity();
```

[Full Documentation](https://github.com/revred/Sharc)
