// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core.Trust;

namespace Sharc.Trust;

/// <summary>
/// Dispatches signature verification to the correct algorithm-specific verifier.
/// </summary>
public static class SignatureVerifier
{
    /// <summary>
    /// Verifies a signature using the specified algorithm.
    /// </summary>
    /// <param name="data">The data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="publicKey">The signer's public key.</param>
    /// <param name="algorithm">The algorithm that produced the signature.</param>
    /// <returns>True if the signature is valid; false otherwise.</returns>
    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature,
                              ReadOnlySpan<byte> publicKey, SignatureAlgorithm algorithm)
    {
        return algorithm switch
        {
            SignatureAlgorithm.HmacSha256 => SharcSigner.Verify(data, signature, publicKey),
            SignatureAlgorithm.EcdsaP256 => EcdsaP256Verifier.Verify(data, signature, publicKey),
            _ => false
        };
    }
}
