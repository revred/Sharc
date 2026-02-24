# Sharc.Vector

SIMD-accelerated vector similarity search for [Sharc](https://github.com/revred/Sharc).

## Features

- **Zero-copy BLOB decode**: `MemoryMarshal.Cast<byte, float>` directly on cached page buffers
- **SIMD distance**: Cosine, Euclidean, and Dot Product via `TensorPrimitives` (AVX-512 when available)
- **Top-K nearest neighbor**: Fixed-capacity heap selection
- **Metadata pre-filtering**: Apply WHERE filters before distance computation
- **JitQuery integration**: Reuses Sharc's pre-compiled query handles

## Quick Start

```csharp
using Sharc;
using Sharc.Vector;

using var db = SharcDatabase.Open("knowledge.db");

// Create a reusable vector search handle
using var vq = db.Vector("documents", "embedding", DistanceMetric.Cosine);

// Optional: metadata pre-filter (applied before distance computation)
vq.Where(FilterStar.Column("category").Eq("science"));

// Find the 10 nearest neighbors
float[] queryVector = GetEmbedding("How do quantum computers work?");
var results = vq.NearestTo(queryVector, k: 10);

foreach (var match in results.Matches)
    Console.WriteLine($"Row {match.RowId}: distance={match.Distance:F4}");
```

## Store Vectors

Vectors are stored as BLOB columns in regular SQLite tables:

```csharp
byte[] vectorBlob = BlobVectorCodec.Encode(embeddingModel.Encode("Hello world"));
// Store vectorBlob as a BLOB column value via SharcWriter
```

## Performance

| Operation | Allocation | Notes |
| :--- | ---: | :--- |
| Per-row distance | **0 B** | Zero-copy BLOB → float reinterpret |
| 10K vectors (384-dim) | ~5-10 ms | TensorPrimitives SIMD |
| 100K vectors (384-dim) | ~50-100 ms | Linear scan baseline |

## Requirements

- .NET 8.0+
- `System.Numerics.Tensors` (included automatically)
