// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Unit tests for <see cref="TieredHashJoin"/> — the zero-allocation FULL OUTER JOIN executor.
/// Tests all three tiers (StackAlloc, Pooled, OpenAddress) via controlled build-side sizes.
/// </summary>
public sealed class TieredHashJoinTests
{
    // Helper: create a row with a single int key and a string payload
    private static QueryValue[] Row(long key, string payload) =>
        new[] { QueryValue.FromInt64(key), QueryValue.FromString(payload) };

    private static QueryValue[] NullRow(int width)
    {
        var row = new QueryValue[width];
        Array.Fill(row, QueryValue.Null);
        return row;
    }

    private static List<QueryValue[]> Collect(IEnumerable<QueryValue[]> rows)
        => rows.ToList();

    // ───── Tier I: StackAlloc (≤256 build rows) ─────

    [Fact]
    public void TierI_FullOuterJoin_UniqueKeys_AllMatched()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "b1"), Row(2, "b2"), Row(3, "b3"),
        };
        var probe = new List<QueryValue[]>
        {
            Row(1, "p1"), Row(2, "p2"), Row(3, "p3"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // All matched — 3 rows, no unmatched on either side
        Assert.Equal(3, results.Count);
        foreach (var row in results)
        {
            Assert.Equal(4, row.Length);
            Assert.False(row[0].IsNull); // probe side
            Assert.False(row[2].IsNull); // build side
        }
    }

    [Fact]
    public void TierI_FullOuterJoin_PartialMatch()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "b1"), Row(2, "b2"), Row(3, "b3"),
        };
        var probe = new List<QueryValue[]>
        {
            Row(2, "p2"), Row(4, "p4"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // 1 matched (key=2), 1 probe-unmatched (key=4), 2 build-unmatched (keys=1,3)
        Assert.Equal(4, results.Count);

        // Verify matched row: probe=left, build=right
        var matched = results.First(r => !r[0].IsNull && !r[2].IsNull);
        Assert.Equal(2L, matched[0].AsInt64());
        Assert.Equal(2L, matched[2].AsInt64());

        // Verify probe-unmatched: probe=left, null=right
        var probeUnmatched = results.Where(r => !r[0].IsNull && r[2].IsNull).ToList();
        Assert.Single(probeUnmatched);
        Assert.Equal(4L, probeUnmatched[0][0].AsInt64());

        // Verify build-unmatched: null=left, build=right
        var buildUnmatched = results.Where(r => r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(2, buildUnmatched.Count);
    }

    [Fact]
    public void TierI_FullOuterJoin_EmptyBuild()
    {
        var build = new List<QueryValue[]>();
        var probe = new List<QueryValue[]>
        {
            Row(1, "p1"), Row(2, "p2"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // All probe-unmatched
        Assert.Equal(2, results.Count);
        foreach (var row in results)
        {
            Assert.False(row[0].IsNull); // probe side present
            Assert.True(row[2].IsNull);  // build side null
        }
    }

    [Fact]
    public void TierI_FullOuterJoin_EmptyProbe()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "b1"), Row(2, "b2"),
        };
        var probe = new List<QueryValue[]>();

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // All build-unmatched
        Assert.Equal(2, results.Count);
        foreach (var row in results)
        {
            Assert.True(row[0].IsNull);  // probe side null
            Assert.False(row[2].IsNull); // build side present
        }
    }

    [Fact]
    public void TierI_FullOuterJoin_NullKeys_NeverMatch()
    {
        var build = new List<QueryValue[]>
        {
            new[] { QueryValue.Null, QueryValue.FromString("b_null") },
            Row(1, "b1"),
        };
        var probe = new List<QueryValue[]>
        {
            new[] { QueryValue.Null, QueryValue.FromString("p_null") },
            Row(1, "p1"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // key=1 matches. Both NULL key rows are unmatched.
        Assert.Equal(3, results.Count);

        // 1 matched (key=1)
        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Single(matched);
        Assert.Equal(1L, matched[0][0].AsInt64());
    }

    [Fact]
    public void TierI_FullOuterJoin_DuplicateBuildKeys()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "b1a"), Row(1, "b1b"), Row(2, "b2"),
        };
        var probe = new List<QueryValue[]>
        {
            Row(1, "p1"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // key=1: 2 matched rows (probe × 2 build), key=2: 1 build-unmatched
        Assert.Equal(3, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(2, matched.Count);
        Assert.All(matched, r => Assert.Equal(1L, r[0].AsInt64()));

        var buildUnmatched = results.Where(r => r[0].IsNull).ToList();
        Assert.Single(buildUnmatched);
        Assert.Equal(2L, buildUnmatched[0][2].AsInt64());
    }

    [Fact]
    public void TierI_FullOuterJoin_DuplicateBothSides()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "b1a"), Row(1, "b1b"),
        };
        var probe = new List<QueryValue[]>
        {
            Row(1, "p1a"), Row(1, "p1b"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // Cross product: 2 probe × 2 build = 4 matched rows
        Assert.Equal(4, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r[0].IsNull);
            Assert.False(r[2].IsNull);
        });
    }

    [Fact]
    public void TierI_FullOuterJoin_BuildIsLeft_SwapsLayout()
    {
        var build = new List<QueryValue[]>
        {
            Row(1, "build"),
        };
        var probe = new List<QueryValue[]>
        {
            Row(1, "probe"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: true));

        Assert.Single(results);
        // buildIsLeft: output = [build, probe]
        Assert.Equal("build", results[0][1].AsString());
        Assert.Equal("probe", results[0][3].AsString());
    }

    // ───── Tier II: Pooled (257–8,192 build rows) ─────

    [Fact]
    public void TierII_FullOuterJoin_PartialMatch_PooledBitArray()
    {
        // 300 build rows → Tier II (Pooled)
        var build = new List<QueryValue[]>(300);
        for (int i = 0; i < 300; i++)
            build.Add(Row(i, $"b{i}"));

        // Probe only even keys 0,2,4,...,298 → 150 matches, 150 build-unmatched
        var probe = new List<QueryValue[]>(150);
        for (int i = 0; i < 300; i += 2)
            probe.Add(Row(i, $"p{i}"));

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // 150 matched + 150 build-unmatched = 300 total
        Assert.Equal(300, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(150, matched.Count);

        var buildUnmatched = results.Where(r => r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(150, buildUnmatched.Count);
    }

    // ───── Tier III: OpenAddress (>8,192 build rows) ─────

    [Fact]
    public void TierIII_FullOuterJoin_BasicMatch_OpenAddress()
    {
        // 9000 build rows → Tier III (OpenAddress)
        var build = new List<QueryValue[]>(9000);
        for (int i = 0; i < 9000; i++)
            build.Add(Row(i, $"b{i}"));

        // Probe keys 0..99 → 100 matches, 8900 build-unmatched
        var probe = new List<QueryValue[]>(100);
        for (int i = 0; i < 100; i++)
            probe.Add(Row(i, $"p{i}"));

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // 100 matched + 8900 build-unmatched = 9000 total
        Assert.Equal(9000, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(100, matched.Count);

        var buildUnmatched = results.Where(r => r[0].IsNull).ToList();
        Assert.Equal(8900, buildUnmatched.Count);
    }

    [Fact]
    public void TierIII_FullOuterJoin_DrainThenRemove_DuplicateKeys()
    {
        // 9000 build rows, some with duplicate keys
        var build = new List<QueryValue[]>(9003);
        for (int i = 0; i < 9000; i++)
            build.Add(Row(i, $"b{i}"));
        // Add 3 more entries with key=0 (total 4 entries for key=0)
        build.Add(Row(0, "b0dup1"));
        build.Add(Row(0, "b0dup2"));
        build.Add(Row(0, "b0dup3"));

        var probe = new List<QueryValue[]>
        {
            Row(0, "p0"), Row(1, "p1"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // key=0: 4 build × 1 probe = 4 matched
        // key=1: 1 build × 1 probe = 1 matched
        // build-unmatched: 9003 - 5 = 8998
        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(5, matched.Count);

        var key0Matched = matched.Where(r => r[0].AsInt64() == 0).ToList();
        Assert.Equal(4, key0Matched.Count);

        var buildUnmatched = results.Where(r => r[0].IsNull).ToList();
        Assert.Equal(8998, buildUnmatched.Count);
    }

    [Fact]
    public void TierIII_FullOuterJoin_DisjointKeys()
    {
        var build = new List<QueryValue[]>(9000);
        for (int i = 0; i < 9000; i++)
            build.Add(Row(i, $"b{i}"));

        // All probe keys are disjoint (start at 10000)
        var probe = new List<QueryValue[]>(100);
        for (int i = 10000; i < 10100; i++)
            probe.Add(Row(i, $"p{i}"));

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // 0 matched + 100 probe-unmatched + 9000 build-unmatched
        Assert.Equal(9100, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Empty(matched);
    }

    // ───── Self-join ─────

    [Fact]
    public void TierI_SelfJoin_AllMatch()
    {
        var data = new List<QueryValue[]>
        {
            Row(1, "a"), Row(2, "b"), Row(3, "c"),
        };

        // Self-join: both build and probe are the same data
        var results = Collect(TieredHashJoin.Execute(
            data, buildKeyIndex: 0, buildColumnCount: 2,
            data, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // Each row matches itself → 3 matched, 0 unmatched
        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r[0].IsNull);
            Assert.False(r[2].IsNull);
            Assert.Equal(r[0].AsInt64(), r[2].AsInt64());
        });
    }

    // ───── Tier selection verification ─────

    [Fact]
    public void TierSelection_256Rows_UsesTierI()
    {
        var build = new List<QueryValue[]>(256);
        for (int i = 0; i < 256; i++)
            build.Add(Row(i, $"b{i}"));

        var probe = new List<QueryValue[]> { Row(0, "p0") };

        // Should not throw and produce correct results (Tier I)
        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        // 1 matched + 255 build-unmatched
        Assert.Equal(256, results.Count);
    }

    [Fact]
    public void TierSelection_257Rows_UsesTierII()
    {
        var build = new List<QueryValue[]>(257);
        for (int i = 0; i < 257; i++)
            build.Add(Row(i, $"b{i}"));

        var probe = new List<QueryValue[]> { Row(0, "p0") };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        Assert.Equal(257, results.Count);
    }

    [Fact]
    public void TierSelection_8193Rows_UsesTierIII()
    {
        var build = new List<QueryValue[]>(8193);
        for (int i = 0; i < 8193; i++)
            build.Add(Row(i, $"b{i}"));

        var probe = new List<QueryValue[]> { Row(0, "p0") };

        var results = Collect(TieredHashJoin.Execute(
            build, buildKeyIndex: 0, buildColumnCount: 2,
            probe, probeKeyIndex: 0, probeColumnCount: 2,
            buildIsLeft: false));

        Assert.Equal(8193, results.Count);
    }
}
