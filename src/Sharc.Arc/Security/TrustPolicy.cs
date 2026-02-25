// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Arc.Security;

/// <summary>
/// Policy for handling ledger entries signed by agents not in the trust anchor set.
/// </summary>
public enum TrustPolicy
{
    /// <summary>Reject the arc if any signer is not in the trust anchor set.</summary>
    RejectUnknown,

    /// <summary>Accept the arc but include warnings for unknown signers.</summary>
    WarnUnknown,

    /// <summary>Accept all signers (disable trust anchor checking).</summary>
    AcceptAll
}
