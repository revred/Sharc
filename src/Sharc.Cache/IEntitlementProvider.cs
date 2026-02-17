// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Resolves the entitlement scope for the current request context.
/// Implement this interface to enable per-scope encryption in the cache.
/// </summary>
public interface IEntitlementProvider
{
    /// <summary>
    /// Returns the entitlement scope for the current request context.
    /// Examples: "tenant:acme", "role:admin", "user:42".
    /// Returns null for unscoped (public) cache entries.
    /// </summary>
    string? GetScope();
}
