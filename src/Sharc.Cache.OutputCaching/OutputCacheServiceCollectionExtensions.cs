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
    /// </summary>
    public static IServiceCollection AddSharcOutputCache(
        this IServiceCollection services,
        Action<CacheOptions>? configure = null)
    {
        var options = new CacheOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IOutputCacheStore>(new SharcOutputCacheStore(options));
        return services;
    }
}
