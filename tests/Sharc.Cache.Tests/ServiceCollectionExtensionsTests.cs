// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
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

    [Fact]
    public void AddSharcCache_ResolvesRegisteredEntitlementProvider()
    {
        var masterKey = new byte[32];
        RandomNumberGenerator.Fill(masterKey);
        var fakeProvider = new FakeEntitlementProvider { CurrentScope = "tenant:acme" };

        var services = new ServiceCollection();
        services.AddSingleton<IEntitlementProvider>(fakeProvider);
        services.AddSharcCache(opts =>
        {
            opts.EnableEntitlement = true;
            opts.MasterKey = masterKey;
        });

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IDistributedCache>();

        // Set with scope
        cache.Set("k1", [1, 2, 3], new DistributedCacheEntryOptions());

        // The entitlement provider is integrated — engine uses it
        var result = cache.Get("k1");
        Assert.NotNull(result);
        Assert.Equal(new byte[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void AddSharcCache_NoEntitlementProvider_UsesNullProvider()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IDistributedCache>();

        // Basic set/get works without any entitlement provider registered
        cache.Set("k1", [1, 2, 3], new DistributedCacheEntryOptions());
        Assert.Equal(new byte[] { 1, 2, 3 }, cache.Get("k1"));
    }

    [Fact]
    public void AddSharcCache_RegistersCacheEngineSingleton()
    {
        var services = new ServiceCollection();
        services.AddSharcCache();

        using var sp = services.BuildServiceProvider();
        var engine1 = sp.GetRequiredService<CacheEngine>();
        var engine2 = sp.GetRequiredService<CacheEngine>();

        Assert.Same(engine1, engine2);
    }

    private sealed class FakeEntitlementProvider : IEntitlementProvider
    {
        public string? CurrentScope { get; set; }
        public string? GetScope() => CurrentScope;
    }
}
