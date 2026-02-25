// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc.Locators;

namespace Sharc.Arc;

/// <summary>
/// Routes <see cref="ArcUri"/> resolution to the appropriate <see cref="IArcLocator"/>
/// by authority. Central entry point for cross-arc reference resolution.
/// <para>
/// Pre-registers <see cref="LocalArcLocator"/> by default.
/// Call <see cref="Register"/> to add remote locators (HTTP, git, etc.).
/// </para>
/// </summary>
public sealed class ArcResolver
{
    private readonly Dictionary<string, IArcLocator> _locators = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a resolver with the default <see cref="LocalArcLocator"/> registered.
    /// </summary>
    public ArcResolver()
    {
        Register(new LocalArcLocator());
    }

    /// <summary>
    /// Registers a locator for a specific authority.
    /// Replaces any existing locator for that authority.
    /// </summary>
    public void Register(IArcLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locators[locator.Authority] = locator;
    }

    /// <summary>
    /// Resolves an <see cref="ArcUri"/>. Never throws.
    /// Returns <see cref="ArcAvailability.UnsupportedAuthority"/> if no locator matches.
    /// </summary>
    public ArcOpenResult Resolve(ArcUri uri, ArcOpenOptions? options = null)
    {
        if (!_locators.TryGetValue(uri.Authority, out var locator))
            return ArcOpenResult.Failure(ArcAvailability.UnsupportedAuthority,
                $"No locator registered for authority '{uri.Authority}'.");

        return locator.TryOpen(uri, options);
    }

    /// <summary>
    /// Resolves from a raw URI string. Never throws.
    /// </summary>
    public ArcOpenResult Resolve(string uriString, ArcOpenOptions? options = null)
    {
        if (!ArcUri.TryParse(uriString, out var uri))
            return ArcOpenResult.Failure(ArcAvailability.Unreachable,
                $"Invalid arc URI: '{uriString}'");

        return Resolve(uri, options);
    }
}
