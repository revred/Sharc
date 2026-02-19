// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Sharc.Cache;

namespace Sharc.Cache.OutputCaching;

/// <summary>
/// Derives the entitlement scope from a tenant ID header or route value on the current request.
/// Checks the header first, then falls back to a route value, then to a claim.
/// Returns null when no tenant ID is found.
/// </summary>
public sealed class TenantEntitlementProvider : IEntitlementProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _headerName;
    private readonly string? _routeValueKey;
    private readonly string? _claimType;

    /// <summary>
    /// Creates a tenant-based entitlement provider.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    /// <param name="headerName">Request header to read (default: <c>"X-Tenant-Id"</c>).</param>
    /// <param name="routeValueKey">Optional route value key to check as fallback.</param>
    /// <param name="claimType">Optional claim type to check as final fallback.</param>
    public TenantEntitlementProvider(
        IHttpContextAccessor httpContextAccessor,
        string headerName = "X-Tenant-Id",
        string? routeValueKey = null,
        string? claimType = null)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(headerName);
        _httpContextAccessor = httpContextAccessor;
        _headerName = headerName;
        _routeValueKey = routeValueKey;
        _claimType = claimType;
    }

    /// <inheritdoc/>
    public string? GetScope()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return null;

        // 1. Check header
        if (context.Request.Headers.TryGetValue(_headerName, out var headerValue)
            && !string.IsNullOrEmpty(headerValue.ToString()))
        {
            return $"tenant:{headerValue}";
        }

        // 2. Check route value
        if (_routeValueKey is not null
            && context.Request.RouteValues.TryGetValue(_routeValueKey, out var routeValue)
            && routeValue is string rv
            && !string.IsNullOrEmpty(rv))
        {
            return $"tenant:{rv}";
        }

        // 3. Check claim
        if (_claimType is not null)
        {
            var claimValue = context.User?.FindFirst(_claimType)?.Value;
            if (!string.IsNullOrEmpty(claimValue))
                return $"tenant:{claimValue}";
        }

        return null;
    }
}
