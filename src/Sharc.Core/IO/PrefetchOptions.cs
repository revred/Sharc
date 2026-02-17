// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.IO;

/// <summary>
/// Configuration for sequential read-ahead prefetch in <see cref="CachedPageSource"/>.
/// </summary>
public sealed class PrefetchOptions
{
    /// <summary>
    /// Number of consecutive sequential page accesses that trigger prefetch.
    /// Default: 3.
    /// </summary>
    public int SequentialThreshold { get; set; } = 3;

    /// <summary>
    /// Number of pages to prefetch ahead once sequential access is detected.
    /// Default: 4.
    /// </summary>
    public int PrefetchDepth { get; set; } = 4;

    /// <summary>
    /// Disable prefetch entirely. Default: false.
    /// </summary>
    public bool Disabled { get; set; }
}
