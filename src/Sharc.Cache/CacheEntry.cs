// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Represents a single cached value with expiration metadata.
/// Mutable <see cref="LastAccess"/> supports sliding expiration without re-insertion.
/// </summary>
internal sealed class CacheEntry
{
    /// <summary>The cached payload.</summary>
    public byte[] Value { get; }

    /// <summary>Absolute wall-clock expiration time, or null for no absolute expiry.</summary>
    public DateTimeOffset? AbsoluteExpiration { get; }

    /// <summary>Sliding expiration window, or null for no sliding expiry.</summary>
    public TimeSpan? SlidingExpiration { get; }

    /// <summary>Last time this entry was accessed. Updated on Get and Refresh for sliding expiry.</summary>
    public DateTimeOffset LastAccess { get; set; }

    /// <summary>Estimated memory footprint of this entry in bytes (value length + overhead).</summary>
    public long Size { get; }

    private const int OverheadBytes = 96; // object header + fields + dictionary entry

    public CacheEntry(byte[] value, DateTimeOffset? absoluteExpiration,
                      TimeSpan? slidingExpiration, DateTimeOffset now)
    {
        Value = value;
        AbsoluteExpiration = absoluteExpiration;
        SlidingExpiration = slidingExpiration;
        LastAccess = now;
        Size = value.Length + OverheadBytes;
    }

    /// <summary>
    /// Checks whether this entry has expired based on absolute and/or sliding expiration.
    /// </summary>
    public bool IsExpired(DateTimeOffset now)
    {
        if (AbsoluteExpiration.HasValue && now >= AbsoluteExpiration.Value)
            return true;

        if (SlidingExpiration.HasValue && (now - LastAccess) >= SlidingExpiration.Value)
            return true;

        return false;
    }

    /// <summary>
    /// Refreshes the <see cref="LastAccess"/> timestamp to extend the sliding window.
    /// </summary>
    public void Touch(DateTimeOffset now) => LastAccess = now;
}
