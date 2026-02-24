// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector;

/// <summary>Distance metric for vector similarity search.</summary>
public enum DistanceMetric
{
    /// <summary>Cosine distance (1 - cosine_similarity). Good for text embeddings.</summary>
    Cosine,

    /// <summary>Euclidean (L2) distance. Good for spatial/geometric data.</summary>
    Euclidean,

    /// <summary>Dot product (inner product). Good for normalized vectors, recommendation.</summary>
    DotProduct
}
