// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

public class TopNHeapTests
{
    private static QueryValue[] Row(long val) => [QueryValue.FromInt64(val)];
    private static QueryValue[] Row(long a, string b) => [QueryValue.FromInt64(a), QueryValue.FromString(b)];

    // Ascending comparison: "worst" = largest (so root holds the max, evicted first)
    private static int AscWorstFirst(QueryValue[] a, QueryValue[] b) =>
        QueryPostProcessor.CompareValues(a[0], b[0]);

    // Descending comparison: "worst" = smallest
    private static int DescWorstFirst(QueryValue[] a, QueryValue[] b) =>
        QueryPostProcessor.CompareValues(b[0], a[0]);

    // ─── Basic insertion ──────────────────────────────────────────

    [Fact]
    public void TryInsert_LessThanCapacity_AllRetained()
    {
        var heap = new TopNHeap(5, AscWorstFirst);
        heap.TryInsert(Row(3));
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(2));

        Assert.Equal(3, heap.Count);
        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void TryInsert_ExactlyCapacity_AllRetained()
    {
        var heap = new TopNHeap(3, AscWorstFirst);
        heap.TryInsert(Row(30));
        heap.TryInsert(Row(10));
        heap.TryInsert(Row(20));

        Assert.Equal(3, heap.Count);
    }

    [Fact]
    public void TryInsert_MoreThanCapacity_EvictsWorst()
    {
        var heap = new TopNHeap(3, AscWorstFirst);
        heap.TryInsert(Row(50));
        heap.TryInsert(Row(30));
        heap.TryInsert(Row(10));
        heap.TryInsert(Row(40));
        heap.TryInsert(Row(20));

        Assert.Equal(3, heap.Count);
        var sorted = heap.ExtractSorted();
        // Keep top-3 smallest: 10, 20, 30
        Assert.Equal(10L, sorted[0][0].AsInt64());
        Assert.Equal(20L, sorted[1][0].AsInt64());
        Assert.Equal(30L, sorted[2][0].AsInt64());
    }

    // ─── Sorted extraction ────────────────────────────────────────

    [Fact]
    public void ExtractSorted_ReturnsAscendingOrder()
    {
        var heap = new TopNHeap(5, AscWorstFirst);
        heap.TryInsert(Row(5));
        heap.TryInsert(Row(3));
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(4));
        heap.TryInsert(Row(2));

        var sorted = heap.ExtractSorted();
        Assert.Equal(5, sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
            Assert.Equal((long)(i + 1), sorted[i][0].AsInt64());
    }

    [Fact]
    public void ExtractSorted_WithEviction_ReturnsAscendingOrder()
    {
        var heap = new TopNHeap(3, AscWorstFirst);
        for (long i = 10; i >= 1; i--)
            heap.TryInsert(Row(i));

        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
        Assert.Equal(1L, sorted[0][0].AsInt64());
        Assert.Equal(2L, sorted[1][0].AsInt64());
        Assert.Equal(3L, sorted[2][0].AsInt64());
    }

    // ─── Descending sort ──────────────────────────────────────────

    [Fact]
    public void DescendingSort_KeepsLargest()
    {
        var heap = new TopNHeap(3, DescWorstFirst);
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(5));
        heap.TryInsert(Row(3));
        heap.TryInsert(Row(4));
        heap.TryInsert(Row(2));

        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
        // Descending: 5, 4, 3
        Assert.Equal(5L, sorted[0][0].AsInt64());
        Assert.Equal(4L, sorted[1][0].AsInt64());
        Assert.Equal(3L, sorted[2][0].AsInt64());
    }

    // ─── Multi-column sort ────────────────────────────────────────

    [Fact]
    public void MultiColumnSort_CorrectOrder()
    {
        // Sort by col0 ASC, col1 ASC
        static int Cmp(QueryValue[] a, QueryValue[] b)
        {
            int c = QueryPostProcessor.CompareValues(a[0], b[0]);
            if (c != 0) return c;
            return QueryPostProcessor.CompareValues(a[1], b[1]);
        }

        var heap = new TopNHeap(3, Cmp);
        heap.TryInsert(Row(2, "b"));
        heap.TryInsert(Row(1, "z"));
        heap.TryInsert(Row(1, "a"));
        heap.TryInsert(Row(3, "x"));
        heap.TryInsert(Row(2, "a"));

        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
        Assert.Equal(1L, sorted[0][0].AsInt64());
        Assert.Equal("a", sorted[0][1].AsString());
        Assert.Equal(1L, sorted[1][0].AsInt64());
        Assert.Equal("z", sorted[1][1].AsString());
        Assert.Equal(2L, sorted[2][0].AsInt64());
        Assert.Equal("a", sorted[2][1].AsString());
    }

    // ─── NULL handling ────────────────────────────────────────────

    [Fact]
    public void NullValues_SortLast()
    {
        var heap = new TopNHeap(3, AscWorstFirst);
        heap.TryInsert([QueryValue.Null]);
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(2));
        heap.TryInsert(Row(3));

        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
        // Nulls sort last in CompareValues, so they're "worst" and evicted first
        // keeping 1, 2, 3
        Assert.Equal(1L, sorted[0][0].AsInt64());
        Assert.Equal(2L, sorted[1][0].AsInt64());
        Assert.Equal(3L, sorted[2][0].AsInt64());
    }

    // ─── Edge cases ───────────────────────────────────────────────

    [Fact]
    public void SingleElement_Works()
    {
        var heap = new TopNHeap(1, AscWorstFirst);
        heap.TryInsert(Row(5));
        heap.TryInsert(Row(3));
        heap.TryInsert(Row(1));

        var sorted = heap.ExtractSorted();
        Assert.Single(sorted);
        Assert.Equal(1L, sorted[0][0].AsInt64());
    }

    [Fact]
    public void DuplicateValues_AllRetained()
    {
        var heap = new TopNHeap(3, AscWorstFirst);
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(1));
        heap.TryInsert(Row(2));

        var sorted = heap.ExtractSorted();
        Assert.Equal(3, sorted.Count);
        Assert.Equal(1L, sorted[0][0].AsInt64());
        Assert.Equal(1L, sorted[1][0].AsInt64());
        Assert.Equal(1L, sorted[2][0].AsInt64());
    }

    [Fact]
    public void EmptyHeap_ExtractSorted_ReturnsEmpty()
    {
        var heap = new TopNHeap(5, AscWorstFirst);
        var sorted = heap.ExtractSorted();
        Assert.Empty(sorted);
    }
}
