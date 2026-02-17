// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Xunit;

namespace Sharc.Cache.Tests;

public sealed class EntitlementEncryptorTests : IDisposable
{
    private readonly byte[] _masterKey;
    private readonly EntitlementEncryptor _encryptor;

    public EntitlementEncryptorTests()
    {
        _masterKey = new byte[32];
        RandomNumberGenerator.Fill(_masterKey);
        _encryptor = new EntitlementEncryptor(_masterKey);
    }

    public void Dispose() => _encryptor.Dispose();

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", "key1");
        var decrypted = _encryptor.TryDecrypt(encrypted, "tenant:acme", "key1");

        Assert.NotNull(decrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_DifferentScopes_DifferentCiphertext()
    {
        var plaintext = new byte[] { 10, 20, 30 };
        var enc1 = _encryptor.Encrypt(plaintext, "tenant:acme", "key1");
        var enc2 = _encryptor.Encrypt(plaintext, "tenant:contoso", "key1");

        Assert.NotEqual(enc1, enc2);
    }

    [Fact]
    public void Decrypt_WrongScope_ReturnsNull()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", "key1");
        var decrypted = _encryptor.TryDecrypt(encrypted, "tenant:contoso", "key1");

        Assert.Null(decrypted);
    }

    [Fact]
    public void Decrypt_WrongCacheKey_ReturnsNull()
    {
        var plaintext = new byte[] { 1, 2, 3 };
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", "key1");
        var decrypted = _encryptor.TryDecrypt(encrypted, "tenant:acme", "key2");

        Assert.Null(decrypted);
    }

    [Fact]
    public void SameScope_DerivesSameKey()
    {
        var plaintext = new byte[] { 42 };
        var encrypted = _encryptor.Encrypt(plaintext, "scope-A", "k");
        var decrypted = _encryptor.TryDecrypt(encrypted, "scope-A", "k");

        Assert.NotNull(decrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void DifferentScopes_DifferentDerivedKeys()
    {
        var plaintext = new byte[] { 42 };
        var encrypted = _encryptor.Encrypt(plaintext, "scope-A", "k");

        // Decrypt with scope-B should fail (different derived key)
        Assert.Null(_encryptor.TryDecrypt(encrypted, "scope-B", "k"));
    }

    [Fact]
    public void Constructor_InvalidKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EntitlementEncryptor(new byte[16]));
    }

    [Fact]
    public void Decrypt_TruncatedInput_ReturnsNull()
    {
        // Less than nonce (12) + tag (16) = 28 bytes
        var shortInput = new byte[10];
        var result = _encryptor.TryDecrypt(shortInput, "scope", "key");
        Assert.Null(result);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ReturnsNull()
    {
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        var encrypted = _encryptor.Encrypt(plaintext, "scope", "key");

        // Flip a bit in the ciphertext portion (after nonce, before tag)
        encrypted[14] ^= 0xFF;

        var result = _encryptor.TryDecrypt(encrypted, "scope", "key");
        Assert.Null(result);
    }

    [Fact]
    public void Encrypt_EmptyPlaintext_RoundTrips()
    {
        var plaintext = Array.Empty<byte>();
        var encrypted = _encryptor.Encrypt(plaintext, "scope", "key");
        var decrypted = _encryptor.TryDecrypt(encrypted, "scope", "key");

        Assert.NotNull(decrypted);
        Assert.Empty(decrypted);
    }

    [Fact]
    public void Encrypt_LargePlaintext_RoundTrips()
    {
        var plaintext = new byte[1024 * 1024]; // 1 MB
        RandomNumberGenerator.Fill(plaintext);

        var encrypted = _encryptor.Encrypt(plaintext, "scope", "key");
        var decrypted = _encryptor.TryDecrypt(encrypted, "scope", "key");

        Assert.NotNull(decrypted);
        Assert.Equal(plaintext, decrypted);
    }
}
