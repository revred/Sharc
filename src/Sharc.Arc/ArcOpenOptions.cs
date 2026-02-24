// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc.Security;

namespace Sharc.Arc;

/// <summary>
/// Options for opening an arc file.
/// </summary>
public sealed class ArcOpenOptions
{
    /// <summary>
    /// Maximum file size to accept in bytes. Default: 100 MB.
    /// Defense against oversized payloads (DoS via resource exhaustion).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Whether to validate hash-chain integrity on open. Default: true.
    /// Set to false for performance when opening known-good local arcs.
    /// </summary>
    public bool ValidateOnOpen { get; set; } = true;

    /// <summary>
    /// Trust anchors for verifying signers in the arc's ledger.
    /// Null means no trust anchor checking (equivalent to <see cref="TrustPolicy.AcceptAll"/>).
    /// </summary>
    public TrustAnchorSet? TrustAnchors { get; set; }

    /// <summary>
    /// Policy for handling ledger entries signed by agents not in the trust anchor set.
    /// Default: <see cref="TrustPolicy.WarnUnknown"/>.
    /// </summary>
    public TrustPolicy UnknownSignerPolicy { get; set; } = TrustPolicy.WarnUnknown;

    /// <summary>
    /// Base directory for resolving relative paths. Null = current directory.
    /// </summary>
    public string? BaseDirectory { get; set; }
}
