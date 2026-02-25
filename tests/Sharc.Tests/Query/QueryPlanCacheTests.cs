// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Query;
using Xunit;

namespace Sharc.Tests.Query;

/// <summary>
/// Tests for <see cref="QueryPlanCache"/> capacity-bounded eviction.
/// </summary>
public sealed class QueryPlanCacheTests
{
    [Fact]
    public void GetOrCompilePlan_CachesResult()
    {
        var cache = new QueryPlanCache();
        var plan1 = cache.GetOrCompilePlan("SELECT name FROM users");
        var plan2 = cache.GetOrCompilePlan("SELECT name FROM users");

        Assert.Same(plan1, plan2);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void GetOrCompilePlan_EvictsAtMaxCapacity()
    {
        var cache = new QueryPlanCache();

        // Fill to capacity
        for (int i = 0; i < QueryPlanCache.MaxCapacity; i++)
            cache.GetOrCompilePlan($"SELECT id FROM t{i}");

        Assert.Equal(QueryPlanCache.MaxCapacity, cache.Count);

        // One more triggers eviction
        cache.GetOrCompilePlan("SELECT id FROM overflow");

        // Cache was cleared then one new entry added
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void GetOrCompilePlan_PostEviction_StillWorks()
    {
        var cache = new QueryPlanCache();

        // Fill to capacity
        for (int i = 0; i < QueryPlanCache.MaxCapacity; i++)
            cache.GetOrCompilePlan($"SELECT id FROM t{i}");

        // Trigger eviction
        var plan = cache.GetOrCompilePlan("SELECT name FROM users WHERE id = 1");

        Assert.NotNull(plan);
        Assert.NotNull(plan.Simple);
        Assert.Equal("users", plan.Simple!.TableName);
    }

    [Fact]
    public void MaxCapacity_Is1024()
    {
        Assert.Equal(1024, QueryPlanCache.MaxCapacity);
    }
}
