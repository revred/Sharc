// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Query.Intent;

/// <summary>
/// Execution hint controlling which query execution tier to use.
/// Specified as a SQL prefix: <c>DIRECT</c> (default), <c>CACHED</c>, or <c>JIT</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="Direct"/> — standard parse-cache-resolve-execute pipeline.</description></item>
/// <item><description><see cref="Cached"/> — auto-Prepare: pre-compiled, skip parse+plan on repeat calls.</description></item>
/// <item><description><see cref="Jit"/> — auto-Jit: zero-parse, compiled filters, cached handles.</description></item>
/// </list>
/// </remarks>
public enum ExecutionHint : byte
{
    /// <summary>Default tier: standard execution with plan caching.</summary>
    Direct = 0,

    /// <summary>Tier 2: auto-Prepare — pre-compiled, reusable handle.</summary>
    Cached = 1,

    /// <summary>Tier 3: auto-Jit — compiled filters, maximum reuse speed.</summary>
    Jit = 2,
}
