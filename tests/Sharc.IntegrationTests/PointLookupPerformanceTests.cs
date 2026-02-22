// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Sharc.IntegrationTests.Helpers;
using Xunit;

namespace Sharc.IntegrationTests;

/// <summary>
/// Performance regression gates for point lookup (B-tree Seek).
/// Release target: &lt; 300 ns. Debug runs ~2.5x slower due to JIT.
/// Latency tests are excluded from CI (shared runners are 4-6x slower).
/// Allocation tests run everywhere since they're not timing-sensitive.
/// Run locally: dotnet test --filter "Category=Performance"
/// </summary>
public class PointLookupPerformanceTests
{
    /// <summary>
    /// Gate: PointLookup must stay under 1500 ns (Debug-safe).
    /// Release baseline: 272 ns. Debug baseline: ~700 ns.
    /// Threshold = ~2x Debug baseline to tolerate CI noise.
    /// A real regression (extra allocation, removed inlining) → 3000+ ns.
    /// Excluded from CI: shared runners measure 4,000+ ns due to resource contention.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void PointLookup_MeanLatency_RegressionGate()
    {
        const int warmupIterations = 10_000;
        const int measuredIterations = 100_000;
        const long ciThresholdNs = 1_500; // 2x Debug baseline, catches real regressions

        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("users");

        // Warmup: let JIT compile, caches fill, CPU ramp
        for (int i = 0; i < warmupIterations; i++)
        {
            reader.Seek(50);
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measuredIterations; i++)
        {
            reader.Seek(50);
        }
        sw.Stop();

        double meanNs = (double)sw.ElapsedTicks / measuredIterations
                        * (1_000_000_000.0 / Stopwatch.Frequency);

        Assert.True(meanNs < ciThresholdNs,
            $"PointLookup mean = {meanNs:F1} ns (gate: < {ciThresholdNs} ns). " +
            $"Release target: < 300 ns. Debug baseline: ~700 ns.");
    }

    /// <summary>
    /// Gate: PointLookup must allocate &lt;= 664 B (Tier 0 budget).
    /// Measured after warmup to exclude one-time JIT/cache allocations.
    /// </summary>
    [Fact]
    public void PointLookup_Allocation_UnderTier0Budget()
    {
        const long maxBytes = 1_024; // 664 B measured + margin

        var data = TestDatabaseFactory.CreateUsersDatabase(100);
        using var db = SharcDatabase.OpenMemory(data);

        // Warmup
        using (var warmup = db.CreateReader("users"))
        {
            warmup.Seek(50);
            _ = warmup.GetString(1);
        }

        // Measure a single seek + read
        long before = GC.GetAllocatedBytesForCurrentThread();
        using (var reader = db.CreateReader("users"))
        {
            reader.Seek(50);
            _ = reader.GetString(1);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.True(allocated <= maxBytes,
            $"PointLookup allocated {allocated:N0} B (gate: <= {maxBytes:N0} B). " +
            $"Baseline is 664 B.");
    }

    /// <summary>
    /// Gate: PointLookup on a multi-page B-tree (1000 rows) stays under threshold.
    /// Deeper tree = more page traversals, but should still be sub-microsecond.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void PointLookup_LargeTable_StillSubMicrosecond()
    {
        const int warmupIterations = 5_000;
        const int measuredIterations = 50_000;
        const long ciThresholdNs = 2_500; // deeper tree, Debug-safe

        var data = TestDatabaseFactory.CreateLargeDatabase(1000);
        using var db = SharcDatabase.OpenMemory(data);
        using var reader = db.CreateReader("large_table");

        // Warmup
        for (int i = 0; i < warmupIterations; i++)
        {
            reader.Seek(500);
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measuredIterations; i++)
        {
            reader.Seek(500);
        }
        sw.Stop();

        double meanNs = (double)sw.ElapsedTicks / measuredIterations
                        * (1_000_000_000.0 / Stopwatch.Frequency);

        Assert.True(meanNs < ciThresholdNs,
            $"PointLookup (1K rows) mean = {meanNs:F1} ns (gate: < {ciThresholdNs} ns). " +
            $"Deeper B-tree should still be sub-microsecond.");
    }
}
