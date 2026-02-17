// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Distributed;

namespace Sharc.Cache;

/// <summary>
/// In-memory distributed cache backed by <see cref="CacheEngine"/>.
/// Implements <see cref="IDistributedCache"/> for seamless integration with
/// ASP.NET Core session state, response caching, and HybridCache L2.
/// </summary>
public sealed class DistributedCache : IDistributedCache, IDisposable
{
    private readonly CacheEngine _engine;

    internal DistributedCache(CacheEngine engine)
    {
        _engine = engine;
    }

    /// <summary>Gets the underlying cache engine for bulk operations.</summary>
    internal CacheEngine Engine => _engine;

    /// <inheritdoc/>
    public byte[]? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _engine.Get(key);
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Get(key));
    }

    /// <inheritdoc/>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _engine.Set(key, value, MapOptions(options));
    }

    /// <inheritdoc/>
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Set(key, value, options);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Refresh(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _engine.Refresh(key);
    }

    /// <inheritdoc/>
    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Refresh(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _engine.Remove(key);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _engine.Dispose();

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
