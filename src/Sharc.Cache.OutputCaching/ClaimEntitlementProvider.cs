// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Sharc.Cache;

namespace Sharc.Cache.OutputCaching;

/// <summary>
/// Derives the entitlement scope from a configurable claim on the current user's <see cref="System.Security.Claims.ClaimsPrincipal"/>.
/// Default claim type is <c>"tenant_id"</c>. Returns null when unauthenticated or claim is absent.
/// </summary>
public sealed class ClaimEntitlementProvider : IEntitlementProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _claimType;
    private readonly string? _scopePrefix;

    /// <summary>
    /// Creates a claim-based entitlement provider.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    /// <param name="claimType">The claim type to read (default: <c>"tenant_id"</c>).</param>
    /// <param name="scopePrefix">Optional prefix prepended to the claim value (e.g., <c>"tenant:"</c>).</param>
    public ClaimEntitlementProvider(
        IHttpContextAccessor httpContextAccessor,
        string claimType = "tenant_id",
        string? scopePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(claimType);
        _httpContextAccessor = httpContextAccessor;
        _claimType = claimType;
        _scopePrefix = scopePrefix;
    }

    /// <inheritdoc/>
    public string? GetScope()
    {
        var claimValue = _httpContextAccessor.HttpContext?.User?.FindFirst(_claimType)?.Value;
        if (claimValue is null)
            return null;

        return _scopePrefix is not null
            ? string.Concat(_scopePrefix, claimValue)
            : claimValue;
    }
}
