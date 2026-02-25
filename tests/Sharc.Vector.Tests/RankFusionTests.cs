// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Vector;
using Xunit;

namespace Sharc.Vector.Tests;

public sealed class RankFusionTests
{
    // ── ComputeScore ────────────────────────────────────────

    [Fact]
    public void ComputeScore_SingleRank_ReturnsExpected()
    {
        Span<int> ranks = stackalloc int[] { 1 };
        float score = RankFusion.ComputeScore(ranks, k: 60);
        Assert.Equal(1.0f / 61, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_TwoRanks_SumsCorrectly()
    {
        Span<int> ranks = stackalloc int[] { 1, 1 };
        float score = RankFusion.ComputeScore(ranks, k: 60);
        Assert.Equal(2.0f / 61, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_UnrankedSentinel_ContributesZero()
    {
        Span<int> ranks = stackalloc int[] { 1, RankFusion.UnrankedSentinel };
        float score = RankFusion.ComputeScore(ranks, k: 60);
        Assert.Equal(1.0f / 61, score, precision: 5);
    }

    [Fact]
    public void ComputeScore_AllUnranked_ReturnsZero()
    {
        Span<int> ranks = stackalloc int[] { RankFusion.UnrankedSentinel, RankFusion.UnrankedSentinel };
        float score = RankFusion.ComputeScore(ranks, k: 60);
        Assert.Equal(0f, score);
    }

    [Fact]
    public void ComputeScore_HighRank_LowContribution()
    {
        Span<int> ranks = stackalloc int[] { 100 };
        float score = RankFusion.ComputeScore(ranks, k: 60);
        Assert.Equal(1.0f / 160, score, precision: 5);
    }

    // ── Fuse ────────────────────────────────────────────────

    [Fact]
    public void Fuse_BothListsOverlap_FusesCorrectly()
    {
        var vectorRanked = new Dictionary<long, int> { [10] = 1, [20] = 2 };
        var textRanked = new Dictionary<long, int> { [10] = 2, [20] = 1 };

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 2);

        Assert.Equal(2, fused.Count);
        // Both rows appear in both lists with symmetric ranks, so equal RRF scores
        // Tie-break: lower vector rank wins, so row 10 (vectorRank=1) before row 20 (vectorRank=2)
        Assert.Equal(10, fused[0].RowId);
        Assert.Equal(20, fused[1].RowId);
        Assert.Equal(fused[0].Score, fused[1].Score, precision: 5);
    }

    [Fact]
    public void Fuse_DisjointLists_BothContributed()
    {
        var vectorRanked = new Dictionary<long, int> { [10] = 1, [20] = 2 };
        var textRanked = new Dictionary<long, int> { [30] = 1, [40] = 2 };

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 4);

        Assert.Equal(4, fused.Count);
        var rowIds = fused.Select(f => f.RowId).ToHashSet();
        Assert.Contains(10L, rowIds);
        Assert.Contains(20L, rowIds);
        Assert.Contains(30L, rowIds);
        Assert.Contains(40L, rowIds);
    }

    [Fact]
    public void Fuse_EmptyVectorList_ReturnsTextOnly()
    {
        var vectorRanked = new Dictionary<long, int>();
        var textRanked = new Dictionary<long, int> { [10] = 1, [20] = 2 };

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 2);

        Assert.Equal(2, fused.Count);
        Assert.Equal(10, fused[0].RowId); // text rank 1 = higher score
        Assert.Equal(20, fused[1].RowId);
        // All vector ranks should be unranked sentinel
        Assert.Equal(RankFusion.UnrankedSentinel, fused[0].VectorRank);
        Assert.Equal(RankFusion.UnrankedSentinel, fused[1].VectorRank);
    }

    [Fact]
    public void Fuse_EmptyTextList_ReturnsVectorOnly()
    {
        var vectorRanked = new Dictionary<long, int> { [10] = 1, [20] = 2 };
        var textRanked = new Dictionary<long, int>();

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 2);

        Assert.Equal(2, fused.Count);
        Assert.Equal(10, fused[0].RowId);
        Assert.Equal(20, fused[1].RowId);
        Assert.Equal(RankFusion.UnrankedSentinel, fused[0].TextRank);
        Assert.Equal(RankFusion.UnrankedSentinel, fused[1].TextRank);
    }

    [Fact]
    public void Fuse_BothEmpty_ReturnsEmpty()
    {
        var vectorRanked = new Dictionary<long, int>();
        var textRanked = new Dictionary<long, int>();

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 10);

        Assert.Empty(fused);
    }

    [Fact]
    public void Fuse_TopK_LimitApplied()
    {
        var vectorRanked = new Dictionary<long, int>();
        var textRanked = new Dictionary<long, int>();
        for (int i = 1; i <= 10; i++)
        {
            vectorRanked[i] = i;
            textRanked[i] = i;
        }

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 3);

        Assert.Equal(3, fused.Count);
    }

    [Fact]
    public void Fuse_TieBreaking_PrefersBetterVectorRank()
    {
        // Row 10: vectorRank=1, textRank=3 -> RRF = 1/61 + 1/63
        // Row 20: vectorRank=3, textRank=1 -> RRF = 1/63 + 1/61
        // Same total score. Tie-break: lower vector rank wins -> row 10 first
        var vectorRanked = new Dictionary<long, int> { [10] = 1, [20] = 3 };
        var textRanked = new Dictionary<long, int> { [10] = 3, [20] = 1 };

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 2);

        Assert.Equal(2, fused.Count);
        Assert.Equal(10, fused[0].RowId);
        Assert.Equal(20, fused[1].RowId);
    }

    [Fact]
    public void Fuse_TopKLargerThanCandidates_ReturnsAll()
    {
        var vectorRanked = new Dictionary<long, int> { [10] = 1, [20] = 2 };
        var textRanked = new Dictionary<long, int> { [10] = 1 };

        var fused = RankFusion.Fuse(vectorRanked, textRanked, topK: 100);

        Assert.Equal(2, fused.Count);
    }
}
