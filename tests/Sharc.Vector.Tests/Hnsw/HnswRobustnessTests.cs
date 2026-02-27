// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

/// <summary>
/// Defensive tests for hardened HNSW APIs: AddNode duplicate guard, UpdateVector
/// bounds checks, MergePendingMutations lock safety, and Compact edge cases.
/// </summary>
public sealed class HnswRobustnessTests
{
    private static HnswIndex BuildSmallIndex(int count = 3)
    {
        var vectors = new float[count][];
        var rowIds = new long[count];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new[] { (float)i, 1f - (float)i / count };
            rowIds[i] = (i + 1) * 10L;
        }
        return HnswIndex.BuildFromMemory(vectors, rowIds, DistanceMetric.Euclidean,
            HnswConfig.Default with { Seed = 42 });
    }

    // ── AddNode duplicate rowId ──────────────────────────────────

    [Fact]
    public void AddNode_DuplicateRowId_ThrowsInvalidOperation()
    {
        var graph = new HnswGraph(2, 1);
        graph.SetRowId(0, 100L);
        graph.SetLevel(0, 0);
        graph.SetRowId(1, 200L);
        graph.SetLevel(1, 0);

        var ex = Assert.Throws<InvalidOperationException>(() => graph.AddNode(100L, 0));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void AddNode_NegativeLevel_ThrowsArgumentOutOfRange()
    {
        var graph = new HnswGraph(1, 1);
        graph.SetRowId(0, 100L);
        graph.SetLevel(0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => graph.AddNode(999L, -1));
    }

    [Fact]
    public void AddNode_NewRowId_Succeeds()
    {
        var graph = new HnswGraph(1, 1);
        graph.SetRowId(0, 100L);
        graph.SetLevel(0, 0);

        int idx = graph.AddNode(200L, 0);
        Assert.Equal(1, idx);
        Assert.Equal(2, graph.NodeCount);
        Assert.Equal(200L, graph.GetRowId(idx));
    }

    // ── MemoryVectorResolver bounds ──────────────────────────────

    [Fact]
    public void UpdateVector_NegativeIndex_ThrowsArgumentOutOfRange()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.UpdateVector(-1, new[] { 3f, 4f }));
    }

    [Fact]
    public void UpdateVector_IndexEqualToCount_ThrowsArgumentOutOfRange()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.UpdateVector(1, new[] { 3f, 4f }));
    }

    [Fact]
    public void UpdateVector_IndexBeyondCount_ThrowsArgumentOutOfRange()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.UpdateVector(999, new[] { 3f, 4f }));
    }

    [Fact]
    public void UpdateVector_NullVector_ThrowsArgumentNull()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentNullException>(() =>
            resolver.UpdateVector(0, null!));
    }

    [Fact]
    public void UpdateVector_WrongDimensions_ThrowsArgumentException()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentException>(() =>
            resolver.UpdateVector(0, new[] { 1f, 2f, 3f }));
    }

    [Fact]
    public void UpdateVector_ValidIndex_Succeeds()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });
        resolver.UpdateVector(0, new[] { 3f, 4f });

        var vec = resolver.GetVector(0);
        Assert.Equal(3f, vec[0]);
        Assert.Equal(4f, vec[1]);
    }

    [Fact]
    public void AppendVector_NullVector_ThrowsArgumentNull()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentNullException>(() => resolver.AppendVector(null!));
    }

    [Fact]
    public void AppendVector_WrongDimensions_ThrowsArgumentException()
    {
        var resolver = new MemoryVectorResolver(new[] { new[] { 1f, 2f } });

        Assert.Throws<ArgumentException>(() => resolver.AppendVector(new[] { 1f }));
    }

    // ── MergePendingMutations / Compact edge cases ──────────────

    [Fact]
    public void MergePendingMutations_NoPendingChanges_IsNoOp()
    {
        using var index = BuildSmallIndex();
        long versionBefore = index.Version;

        index.MergePendingMutations();

        Assert.Equal(versionBefore, index.Version);
        Assert.False(index.HasPendingMutations);
    }

    [Fact]
    public void MergePendingMutations_OnlyUpdates_PreservesNodeCount()
    {
        using var index = BuildSmallIndex(3);
        int countBefore = index.Count;

        index.Upsert(10, new[] { 99f, 99f });
        index.MergePendingMutations();

        Assert.Equal(countBefore, index.Count);
        Assert.False(index.HasPendingMutations);
    }

    [Fact]
    public void MergePendingMutations_WithTombstones_DelegatesToCompact()
    {
        using var index = BuildSmallIndex(3);

        index.Upsert(40, new[] { 0.5f, 0.5f });
        index.Delete(10);

        index.MergePendingMutations();

        Assert.False(index.HasPendingMutations);
        Assert.Equal(3, index.Count); // 3 original - 1 deleted + 1 new = 3
    }

    [Fact]
    public void Compact_AllDeletedExceptDelta_Succeeds()
    {
        using var index = BuildSmallIndex(2);

        index.Delete(10);
        index.Delete(20);
        index.Upsert(99, new[] { 1f, 1f });

        index.Compact();

        Assert.Equal(1, index.Count);
        Assert.False(index.HasPendingMutations);
    }

    [Fact]
    public void Compact_AllDeletedNoDelta_ThrowsInvalidOperation()
    {
        using var index = BuildSmallIndex(2);

        index.Delete(10);
        index.Delete(20);

        Assert.Throws<InvalidOperationException>(() => index.Compact());
    }

    [Fact]
    public void MergePendingMutations_DisposedIndex_ThrowsObjectDisposed()
    {
        var index = BuildSmallIndex(2);
        index.Dispose();

        Assert.Throws<ObjectDisposedException>(() => index.MergePendingMutations());
    }

    [Fact]
    public void Compact_DisposedIndex_ThrowsObjectDisposed()
    {
        var index = BuildSmallIndex(2);
        index.Dispose();

        Assert.Throws<ObjectDisposedException>(() => index.Compact());
    }

    // ── Search after hardened operations ─────────────────────────

    [Fact]
    public void Search_AfterIncrementalMerge_FindsNewNodes()
    {
        using var index = BuildSmallIndex(3);

        // Insert a node very close to query
        index.Upsert(99, new[] { 100f, 0f });
        index.MergePendingMutations();

        var result = index.Search(new float[] { 100f, 0f }, k: 1);
        Assert.Equal(99L, result[0].RowId);
    }

    [Fact]
    public void Search_AfterCompact_FindsRemainingNodes()
    {
        using var index = BuildSmallIndex(3);

        index.Delete(10);
        index.Compact();

        var result = index.Search(new float[] { 1f, 0.5f }, k: 2);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result.Matches, m => m.RowId == 10);
    }
}
