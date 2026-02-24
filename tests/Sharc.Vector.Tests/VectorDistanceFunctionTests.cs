// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public class VectorDistanceFunctionTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void CosineDistance_IdenticalVectors_ReturnsZero()
    {
        float[] a = [1.0f, 2.0f, 3.0f];
        float distance = VectorDistanceFunctions.CosineDistance(a, a);

        Assert.InRange(distance, -Tolerance, Tolerance);
    }

    [Fact]
    public void CosineDistance_OrthogonalVectors_ReturnsOne()
    {
        float[] a = [1.0f, 0.0f];
        float[] b = [0.0f, 1.0f];
        float distance = VectorDistanceFunctions.CosineDistance(a, b);

        Assert.InRange(distance, 1.0f - Tolerance, 1.0f + Tolerance);
    }

    [Fact]
    public void CosineDistance_OppositeVectors_ReturnsTwo()
    {
        float[] a = [1.0f, 0.0f];
        float[] b = [-1.0f, 0.0f];
        float distance = VectorDistanceFunctions.CosineDistance(a, b);

        Assert.InRange(distance, 2.0f - Tolerance, 2.0f + Tolerance);
    }

    [Fact]
    public void CosineDistance_ScaledVersions_ReturnsZero()
    {
        float[] a = [1.0f, 2.0f, 3.0f];
        float[] b = [2.0f, 4.0f, 6.0f];
        float distance = VectorDistanceFunctions.CosineDistance(a, b);

        Assert.InRange(distance, -Tolerance, Tolerance);
    }

    [Fact]
    public void EuclideanDistance_IdenticalVectors_ReturnsZero()
    {
        float[] a = [1.0f, 2.0f, 3.0f];
        float distance = VectorDistanceFunctions.EuclideanDistance(a, a);

        Assert.InRange(distance, -Tolerance, Tolerance);
    }

    [Fact]
    public void EuclideanDistance_KnownPair_ReturnsExpected()
    {
        float[] a = [0.0f, 0.0f];
        float[] b = [3.0f, 4.0f];
        float distance = VectorDistanceFunctions.EuclideanDistance(a, b);

        Assert.InRange(distance, 5.0f - Tolerance, 5.0f + Tolerance);
    }

    [Fact]
    public void EuclideanDistance_UnitDifference_ReturnsOne()
    {
        float[] a = [0.0f];
        float[] b = [1.0f];
        float distance = VectorDistanceFunctions.EuclideanDistance(a, b);

        Assert.InRange(distance, 1.0f - Tolerance, 1.0f + Tolerance);
    }

    [Fact]
    public void DotProduct_IdenticalNormalized_ReturnsOne()
    {
        float norm = 1.0f / MathF.Sqrt(3.0f);
        float[] a = [norm, norm, norm];
        float result = VectorDistanceFunctions.DotProduct(a, a);

        Assert.InRange(result, 1.0f - Tolerance, 1.0f + Tolerance);
    }

    [Fact]
    public void DotProduct_Orthogonal_ReturnsZero()
    {
        float[] a = [1.0f, 0.0f];
        float[] b = [0.0f, 1.0f];
        float result = VectorDistanceFunctions.DotProduct(a, b);

        Assert.InRange(result, -Tolerance, Tolerance);
    }

    [Fact]
    public void DotProduct_KnownPair_ReturnsExpected()
    {
        float[] a = [1.0f, 2.0f, 3.0f];
        float[] b = [4.0f, 5.0f, 6.0f];
        float result = VectorDistanceFunctions.DotProduct(a, b);

        // 1*4 + 2*5 + 3*6 = 32
        Assert.InRange(result, 32.0f - Tolerance, 32.0f + Tolerance);
    }

    [Fact]
    public void Resolve_Cosine_ReturnsCosineFunction()
    {
        var fn = VectorDistanceFunctions.Resolve(DistanceMetric.Cosine);
        float[] a = [1.0f, 0.0f];
        float[] b = [0.0f, 1.0f];

        float result = fn(a, b);
        Assert.InRange(result, 1.0f - Tolerance, 1.0f + Tolerance);
    }

    [Fact]
    public void Resolve_Euclidean_ReturnsEuclideanFunction()
    {
        var fn = VectorDistanceFunctions.Resolve(DistanceMetric.Euclidean);
        float[] a = [0.0f, 0.0f];
        float[] b = [3.0f, 4.0f];

        float result = fn(a, b);
        Assert.InRange(result, 5.0f - Tolerance, 5.0f + Tolerance);
    }

    [Fact]
    public void Resolve_DotProduct_ReturnsDotFunction()
    {
        var fn = VectorDistanceFunctions.Resolve(DistanceMetric.DotProduct);
        float[] a = [1.0f, 2.0f];
        float[] b = [3.0f, 4.0f];

        float result = fn(a, b);
        Assert.InRange(result, 11.0f - Tolerance, 11.0f + Tolerance);
    }

    [Fact]
    public void Resolve_InvalidMetric_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VectorDistanceFunctions.Resolve((DistanceMetric)99));
    }
}
