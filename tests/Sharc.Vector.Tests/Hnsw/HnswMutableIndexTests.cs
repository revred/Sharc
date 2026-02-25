// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector.Hnsw;
using Xunit;

namespace Sharc.Vector.Tests.Hnsw;

public sealed class HnswMutableIndexTests
{
    [Fact]
    public void Upsert_NewRow_IsSearchableBeforeMerge()
    {
        var vectors = new[]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f }
        };
        var rowIds = new long[] { 10, 20 };

        using var index = HnswIndex.BuildFromMemory(
            vectors,
            rowIds,
            DistanceMetric.Euclidean,
            HnswConfig.Default with { Seed = 7 });

        index.Upsert(99, new float[] { 0.99f, 0.01f });

        var result = index.Search(new float[] { 0.98f, 0.02f }, k: 1);

        Assert.True(index.HasPendingMutations);
        Assert.Equal(99L, result[0].RowId);
    }

    [Fact]
    public void Delete_TombstonesBaseRow_WithoutMerge()
    {
        var vectors = new[]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f }
        };
        var rowIds = new long[] { 10, 20 };

        using var index = HnswIndex.BuildFromMemory(
            vectors,
            rowIds,
            DistanceMetric.Euclidean,
            HnswConfig.Default with { Seed = 11 });

        Assert.True(index.Delete(10));

        var result = index.Search(new float[] { 1f, 0f }, k: 2);

        Assert.DoesNotContain(result.Matches, m => m.RowId == 10);
        Assert.True(index.HasPendingMutations);
    }

    [Fact]
    public void MergePendingMutations_CompactsDelta_AndClearsPendingState()
    {
        var vectors = new[]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f },
            new[] { -1f, 0f }
        };
        var rowIds = new long[] { 10, 20, 30 };

        using var index = HnswIndex.BuildFromMemory(
            vectors,
            rowIds,
            DistanceMetric.Euclidean,
            HnswConfig.Default with { Seed = 17 });

        index.Upsert(40, new float[] { 0.95f, 0.05f });
        index.Delete(30);

        index.MergePendingMutations();

        Assert.False(index.HasPendingMutations);
        Assert.Equal(3, index.Count); // 3 base rows after delete(30) + insert(40)

        var result = index.Search(new float[] { 1f, 0f }, k: 3);
        Assert.Contains(result.Matches, m => m.RowId == 40);
        Assert.DoesNotContain(result.Matches, m => m.RowId == 30);
    }

    [Fact]
    public void Snapshot_VersionAndChecksum_TrackMutationLifecycle()
    {
        var vectors = new[]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f }
        };
        var rowIds = new long[] { 10, 20 };

        using var index = HnswIndex.BuildFromMemory(
            vectors,
            rowIds,
            DistanceMetric.Euclidean,
            HnswConfig.Default with { Seed = 19 });

        var before = index.GetSnapshot();
        index.Upsert(30, new float[] { 0.9f, 0.1f });
        var withDelta = index.GetSnapshot();

        Assert.True(withDelta.Version > before.Version);
        Assert.True(withDelta.PendingUpsertCount > 0);
        Assert.NotEqual(before.Checksum, withDelta.Checksum);

        index.MergePendingMutations();
        var merged = index.GetSnapshot();

        Assert.True(merged.Version > withDelta.Version);
        Assert.Equal(0, merged.PendingUpsertCount);
        Assert.Equal(0, merged.PendingDeleteCount);
        Assert.False(merged.HasPendingMutations);
        Assert.Equal(3, merged.ActiveNodeCount);
    }
}
