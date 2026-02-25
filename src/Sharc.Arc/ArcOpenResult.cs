// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc;

/// <summary>
/// Result of attempting to open an arc file. Never throws — always returns a structured result.
/// Dispose this to release the underlying <see cref="ArcHandle"/> when done.
/// </summary>
public sealed class ArcOpenResult : IDisposable
{
    /// <summary>Availability state of the arc.</summary>
    public ArcAvailability Availability { get; }

    /// <summary>The opened arc handle (null if not <see cref="ArcAvailability.Available"/>).</summary>
    public ArcHandle? Handle { get; }

    /// <summary>Human-readable error message (null on success).</summary>
    public string? ErrorMessage { get; }

    /// <summary>Non-fatal warnings encountered during open/validation.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Whether the arc was successfully opened.</summary>
    public bool IsAvailable => Availability == ArcAvailability.Available;

    private ArcOpenResult(ArcAvailability availability, ArcHandle? handle,
        string? error, IReadOnlyList<string>? warnings)
    {
        Availability = availability;
        Handle = handle;
        ErrorMessage = error;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Creates a successful result with an opened handle.</summary>
    public static ArcOpenResult Success(ArcHandle handle, IReadOnlyList<string>? warnings = null)
        => new(ArcAvailability.Available, handle, null, warnings);

    /// <summary>Creates a failure result with the given availability and error message.</summary>
    public static ArcOpenResult Failure(ArcAvailability availability, string error)
        => new(availability, null, error, null);

    /// <inheritdoc />
    public void Dispose() => Handle?.Dispose();
}
