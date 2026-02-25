// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Sharc.Query.Execution;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Table 8 correctness matrix from the ZeroAllocHashJoin paper.
/// Each scenario is parameterized across all 3 tiers:
///   Tier I (≤256), Tier II (300), Tier III (9000).
/// </summary>
public sealed class TieredHashJoinCorrectnessMatrixTests
{
    private static QueryValue[] Row(long key, string payload) =>
        new[] { QueryValue.FromInt64(key), QueryValue.FromString(payload) };

    private static List<QueryValue[]> BuildN(int n, int keyStart = 0)
    {
        var rows = new List<QueryValue[]>(n);
        for (int i = 0; i < n; i++)
            rows.Add(Row(keyStart + i, $"b{keyStart + i}"));
        return rows;
    }

    private static List<QueryValue[]> Collect(IEnumerable<QueryValue[]> rows) => rows.ToList();

    // Tier sizes: I=10, II=300, III=9000
    public static IEnumerable<object[]> TierSizes =>
        new List<object[]>
        {
            new object[] { 10 },    // Tier I: StackAlloc
            new object[] { 300 },   // Tier II: Pooled
            new object[] { 9000 },  // Tier III: OpenAddress
        };

    // ─── C1: Unique full match ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C1_UniqueFullMatch_AllRowsMatched(int n)
    {
        var build = BuildN(n);
        var probe = BuildN(n); // same keys

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // All matched, no unmatched on either side
        Assert.Equal(n, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r[0].IsNull);
            Assert.False(r[2].IsNull);
        });
    }

    // ─── C2: Unique partial match ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C2_UniquePartialMatch_CorrectCounts(int n)
    {
        var build = BuildN(n);

        // Probe only even keys → n/2 matches
        var probe = new List<QueryValue[]>(n / 2);
        for (int i = 0; i < n; i += 2)
            probe.Add(Row(i, $"p{i}"));

        // Also add a disjoint probe key
        probe.Add(Row(n + 100, "disjoint"));

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        int expectedMatched = n / 2;
        int expectedProbeUnmatched = 1;  // disjoint key
        int expectedBuildUnmatched = n - expectedMatched;

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        var probeUnmatched = results.Where(r => !r[0].IsNull && r[2].IsNull).ToList();
        var buildUnmatched = results.Where(r => r[0].IsNull && !r[2].IsNull).ToList();

        Assert.Equal(expectedMatched, matched.Count);
        Assert.Equal(expectedProbeUnmatched, probeUnmatched.Count);
        Assert.Equal(expectedBuildUnmatched, buildUnmatched.Count);
    }

    // ─── C3: Duplicate build keys ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C3_DuplicateBuildKeys_CartesianPerKey(int n)
    {
        var build = new List<QueryValue[]>(n + 2);
        for (int i = 0; i < n; i++)
            build.Add(Row(i, $"b{i}"));
        // 2 extra duplicates of key=0
        build.Add(Row(0, "b0dup1"));
        build.Add(Row(0, "b0dup2"));

        var probe = new List<QueryValue[]> { Row(0, "p0") };

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // key=0: 3 build × 1 probe = 3 matched
        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(3, matched.Count);

        // remaining n-1 build rows are unmatched (keys 1..n-1)
        var buildUnmatched = results.Where(r => r[0].IsNull).ToList();
        Assert.Equal(n - 1, buildUnmatched.Count);
    }

    // ─── C4: Duplicate both sides ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C4_DuplicateBothSides_CartesianProduct(int n)
    {
        var build = new List<QueryValue[]>(n + 1);
        for (int i = 0; i < n; i++)
            build.Add(Row(i, $"b{i}"));
        build.Add(Row(0, "b0dup")); // 2 entries for key=0

        var probe = new List<QueryValue[]>
        {
            Row(0, "p0a"), Row(0, "p0b"), // 2 probes for key=0
        };

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // key=0: 2 build × 2 probe = 4 matched
        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(4, matched.Count);

        // n-1 build-unmatched (keys 1..n-1)
        var buildUnmatched = results.Where(r => r[0].IsNull).ToList();
        Assert.Equal(n - 1, buildUnmatched.Count);
    }

    // ─── C5: NULL keys never match ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C5_NullKeys_NeverMatch(int n)
    {
        var build = new List<QueryValue[]>(n + 1);
        for (int i = 0; i < n; i++)
            build.Add(Row(i, $"b{i}"));
        build.Add(new[] { QueryValue.Null, QueryValue.FromString("b_null") });

        var probe = new List<QueryValue[]>
        {
            new[] { QueryValue.Null, QueryValue.FromString("p_null") },
            Row(0, "p0"),
        };

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // key=0: 1 matched
        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Single(matched);
        Assert.Equal(0L, matched[0][0].AsInt64());

        // NULL probe key: probe-unmatched
        var probeNull = results.Where(r => !r[0].IsNull && r[0].Type == QueryValueType.Null).ToList();
        // NULL build key: build-unmatched
        // Total: 1 matched + 1 probe-unmatched (null key) + n build-unmatched (n-1 non-null + 1 null)
        Assert.Equal(n + 2, results.Count);
    }

    // ─── C6: Empty build side ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C6_EmptyBuild_AllProbeUnmatched(int _)
    {
        var build = new List<QueryValue[]>();
        var probe = new List<QueryValue[]> { Row(1, "p1"), Row(2, "p2") };

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r[0].IsNull); // probe present
            Assert.True(r[2].IsNull);  // build null
        });
    }

    // ─── C7: Self-join ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C7_SelfJoin_AllMatch(int n)
    {
        var data = BuildN(n);

        var results = Collect(TieredHashJoin.Execute(
            data, 0, 2, data, 0, 2, buildIsLeft: false));

        Assert.Equal(n, results.Count);
        Assert.All(results, r =>
        {
            Assert.Equal(r[0].AsInt64(), r[2].AsInt64());
        });
    }

    // ─── C8: Disjoint keys ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C8_DisjointKeys_NoMatches(int n)
    {
        var build = BuildN(n);
        var probe = BuildN(n, keyStart: n + 1000); // completely disjoint

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // 0 matched, n probe-unmatched, n build-unmatched
        Assert.Equal(n + n, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Empty(matched);
    }

    // ─── C9: Symmetry — buildIsLeft flag ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C9_BuildIsLeft_SwapsOutputLayout(int n)
    {
        var build = BuildN(n);
        var probe = new List<QueryValue[]> { Row(0, "probe") };

        var resultsNotSwapped = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        var resultsSwapped = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: true));

        // Both should produce the same number of results
        Assert.Equal(resultsNotSwapped.Count, resultsSwapped.Count);

        // In the matched row:
        var matchedNS = resultsNotSwapped.First(r => !r[0].IsNull && !r[2].IsNull);
        var matchedS = resultsSwapped.First(r => !r[0].IsNull && !r[2].IsNull);

        // Not swapped: [probe, build]
        Assert.Equal("probe", matchedNS[1].AsString());
        Assert.Equal("b0", matchedNS[3].AsString());

        // Swapped: [build, probe]
        Assert.Equal("b0", matchedS[1].AsString());
        Assert.Equal("probe", matchedS[3].AsString());
    }

    // ─── C10: Large partial match with mixed NULLs ───
    [Theory]
    [MemberData(nameof(TierSizes))]
    public void C10_LargePartialMatch_WithNulls_CorrectTotals(int n)
    {
        // Build: n rows with keys 0..n-1, plus 5 NULL keys
        var build = new List<QueryValue[]>(n + 5);
        for (int i = 0; i < n; i++)
            build.Add(Row(i, $"b{i}"));
        for (int i = 0; i < 5; i++)
            build.Add(new[] { QueryValue.Null, QueryValue.FromString($"bnull{i}") });

        // Probe: every 3rd key matches, plus 3 NULL keys, plus 10 disjoint keys
        var probe = new List<QueryValue[]>();
        int matchCount = 0;
        for (int i = 0; i < n; i += 3)
        {
            probe.Add(Row(i, $"p{i}"));
            matchCount++;
        }
        for (int i = 0; i < 3; i++)
            probe.Add(new[] { QueryValue.Null, QueryValue.FromString($"pnull{i}") });
        for (int i = 0; i < 10; i++)
            probe.Add(Row(n + 1000 + i, $"disjoint{i}"));

        var results = Collect(TieredHashJoin.Execute(
            build, 0, 2, probe, 0, 2, buildIsLeft: false));

        // Counts:
        // matched: matchCount
        // probe-unmatched: 3 null + 10 disjoint = 13
        // build-unmatched: (n - matchCount) non-null + 5 null = n - matchCount + 5
        int expectedTotal = matchCount + 13 + (n - matchCount + 5);
        Assert.Equal(expectedTotal, results.Count);

        var matched = results.Where(r => !r[0].IsNull && !r[2].IsNull).ToList();
        Assert.Equal(matchCount, matched.Count);
    }
}
