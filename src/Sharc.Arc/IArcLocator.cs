// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// Resolves an <see cref="ArcUri"/> to an opened <see cref="ArcHandle"/>.
/// Implementations are authority-specific (e.g., "local" for filesystem, "https" for HTTP).
/// <para>
/// <b>Contract:</b> <see cref="TryOpen"/> MUST NOT throw.
/// All errors are returned as <see cref="ArcOpenResult"/> with appropriate
/// <see cref="ArcAvailability"/> status.
/// </para>
/// </summary>
public interface IArcLocator
{
    /// <summary>The URI authority this locator handles (e.g., "local", "https", "git").</summary>
    string Authority { get; }

    /// <summary>
    /// Attempts to resolve and open the arc at the given URI.
    /// MUST NOT throw. Returns <see cref="ArcOpenResult"/> with availability status.
    /// </summary>
    ArcOpenResult TryOpen(ArcUri uri, ArcOpenOptions? options = null);
}
