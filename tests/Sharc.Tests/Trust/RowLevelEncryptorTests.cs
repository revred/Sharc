// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Trust;

public sealed class RowLevelEncryptorTests : IDisposable
{
    private readonly byte[] _masterKey;
    private readonly RowLevelEncryptor _encryptor;

    public RowLevelEncryptorTests()
    {
        _masterKey = new byte[32];
        RandomNumberGenerator.Fill(_masterKey);
        _encryptor = new RowLevelEncryptor(_masterKey);
    }

    public void Dispose() => _encryptor.Dispose();

    // ── Roundtrip Tests ──

    [Fact]
    public void Encrypt_Decrypt_TextRoundtrip()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, Sharc!");
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", rowId: 1);
        var decrypted = _encryptor.Decrypt(encrypted, "tenant:acme", rowId: 1);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_IntegerRoundtrip()
    {
        // Simulate an integer value as 8-byte big-endian
        var plaintext = BitConverter.GetBytes(42L);
        var encrypted = _encryptor.Encrypt(plaintext, "role:admin", rowId: 7);
        var decrypted = _encryptor.Decrypt(encrypted, "role:admin", rowId: 7);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_BlobRoundtrip()
    {
        var plaintext = new byte[256];
        RandomNumberGenerator.Fill(plaintext);
        var encrypted = _encryptor.Encrypt(plaintext, "team:engineering", rowId: 100);
        var decrypted = _encryptor.Decrypt(encrypted, "team:engineering", rowId: 100);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_EmptyPlaintext()
    {
        var encrypted = _encryptor.Encrypt(ReadOnlySpan<byte>.Empty, "tag", rowId: 1);
        var decrypted = _encryptor.Decrypt(encrypted, "tag", rowId: 1);

        Assert.Empty(decrypted);
    }

    // ── Wrong Tag Tests ──

    [Fact]
    public void Decrypt_WrongTag_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret data");
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", rowId: 1);

        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptor.Decrypt(encrypted, "tenant:evil", rowId: 1));
    }

    [Fact]
    public void TryDecrypt_WrongTag_ReturnsFalse()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret data");
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", rowId: 1);

        bool success = _encryptor.TryDecrypt(encrypted, "tenant:evil", rowId: 1, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecrypt_CorrectTag_ReturnsTrue()
    {
        var plaintext = Encoding.UTF8.GetBytes("visible data");
        var encrypted = _encryptor.Encrypt(plaintext, "role:cfo", rowId: 5);

        bool success = _encryptor.TryDecrypt(encrypted, "role:cfo", rowId: 5, out var result);

        Assert.True(success);
        Assert.Equal(plaintext, result);
    }

    // ── Wrong RowId Tests ──

    [Fact]
    public void Decrypt_WrongRowId_ThrowsCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret data");
        var encrypted = _encryptor.Encrypt(plaintext, "tenant:acme", rowId: 1);

        // AAD includes rowId — wrong rowId fails authentication
        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptor.Decrypt(encrypted, "tenant:acme", rowId: 999));
    }

    // ── Key Derivation Consistency ──

    [Fact]
    public void SameTag_ProducesDeterministicEncryption()
    {
        var plaintext = Encoding.UTF8.GetBytes("deterministic test");

        var encrypted1 = _encryptor.Encrypt(plaintext, "tag:same", rowId: 42);
        var encrypted2 = _encryptor.Encrypt(plaintext, "tag:same", rowId: 42);

        // Deterministic nonce from rowKey + rowId → identical ciphertext
        Assert.Equal(encrypted1, encrypted2);
    }

    [Fact]
    public void DifferentTags_ProduceDifferentCiphertext()
    {
        var plaintext = Encoding.UTF8.GetBytes("same plaintext");

        var encrypted1 = _encryptor.Encrypt(plaintext, "tag:alpha", rowId: 1);
        var encrypted2 = _encryptor.Encrypt(plaintext, "tag:beta", rowId: 1);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void DifferentRowIds_ProduceDifferentCiphertext()
    {
        var plaintext = Encoding.UTF8.GetBytes("same plaintext");

        var encrypted1 = _encryptor.Encrypt(plaintext, "tag:same", rowId: 1);
        var encrypted2 = _encryptor.Encrypt(plaintext, "tag:same", rowId: 2);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    // ── Overhead ──

    [Fact]
    public void EncryptedSize_EqualsPlaintextPlusOverhead()
    {
        var plaintext = Encoding.UTF8.GetBytes("measure me");
        var encrypted = _encryptor.Encrypt(plaintext, "tag", rowId: 1);

        Assert.Equal(plaintext.Length + RowLevelEncryptor.Overhead, encrypted.Length);
    }

    // ── Tamper Detection ──

    [Fact]
    public void TamperedCiphertext_FailsAuthentication()
    {
        var plaintext = Encoding.UTF8.GetBytes("tamper test");
        var encrypted = _encryptor.Encrypt(plaintext, "tag", rowId: 1);

        // Flip a bit in the ciphertext region
        encrypted[RowLevelEncryptor.Overhead / 2] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            _encryptor.Decrypt(encrypted, "tag", rowId: 1));
    }

    // ── Edge Cases ──

    [Fact]
    public void Constructor_WrongKeySize_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RowLevelEncryptor(new byte[16]));
    }

    [Fact]
    public void Decrypt_TooSmallInput_Throws()
    {
        Assert.Throws<CryptographicException>(() =>
            _encryptor.Decrypt(new byte[5], "tag", rowId: 1));
    }

    [Fact]
    public void Dispose_ThenEncrypt_Throws()
    {
        var encryptor = new RowLevelEncryptor(new byte[32]);
        encryptor.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            encryptor.Encrypt(new byte[10], "tag", rowId: 1));
    }

    // ── Cross-Encryptor Consistency ──

    [Fact]
    public void TwoEncryptors_SameMasterKey_ProduceSameResult()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("cross-instance test");

        using var enc1 = new RowLevelEncryptor(key);
        using var enc2 = new RowLevelEncryptor(key);

        var encrypted = enc1.Encrypt(plaintext, "tag:shared", rowId: 10);
        var decrypted = enc2.Decrypt(encrypted, "tag:shared", rowId: 10);

        Assert.Equal(plaintext, decrypted);
    }
}
