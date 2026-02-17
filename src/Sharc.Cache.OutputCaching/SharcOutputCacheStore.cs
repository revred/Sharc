// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.OutputCaching;

namespace Sharc.Cache.OutputCaching;

/// <summary>
/// ASP.NET Core <see cref="IOutputCacheStore"/> backed by the Sharc in-memory cache engine.
/// Thin adapter — all methods delegate directly to <see cref="CacheEngine"/>.
/// </summary>
public sealed class SharcOutputCacheStore : IOutputCacheStore, IDisposable
{
    private readonly CacheEngine _engine;

    /// <summary>Creates a new store with the given cache options.</summary>
    public SharcOutputCacheStore(CacheOptions options)
    {
        _engine = new CacheEngine(options);
    }

    /// <inheritdoc/>
    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]?>(_engine.Get(key));
    }

    /// <inheritdoc/>
    public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _engine.Set(key, value, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = validFor,
            Tags = tags,
        });
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _engine.EvictByTag(tag);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _engine.Dispose();
}
