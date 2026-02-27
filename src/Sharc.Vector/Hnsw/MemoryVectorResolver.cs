// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Vector.Hnsw;

/// <summary>
/// In-memory vector resolver backed by a single contiguous float array.
/// Vectors are packed at construction time for cache-friendly traversal
/// during HNSW graph search.
/// </summary>
internal sealed class MemoryVectorResolver : IVectorResolver
{
    private readonly float[] _packed;
    private readonly int _dimensions;

    internal MemoryVectorResolver(float[][] vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Length == 0)
        {
            _packed = [];
            _dimensions = 0;
            return;
        }

        if (vectors[0] == null)
            throw new ArgumentException("Vector 0 is null.", nameof(vectors));

        _dimensions = vectors[0].Length;

        for (int i = 1; i < vectors.Length; i++)
        {
            if (vectors[i] == null)
                throw new ArgumentException($"Vector {i} is null.", nameof(vectors));
            if (vectors[i].Length != _dimensions)
            {
                throw new ArgumentException(
                    $"Vector {i} has dimension {vectors[i].Length}, expected {_dimensions}.",
                    nameof(vectors));
            }
        }

        _packed = new float[vectors.Length * _dimensions];
        for (int i = 0; i < vectors.Length; i++)
            vectors[i].CopyTo(_packed.AsSpan(i * _dimensions, _dimensions));
    }

    public ReadOnlySpan<float> GetVector(int nodeIndex)
        => _packed.AsSpan(nodeIndex * _dimensions, _dimensions);

    public int Dimensions => _dimensions;
}
