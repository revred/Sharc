# Vector Search

Sharc.Vector provides SIMD-accelerated vector similarity search with zero-copy metadata pre-filtering.

## Installation

```bash
dotnet add package Sharc.Vector
```

## Storing Embeddings

```csharp
using Sharc.Vector;

// Create a vector index
var index = new HnswIndex<float>(dimensions: 384, maxElements: 10000);

// Add vectors with metadata IDs
index.Add(id: 1, embedding: new float[] { 0.1f, 0.2f, ... });
index.Add(id: 2, embedding: new float[] { 0.3f, 0.4f, ... });
```

## Similarity Search

```csharp
// Find k nearest neighbors
var results = index.Search(queryVector, k: 10);

foreach (var (id, distance) in results)
    Console.WriteLine($"ID: {id}, Distance: {distance}");
```

## Distance Metrics

| Metric | Use Case |
|:---|:---|
| Cosine | Text embeddings, normalized vectors |
| Euclidean | Spatial data, image features |
| DotProduct | Pre-normalized vectors, maximum inner product |

## Integration with Sharc

```csharp
// Store embeddings alongside relational data
using var db = SharcDatabase.Open("knowledge.db");
using var reader = db.CreateReader("documents");

// Use vector search results to look up full documents
var nearest = index.Search(queryEmbedding, k: 5);
foreach (var (id, _) in nearest)
{
    using var doc = db.PrepareReader("documents");
    if (doc.Seek(id))
        Console.WriteLine(doc.GetString(1)); // document text
}
```

## See Also

- [docs/VECTOR_SEARCH.md](../docs/VECTOR_SEARCH.md) — Full vector search guide with RAG patterns
- [Performance Guide](Performance-Guide) — Benchmark results
- [AI Agent Reference](AI-Agent-Reference) — Agent memory patterns
