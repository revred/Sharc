// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Trust;

/// <summary>
/// Identifies the cryptographic algorithm used for agent signatures.
/// </summary>
public enum SignatureAlgorithm : byte
{
    /// <summary>
    /// HMAC-SHA256 symmetric signing (32-byte signatures).
    /// </summary>
    HmacSha256 = 0,

    /// <summary>
    /// ECDSA P-256 + SHA-256 asymmetric signing (64-byte signatures).
    /// </summary>
    EcdsaP256 = 1
}
