// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class VectorTopKHeapTests
{
    [Fact]
    public void TryInsert_LessThanK_ReturnsAll()
    {
        var heap = new VectorTopKHeap(5, isMinHeap: true);
        heap.TryInsert(1, 0.5f);
        heap.TryInsert(2, 0.3f);
        heap.TryInsert(3, 0.8f);

        var result = heap.ToResult();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void TryInsert_MoreThanK_ReturnsOnlyK()
    {
        var heap = new VectorTopKHeap(3, isMinHeap: true);
        heap.TryInsert(1, 0.9f);
        heap.TryInsert(2, 0.1f);
        heap.TryInsert(3, 0.5f);
        heap.TryInsert(4, 0.3f);
        heap.TryInsert(5, 0.7f);

        var result = heap.ToResult();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Cosine_KeepsSmallestDistances()
    {
        // isMinHeap=true: keep smallest distances (Cosine/Euclidean)
        var heap = new VectorTopKHeap(3, isMinHeap: true);
        heap.TryInsert(1, 0.9f);
        heap.TryInsert(2, 0.1f);
        heap.TryInsert(3, 0.5f);
        heap.TryInsert(4, 0.3f);
        heap.TryInsert(5, 0.2f);

        var result = heap.ToResult();
        Assert.Equal(3, result.Count);

        // Should contain the 3 smallest: 0.1, 0.2, 0.3
        Assert.Equal(0.1f, result[0].Distance);
        Assert.Equal(0.2f, result[1].Distance);
        Assert.Equal(0.3f, result[2].Distance);
    }

    [Fact]
    public void DotProduct_KeepsLargestValues()
    {
        // isMinHeap=false: keep largest values (DotProduct)
        var heap = new VectorTopKHeap(3, isMinHeap: false);
        heap.TryInsert(1, 0.1f);
        heap.TryInsert(2, 0.9f);
        heap.TryInsert(3, 0.5f);
        heap.TryInsert(4, 0.7f);
        heap.TryInsert(5, 0.8f);

        var result = heap.ToResult();
        Assert.Equal(3, result.Count);

        // Should contain the 3 largest: 0.9, 0.8, 0.7 (sorted descending)
        Assert.Equal(0.9f, result[0].Distance);
        Assert.Equal(0.8f, result[1].Distance);
        Assert.Equal(0.7f, result[2].Distance);
    }

    [Fact]
    public void ToResult_SortedByDistance_Ascending()
    {
        var heap = new VectorTopKHeap(5, isMinHeap: true);
        heap.TryInsert(1, 0.5f);
        heap.TryInsert(2, 0.1f);
        heap.TryInsert(3, 0.9f);
        heap.TryInsert(4, 0.3f);

        var result = heap.ToResult();

        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Distance <= result[i].Distance);
    }

    [Fact]
    public void ToResult_SortedByDistance_Descending()
    {
        var heap = new VectorTopKHeap(5, isMinHeap: false);
        heap.TryInsert(1, 0.5f);
        heap.TryInsert(2, 0.1f);
        heap.TryInsert(3, 0.9f);
        heap.TryInsert(4, 0.3f);

        var result = heap.ToResult();

        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Distance >= result[i].Distance);
    }

    [Fact]
    public void EmptyHeap_ReturnsEmptyResult()
    {
        var heap = new VectorTopKHeap(5, isMinHeap: true);
        var result = heap.ToResult();

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void SingleElement_ReturnsSingleResult()
    {
        var heap = new VectorTopKHeap(5, isMinHeap: true);
        heap.TryInsert(42, 0.75f);

        var result = heap.ToResult();
        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].RowId);
        Assert.Equal(0.75f, result[0].Distance);
    }

    [Fact]
    public void RowIds_PreservedCorrectly()
    {
        var heap = new VectorTopKHeap(3, isMinHeap: true);
        heap.TryInsert(100, 0.1f);
        heap.TryInsert(200, 0.2f);
        heap.TryInsert(300, 0.3f);

        var result = heap.ToResult();

        Assert.Equal(100, result[0].RowId);
        Assert.Equal(200, result[1].RowId);
        Assert.Equal(300, result[2].RowId);
    }

    [Fact]
    public void DuplicateDistances_AllRetained()
    {
        var heap = new VectorTopKHeap(3, isMinHeap: true);
        heap.TryInsert(1, 0.5f);
        heap.TryInsert(2, 0.5f);
        heap.TryInsert(3, 0.5f);

        var result = heap.ToResult();
        Assert.Equal(3, result.Count);
        Assert.All(result.Matches, m => Assert.Equal(0.5f, m.Distance));
    }

    [Fact]
    public void WorseElement_NotInsertedWhenFull()
    {
        var heap = new VectorTopKHeap(2, isMinHeap: true);
        heap.TryInsert(1, 0.1f);
        heap.TryInsert(2, 0.2f);
        heap.TryInsert(3, 0.9f); // worse than both, should be rejected

        var result = heap.ToResult();
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result.Matches, m => m.RowId == 3);
    }
}
