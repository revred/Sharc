// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Sharc.Crypto;

/// <summary>
/// Row-level column encryption using AES-256-GCM with HKDF-derived per-tag keys.
/// <para>
/// <b>Zero cost when disabled</b>: this class is only instantiated when row-level
/// entitlement encryption is explicitly enabled. When not used, no code paths execute.
/// </para>
/// <para>
/// Wire format per encrypted column value: <c>[12-byte nonce][ciphertext][16-byte tag]</c>.
/// The nonce is derived from the row-level key + rowId, ensuring deterministic
/// encryption for the same key/rowId pair (enables re-encryption verification).
/// </para>
/// </summary>
public sealed class RowLevelEncryptor : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Overhead bytes per encrypted value (nonce + auth tag).</summary>
    public const int Overhead = NonceSize + TagSize; // 28 bytes

    private readonly byte[] _masterKey;
    private readonly Dictionary<string, byte[]> _keyCache = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Creates a row-level encryptor with the given 32-byte master key.
    /// </summary>
    /// <param name="masterKey">The 32-byte master encryption key.</param>
    public RowLevelEncryptor(byte[] masterKey)
    {
        if (masterKey.Length != 32)
            throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey));
        _masterKey = (byte[])masterKey.Clone();
    }

    /// <summary>
    /// Creates a row-level encryptor from a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public RowLevelEncryptor(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.Length != 32)
            throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey));
        _masterKey = masterKey.ToArray();
    }

    /// <summary>
    /// Encrypts a plaintext value for the given entitlement tag and rowId.
    /// Returns the encrypted blob: <c>[nonce][ciphertext][tag]</c>.
    /// </summary>
    /// <param name="plaintext">The plaintext bytes to encrypt.</param>
    /// <param name="entitlementTag">The entitlement tag (e.g., "tenant:acme").</param>
    /// <param name="rowId">The row identifier, used as additional authenticated data.</param>
    /// <returns>Encrypted byte array.</returns>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string entitlementTag, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] rowKey = GetOrDeriveKey(entitlementTag);
        var result = new byte[NonceSize + plaintext.Length + TagSize];

        var nonceSpan = result.AsSpan(0, NonceSize);
        var ciphertextSpan = result.AsSpan(NonceSize, plaintext.Length);
        var tagSpan = result.AsSpan(NonceSize + plaintext.Length, TagSize);

        // Deterministic nonce from HMAC(rowKey, rowId) — same key+rowId = same nonce
        DeriveNonce(rowKey, rowId, nonceSpan);

        // AAD = rowId as 8-byte big-endian (prevents value swapping between rows)
        Span<byte> aad = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(aad, rowId);

        using var aes = new AesGcm(rowKey, TagSize);
        aes.Encrypt(nonceSpan, plaintext, ciphertextSpan, tagSpan, aad);

        return result;
    }

    /// <summary>
    /// Decrypts an encrypted column value for the given entitlement tag and rowId.
    /// Returns the plaintext bytes, or throws <see cref="CryptographicException"/> if
    /// the tag is wrong or the data has been tampered with.
    /// </summary>
    /// <param name="encrypted">The encrypted blob: <c>[nonce][ciphertext][tag]</c>.</param>
    /// <param name="entitlementTag">The entitlement tag used during encryption.</param>
    /// <param name="rowId">The row identifier used during encryption.</param>
    /// <returns>Decrypted plaintext bytes.</returns>
    public byte[] Decrypt(ReadOnlySpan<byte> encrypted, string entitlementTag, long rowId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encrypted.Length < Overhead)
            throw new CryptographicException(
                $"Encrypted value too small: expected at least {Overhead} bytes, got {encrypted.Length}.");

        byte[] rowKey = GetOrDeriveKey(entitlementTag);

        int plaintextLen = encrypted.Length - Overhead;
        var nonce = encrypted[..NonceSize];
        var ciphertext = encrypted.Slice(NonceSize, plaintextLen);
        var tag = encrypted.Slice(NonceSize + plaintextLen, TagSize);

        var plaintext = new byte[plaintextLen];

        // AAD = rowId as 8-byte big-endian
        Span<byte> aad = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(aad, rowId);

        using var aes = new AesGcm(rowKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        return plaintext;
    }

    /// <summary>
    /// Attempts to decrypt an encrypted column value. Returns false if decryption
    /// fails (wrong tag, tampered data) instead of throwing.
    /// </summary>
    /// <param name="encrypted">The encrypted blob.</param>
    /// <param name="entitlementTag">The entitlement tag.</param>
    /// <param name="rowId">The row identifier.</param>
    /// <param name="plaintext">The decrypted plaintext if successful; null otherwise.</param>
    /// <returns>True if decryption succeeded; false otherwise.</returns>
    public bool TryDecrypt(ReadOnlySpan<byte> encrypted, string entitlementTag, long rowId,
        out byte[]? plaintext)
    {
        try
        {
            plaintext = Decrypt(encrypted, entitlementTag, rowId);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = null;
            return false;
        }
    }

    /// <summary>
    /// Gets the cached row-level key for the given tag, or derives it via HKDF.
    /// </summary>
    private byte[] GetOrDeriveKey(string entitlementTag)
    {
        if (_keyCache.TryGetValue(entitlementTag, out var cached))
            return cached;

        var key = HkdfSha256.DeriveRowKey(_masterKey, entitlementTag);
        _keyCache[entitlementTag] = key;
        return key;
    }

    /// <summary>
    /// Derives a deterministic 12-byte nonce from the row key and row ID.
    /// Uses HMAC-SHA256(rowKey, rowId)[..12].
    /// </summary>
    private static void DeriveNonce(byte[] rowKey, long rowId, Span<byte> nonce)
    {
        Span<byte> rowIdBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(rowIdBytes, rowId);

        Span<byte> hmacOutput = stackalloc byte[32];
        HMACSHA256.TryHashData(rowKey, rowIdBytes, hmacOutput, out _);
        hmacOutput[..NonceSize].CopyTo(nonce);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clear cached keys from memory
        foreach (var key in _keyCache.Values)
            CryptographicOperations.ZeroMemory(key);
        _keyCache.Clear();

        CryptographicOperations.ZeroMemory(_masterKey);
    }
}
