// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;

namespace Sharc.Trust;

/// <summary>
/// Native .NET ECDSA P-256 + SHA-256 signature verifier.
/// Handles the raw 64-byte (r+s) IEEE P1363 format produced by Web Crypto API.
/// </summary>
internal static class EcdsaP256Verifier
{
    /// <summary>
    /// Verifies an ECDSA P-256 signature over the given data.
    /// </summary>
    /// <param name="data">The signed data.</param>
    /// <param name="signature">The 64-byte IEEE P1363 signature (r + s, 32 bytes each).</param>
    /// <param name="publicKey">The public key in SubjectPublicKeyInfo (DER) format.</param>
    /// <returns>True if the signature is valid; false otherwise.</returns>
    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
