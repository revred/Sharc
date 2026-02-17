// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sharc.Cache.OutputCaching;

/// <summary>
/// Extension methods for registering <see cref="SharcOutputCacheStore"/> with DI.
/// </summary>
public static class OutputCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="SharcOutputCacheStore"/> as the <see cref="IOutputCacheStore"/> singleton.
    /// When <c>AddSharcCache()</c> has already been called, the output cache store shares
    /// the same engine. Otherwise a dedicated engine is created from <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddSharcOutputCache(
        this IServiceCollection services,
        Action<CacheOptions>? configure = null)
    {
        services.TryAddSingleton<IOutputCacheStore>(sp =>
        {
            // Reuse existing CacheEngine if AddSharcCache() was called first.
            var engine = sp.GetService<CacheEngine>();
            if (engine is not null)
                return new SharcOutputCacheStore(engine);

            var options = new CacheOptions();
            configure?.Invoke(options);
            return new SharcOutputCacheStore(options);
        });
        return services;
    }
}
