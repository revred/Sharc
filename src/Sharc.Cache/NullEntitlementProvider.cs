// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache;

/// <summary>
/// Default entitlement provider that always returns null (no scope).
/// Used when entitlement encryption is disabled.
/// </summary>
internal sealed class NullEntitlementProvider : IEntitlementProvider
{
    public static readonly NullEntitlementProvider Instance = new();

    public string? GetScope() => null;
}
