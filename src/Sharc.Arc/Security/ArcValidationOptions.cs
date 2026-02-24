// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Security;

/// <summary>
/// Options for <see cref="ArcValidator.Validate"/>. Controls which layers
/// are checked and their thresholds.
/// </summary>
public sealed class ArcValidationOptions
{
    /// <summary>Trust anchors to validate signers against. Null disables trust checking.</summary>
    public TrustAnchorSet? TrustAnchors { get; init; }

    /// <summary>Policy for unknown signers. Default: <see cref="TrustPolicy.WarnUnknown"/>.</summary>
    public TrustPolicy UnknownSignerPolicy { get; init; } = TrustPolicy.WarnUnknown;

    /// <summary>Maximum allowed ledger entries. 0 or negative to disable. Default: disabled.</summary>
    public int MaxLedgerEntryCount { get; init; } = -1;
}
