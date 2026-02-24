// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace Sharc.Vector;

/// <summary>
/// Distance metrics for vector similarity. Delegates to <see cref="TensorPrimitives"/>
/// for SIMD-accelerated computation (Vector128/256/512, including AVX-512 when available).
/// </summary>
/// <remarks>
/// Uses <c>System.Numerics.Tensors.TensorPrimitives</c> — a Microsoft 1st-party BCL
/// package (MIT, zero transitive deps on .NET 8+). This provides battle-tested SIMD
/// implementations with explicit Vector128/256/512 intrinsics.
/// The entire distance layer is 6 lines of delegation. Microsoft maintains the SIMD.
/// </remarks>
public static class VectorDistanceFunctions
{
    /// <summary>
    /// Cosine distance = 1.0 - cosine_similarity.
    /// Range: [0, 2]. 0 = identical direction. 1 = orthogonal. 2 = opposite.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => 1f - TensorPrimitives.CosineSimilarity(a, b);

    /// <summary>
    /// Euclidean (L2) distance. Range: [0, +inf).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => TensorPrimitives.Distance(a, b);

    /// <summary>
    /// Dot product (inner product). Higher = more similar.
    /// For normalized vectors, equivalent to cosine similarity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => TensorPrimitives.Dot(a, b);

    /// <summary>Resolves a metric enum to the corresponding function delegate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static VectorDistanceFunction Resolve(DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => CosineDistance,
        DistanceMetric.Euclidean => EuclideanDistance,
        DistanceMetric.DotProduct => DotProduct,
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
}

/// <summary>Delegate for vector distance computation on spans.</summary>
internal delegate float VectorDistanceFunction(ReadOnlySpan<float> a, ReadOnlySpan<float> b);
