// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Sharc.Cache;

/// <summary>
/// Extension methods for registering Sharc cache services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sharc as the <see cref="IDistributedCache"/> implementation.
    /// HybridCache automatically discovers this registration as its L2 backend.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback for <see cref="CacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharcCache(
        this IServiceCollection services,
        Action<CacheOptions>? configure = null)
    {
        var options = new CacheOptions();
        configure?.Invoke(options);

        var engine = new CacheEngine(options);
        var cache = new DistributedCache(engine);

        services.AddSingleton(engine);
        services.AddSingleton<IDistributedCache>(cache);

        return services;
    }
}
