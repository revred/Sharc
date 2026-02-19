// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sharc.Cache;

/// <summary>
/// Extension methods for registering Sharc cache services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sharc as the <see cref="IDistributedCache"/> implementation.
    /// HybridCache automatically discovers this registration as its L2 backend.
    /// If an <see cref="IEntitlementProvider"/> is registered, it will be used for per-scope encryption.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback for <see cref="CacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharcCache(
        this IServiceCollection services,
        Action<CacheOptions>? configure = null)
    {
        services.TryAddSingleton(sp =>
        {
            var options = new CacheOptions();
            configure?.Invoke(options);
            var provider = sp.GetService<IEntitlementProvider>();
            return new CacheEngine(options, provider);
        });

        services.TryAddSingleton<IDistributedCache>(sp =>
        {
            var engine = sp.GetRequiredService<CacheEngine>();
            return new DistributedCache(engine);
        });

        return services;
    }
}
