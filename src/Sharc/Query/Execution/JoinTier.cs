// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Execution;

/// <summary>
/// Tier classification for zero-allocation hash join.
/// <list type="bullet">
/// <item><description><see cref="StackAlloc"/>: ≤256 build rows, stackalloc bit array, L1 cache resident.</description></item>
/// <item><description><see cref="Pooled"/>: 257–8,192 build rows, ArrayPool-backed bit-packed tracker, L2 cache resident.</description></item>
/// <item><description><see cref="DestructiveProbe"/>: &gt;8,192 build rows, open-address hash table with backward-shift deletion.</description></item>
/// </list>
/// </summary>
internal enum JoinTierKind : byte
{
    /// <summary>Build side ≤256 rows. Matched bits tracked via stackalloc.</summary>
    StackAlloc = 0,

    /// <summary>Build side 257–8,192 rows. Matched bits tracked via ArrayPool bit-packed array.</summary>
    Pooled = 1,

    /// <summary>Build side &gt;8,192 rows. Destructive probe removes matched entries from hash table.</summary>
    DestructiveProbe = 2,
}

/// <summary>
/// Tier selection logic for zero-allocation hash join.
/// </summary>
internal static class JoinTier
{
    /// <summary>Maximum build-side cardinality for stackalloc tier (L1 cache: 32 bytes for 256 bits).</summary>
    public const int StackAllocThreshold = 256;

    /// <summary>Maximum build-side cardinality for pooled tier (L2 cache: 1 KB for 8,192 bits).</summary>
    public const int PooledThreshold = 8192;

    /// <summary>Alias for <see cref="JoinTierKind.StackAlloc"/>.</summary>
    public const JoinTierKind StackAlloc = JoinTierKind.StackAlloc;

    /// <summary>Alias for <see cref="JoinTierKind.Pooled"/>.</summary>
    public const JoinTierKind Pooled = JoinTierKind.Pooled;

    /// <summary>Alias for <see cref="JoinTierKind.DestructiveProbe"/>.</summary>
    public const JoinTierKind DestructiveProbe = JoinTierKind.DestructiveProbe;

    /// <summary>
    /// Selects the appropriate join tier based on build-side cardinality.
    /// </summary>
    public static JoinTierKind Select(int buildCount)
    {
        if (buildCount <= StackAllocThreshold) return JoinTierKind.StackAlloc;
        if (buildCount <= PooledThreshold) return JoinTierKind.Pooled;
        return JoinTierKind.DestructiveProbe;
    }
}
