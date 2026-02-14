// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using System.Security.Cryptography;
using System.Text;

namespace Sharc.Crypto;

/// <summary>
/// HKDF-SHA256 key derivation for row-level entitlement encryption.
/// Derives a unique 32-byte row-level key (RLK) from a master key and entitlement tag.
/// Per GreyPaper Section 5: RLK = HKDF-SHA256(master_key, salt=entitlement_tag, info="SHARC_ROW_v1", length=32).
/// </summary>
internal static class HkdfSha256
{
    private static readonly byte[] RowKeyInfo = "SHARC_ROW_v1"u8.ToArray();

    /// <summary>
    /// Derives a 32-byte row-level key from a master key and entitlement tag string.
    /// </summary>
    /// <param name="masterKey">The 32-byte master encryption key.</param>
    /// <param name="entitlementTag">The entitlement tag (e.g., "tenant:acme").</param>
    /// <returns>A 32-byte derived key unique to this master key + tag combination.</returns>
    public static byte[] DeriveRowKey(ReadOnlySpan<byte> masterKey, string entitlementTag)
    {
        int saltLen = Encoding.UTF8.GetByteCount(entitlementTag);
        Span<byte> salt = saltLen <= 256 ? stackalloc byte[saltLen] : new byte[saltLen];
        Encoding.UTF8.GetBytes(entitlementTag, salt);
        return DeriveRowKey(masterKey, salt);
    }

    /// <summary>
    /// Derives a 32-byte row-level key from a master key and salt bytes.
    /// </summary>
    /// <param name="masterKey">The 32-byte master encryption key.</param>
    /// <param name="salt">The salt bytes (typically UTF-8 encoded entitlement tag).</param>
    /// <returns>A 32-byte derived key.</returns>
    public static byte[] DeriveRowKey(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> salt)
    {
        var output = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, output, salt, RowKeyInfo);
        return output;
    }
}