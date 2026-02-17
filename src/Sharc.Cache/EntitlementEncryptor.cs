// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Sharc.Cache;

/// <summary>
/// Per-scope AES-256-GCM encryption using HKDF-SHA256 derived keys.
/// Each entitlement scope gets a unique derived key from the master key.
/// Wire format: [12-byte nonce][ciphertext][16-byte auth tag].
/// AAD = UTF-8(cacheKey) to prevent ciphertext relocation between keys.
/// </summary>
internal sealed class EntitlementEncryptor : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Info = "SHARC_CACHE_v1"u8.ToArray();

    private readonly byte[] _masterKey;
    private readonly ConcurrentDictionary<string, byte[]> _keyCache = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Creates a new encryptor with the given 32-byte master key.</summary>
    /// <exception cref="ArgumentException">Thrown when master key is not 32 bytes.</exception>
    public EntitlementEncryptor(byte[] masterKey)
    {
        if (masterKey.Length != 32)
            throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey));
        _masterKey = (byte[])masterKey.Clone();
    }

    /// <summary>
    /// Encrypts plaintext for the given scope and cache key.
    /// Returns wire-format bytes: [nonce][ciphertext][tag].
    /// </summary>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string scope, string cacheKey)
    {
        var scopeKey = GetOrDeriveKey(scope);
        var aad = Encoding.UTF8.GetBytes(cacheKey);

        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        var ciphertext = output.AsSpan(NonceSize, plaintext.Length);
        var tag = output.AsSpan(NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(scopeKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        return output;
    }

    /// <summary>
    /// Attempts to decrypt ciphertext for the given scope and cache key.
    /// Returns null if decryption fails (wrong scope, tampered data, or truncated input).
    /// </summary>
    public byte[]? TryDecrypt(ReadOnlySpan<byte> encrypted, string scope, string cacheKey)
    {
        if (encrypted.Length < NonceSize + TagSize)
            return null;

        var scopeKey = GetOrDeriveKey(scope);
        var aad = Encoding.UTF8.GetBytes(cacheKey);

        var nonce = encrypted[..NonceSize];
        int ciphertextLen = encrypted.Length - NonceSize - TagSize;
        var ciphertext = encrypted.Slice(NonceSize, ciphertextLen);
        var tag = encrypted[^TagSize..];

        var plaintext = new byte[ciphertextLen];

        try
        {
            using var aes = new AesGcm(scopeKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null; // Wrong scope, tampered data, or wrong cache key
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CryptographicOperations.ZeroMemory(_masterKey);
        foreach (var kvp in _keyCache)
            CryptographicOperations.ZeroMemory(kvp.Value);
        _keyCache.Clear();
    }

    private byte[] GetOrDeriveKey(string scope)
    {
        return _keyCache.GetOrAdd(scope, s =>
        {
            int saltLen = Encoding.UTF8.GetByteCount(s);
            Span<byte> salt = saltLen <= 256 ? stackalloc byte[saltLen] : new byte[saltLen];
            Encoding.UTF8.GetBytes(s, salt);

            var key = new byte[32];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, key, salt, Info);
            return key;
        });
    }
}
