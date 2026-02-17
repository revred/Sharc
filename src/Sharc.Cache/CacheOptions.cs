// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Configuration options for the in-memory cache engine.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>Maximum total size of cached values in bytes. Default is 256 MB.</summary>
    public long MaxCacheSize { get; set; } = 256L * 1024 * 1024;

    /// <summary>Interval between background TTL sweep passes. Default is 60 seconds.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Optional hard cap on number of entries. 0 means no limit (size-based only).</summary>
    public int MaxEntries { get; set; }

    /// <summary>Time provider for testability. Defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Whether entitlement-based per-scope encryption is enabled. Default: false.</summary>
    public bool EnableEntitlement { get; set; }

    /// <summary>
    /// 32-byte master key for HKDF scope key derivation.
    /// Required when <see cref="EnableEntitlement"/> is true (unless <see cref="MasterKeyProvider"/> is set).
    /// </summary>
    public byte[]? MasterKey { get; set; }

    /// <summary>
    /// Callback that provides the master key. Evaluated once at engine construction.
    /// Takes precedence over <see cref="MasterKey"/> if both are set.
    /// </summary>
    public Func<byte[]>? MasterKeyProvider { get; set; }
}
