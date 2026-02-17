// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sharc.Cache.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharcCache_RegistersIDistributedCache()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetService<IDistributedCache>();

        Assert.NotNull(cache);
        Assert.IsType<DistributedCache>(cache);
    }

    [Fact]
    public void AddSharcCache_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDistributedCache));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddSharcCache_WithConfiguration_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddSharcCache(opts =>
        {
            opts.MaxCacheSize = 1024;
            opts.MaxEntries = 10;
        });

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        // Store 11 entries — with MaxEntries=10, the first should be evicted
        for (int i = 0; i < 11; i++)
            cache.Set($"key{i}", new byte[] { (byte)i }, new DistributedCacheEntryOptions());

        // Verify eviction happened (key0 evicted as LRU)
        Assert.Null(cache.Get("key0"));
        Assert.NotNull(cache.Get("key10"));
    }

    [Fact]
    public void AddSharcCache_WithoutConfiguration_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        // Should be able to store many entries without eviction
        for (int i = 0; i < 100; i++)
            cache.Set($"key{i}", new byte[] { (byte)i }, new DistributedCacheEntryOptions());

        Assert.NotNull(cache.Get("key0"));
        Assert.NotNull(cache.Get("key99"));
    }

    [Fact]
    public void AddSharcCache_ResolveTwice_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IDistributedCache>();
        var second = provider.GetRequiredService<IDistributedCache>();

        Assert.Same(first, second);
    }
}
