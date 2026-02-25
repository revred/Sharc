// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// In-memory vector resolver. Used during index construction (when all vectors
/// are already loaded) and in unit tests.
/// </summary>
internal sealed class MemoryVectorResolver : IVectorResolver
{
    private readonly float[][] _vectors;

    internal MemoryVectorResolver(float[][] vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        _vectors = vectors;
        if (vectors.Length == 0)
        {
            Dimensions = 0;
            return;
        }

        if (vectors[0] == null)
            throw new ArgumentException("Vector 0 is null.", nameof(vectors));

        Dimensions = vectors[0].Length;

        for (int i = 1; i < vectors.Length; i++)
        {
            if (vectors[i] == null)
                throw new ArgumentException($"Vector {i} is null.", nameof(vectors));
            if (vectors[i].Length != Dimensions)
            {
                throw new ArgumentException(
                    $"Vector {i} has dimension {vectors[i].Length}, expected {Dimensions}.",
                    nameof(vectors));
            }
        }
    }

    public ReadOnlySpan<float> GetVector(int nodeIndex) => _vectors[nodeIndex];

    public int Dimensions { get; }
}
