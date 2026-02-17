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
    private readonly bool _ownsEngine;

    /// <summary>Creates a new store with the given cache options (owns the engine).</summary>
    public SharcOutputCacheStore(CacheOptions options)
    {
        _engine = new CacheEngine(options);
        _ownsEngine = true;
    }

    /// <summary>Creates a store backed by an existing engine (does not dispose it).</summary>
    internal SharcOutputCacheStore(CacheEngine engine)
    {
        _engine = engine;
        _ownsEngine = false;
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
    public void Dispose()
    {
        if (_ownsEngine)
            _engine.Dispose();
    }
}
