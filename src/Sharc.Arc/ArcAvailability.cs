// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// Availability state of an arc file after a resolution attempt.
/// </summary>
public enum ArcAvailability
{
    /// <summary>Arc is available, opened, and accessible.</summary>
    Available,

    /// <summary>Arc location is unreachable (missing file, network error, etc.).</summary>
    Unreachable,

    /// <summary>Arc exists but failed integrity or trust validation.</summary>
    Untrusted,

    /// <summary>The authority type in the URI is not supported by any registered locator.</summary>
    UnsupportedAuthority
}
