// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public class CandidateHeapTests
{
    [Fact]
    public void MinHeap_PopReturnsSmallest()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        heap.Push(3.0f, 30);
        heap.Push(1.0f, 10);
        heap.Push(2.0f, 20);

        var first = heap.Pop();
        Assert.Equal(1.0f, first.Distance);
        Assert.Equal(10, first.NodeIndex);

        var second = heap.Pop();
        Assert.Equal(2.0f, second.Distance);
        Assert.Equal(20, second.NodeIndex);

        var third = heap.Pop();
        Assert.Equal(3.0f, third.Distance);
        Assert.Equal(30, third.NodeIndex);

        heap.Dispose();
    }

    [Fact]
    public void MaxHeap_PopReturnsLargest()
    {
        var heap = new CandidateHeap(10, isMinHeap: false);

        heap.Push(1.0f, 10);
        heap.Push(3.0f, 30);
        heap.Push(2.0f, 20);

        var first = heap.Pop();
        Assert.Equal(3.0f, first.Distance);
        Assert.Equal(30, first.NodeIndex);

        var second = heap.Pop();
        Assert.Equal(2.0f, second.Distance);
        Assert.Equal(20, second.NodeIndex);

        heap.Dispose();
    }

    [Fact]
    public void Peek_ReturnsRootWithoutRemoving()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        heap.Push(5.0f, 50);
        heap.Push(2.0f, 20);

        var peeked = heap.Peek();
        Assert.Equal(2.0f, peeked.Distance);
        Assert.Equal(2, heap.Count);

        heap.Dispose();
    }

    [Fact]
    public void Count_TracksElements()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        Assert.Equal(0, heap.Count);
        Assert.True(heap.IsEmpty);

        heap.Push(1.0f, 1);
        Assert.Equal(1, heap.Count);
        Assert.False(heap.IsEmpty);

        heap.Push(2.0f, 2);
        Assert.Equal(2, heap.Count);

        heap.Pop();
        Assert.Equal(1, heap.Count);

        heap.Dispose();
    }

    [Fact]
    public void Pop_WhenEmpty_Throws()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        Assert.Throws<InvalidOperationException>(() => heap.Pop());

        heap.Dispose();
    }

    [Fact]
    public void Peek_WhenEmpty_Throws()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        Assert.Throws<InvalidOperationException>(() => heap.Peek());

        heap.Dispose();
    }

    [Fact]
    public void Push_WhenFull_Throws()
    {
        var heap = new CandidateHeap(2, isMinHeap: true);

        heap.Push(1.0f, 1);
        heap.Push(2.0f, 2);

        Assert.Throws<InvalidOperationException>(() => heap.Push(3.0f, 3));

        heap.Dispose();
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);

        heap.Push(1.0f, 1);
        heap.Push(2.0f, 2);
        heap.Clear();

        Assert.Equal(0, heap.Count);
        Assert.True(heap.IsEmpty);

        heap.Dispose();
    }

    [Fact]
    public void MinHeap_ManyElements_CorrectOrder()
    {
        var heap = new CandidateHeap(8, isMinHeap: true);

        heap.Push(7.0f, 7);
        heap.Push(3.0f, 3);
        heap.Push(5.0f, 5);
        heap.Push(1.0f, 1);
        heap.Push(6.0f, 6);
        heap.Push(2.0f, 2);
        heap.Push(4.0f, 4);
        heap.Push(8.0f, 8);

        float prev = float.MinValue;
        while (!heap.IsEmpty)
        {
            var item = heap.Pop();
            Assert.True(item.Distance >= prev,
                $"Out of order: {item.Distance} < {prev}");
            prev = item.Distance;
        }

        heap.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var heap = new CandidateHeap(10, isMinHeap: true);
        heap.Push(1.0f, 1);
        heap.Dispose();
        heap.Dispose(); // should not throw
    }
}
