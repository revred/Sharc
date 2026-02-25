// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// Resolves a vector by node index. Implementations may load from memory (build/test)
/// or from disk via PreparedReader (search).
/// </summary>
internal interface IVectorResolver
{
    /// <summary>Gets the vector for the given node index.</summary>
    ReadOnlySpan<float> GetVector(int nodeIndex);

    /// <summary>Number of dimensions per vector.</summary>
    int Dimensions { get; }
}
