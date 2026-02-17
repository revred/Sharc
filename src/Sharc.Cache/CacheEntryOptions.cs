// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Per-entry expiration settings for the cache engine.
/// </summary>
internal sealed class CacheEntryOptions
{
    /// <summary>Fixed wall-clock expiration time.</summary>
    public DateTimeOffset? AbsoluteExpiration { get; init; }

    /// <summary>TTL relative to the current time (converted to absolute on Set).</summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; init; }

    /// <summary>Sliding expiration window that resets on each access.</summary>
    public TimeSpan? SlidingExpiration { get; init; }
}
