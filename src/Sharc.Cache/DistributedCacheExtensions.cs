// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;

namespace Sharc.Cache;

/// <summary>
/// Extension methods for bulk operations on <see cref="IDistributedCache"/>.
/// When the cache is a <see cref="DistributedCache"/>, delegates to the engine's
/// optimized bulk methods. Otherwise, falls back to individual calls.
/// </summary>
public static class DistributedCacheExtensions
{
    /// <summary>
    /// Retrieves multiple cached values in a single operation.
    /// Returns a dictionary with null values for misses.
    /// </summary>
    public static Dictionary<string, byte[]?> GetMany(this IDistributedCache cache, IEnumerable<string> keys)
    {
        if (cache is DistributedCache sharc)
            return sharc.Engine.GetMany(keys);

        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var key in keys)
            result[key] = cache.Get(key);
        return result;
    }

    /// <summary>
    /// Stores multiple entries in a single operation.
    /// </summary>
    public static void SetMany(this IDistributedCache cache,
        IEnumerable<KeyValuePair<string, byte[]>> entries,
        DistributedCacheEntryOptions? options = null)
    {
        if (cache is DistributedCache sharc)
        {
            sharc.Engine.SetMany(entries, MapOptions(options));
            return;
        }

        var opts = options ?? new DistributedCacheEntryOptions();
        foreach (var kvp in entries)
            cache.Set(kvp.Key, kvp.Value, opts);
    }

    /// <summary>
    /// Removes multiple entries in a single operation.
    /// Returns the number of keys processed (exact removed count when using Sharc).
    /// </summary>
    public static int RemoveMany(this IDistributedCache cache, IEnumerable<string> keys)
    {
        if (cache is DistributedCache sharc)
            return sharc.Engine.RemoveMany(keys);

        int count = 0;
        foreach (var key in keys)
        {
            cache.Remove(key);
            count++;
        }
        return count;
    }

    private static CacheEntryOptions? MapOptions(DistributedCacheEntryOptions? options)
    {
        if (options is null)
            return null;

        return new CacheEntryOptions
        {
            AbsoluteExpiration = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration,
        };
    }
}
