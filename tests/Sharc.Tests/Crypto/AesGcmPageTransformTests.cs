// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Crypto;

public class AesGcmPageTransformTests
{
    private const int PageSize = 4096;

    [Fact]
    public void RoundTrip_EncryptDecrypt_RecoversOriginalData()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        var plaintext = new byte[PageSize];
        RandomNumberGenerator.Fill(plaintext);

        var encrypted = new byte[transform.TransformedPageSize(PageSize)];
        transform.TransformWrite(plaintext, encrypted, pageNumber: 1);

        var decrypted = new byte[PageSize];
        transform.TransformRead(encrypted, decrypted, pageNumber: 1);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void TransformedPageSize_ReturnsPagePlusOverhead()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        Assert.Equal(PageSize + AesGcmPageTransform.Overhead, transform.TransformedPageSize(PageSize));
        Assert.Equal(PageSize + 28, transform.TransformedPageSize(PageSize));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        using var key1 = SharcKeyHandle.FromRawKey(GenerateKey());
        using var key2 = SharcKeyHandle.FromRawKey(GenerateKey());
        using var encryptor = new AesGcmPageTransform(key1);
        using var decryptor = new AesGcmPageTransform(key2);

        var plaintext = new byte[PageSize];
        plaintext[0] = 0x42;

        var encrypted = new byte[encryptor.TransformedPageSize(PageSize)];
        encryptor.TransformWrite(plaintext, encrypted, pageNumber: 1);

        var decrypted = new byte[PageSize];
        Assert.ThrowsAny<CryptographicException>(() =>
            decryptor.TransformRead(encrypted, decrypted, pageNumber: 1));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        var plaintext = new byte[PageSize];
        var encrypted = new byte[transform.TransformedPageSize(PageSize)];
        transform.TransformWrite(plaintext, encrypted, pageNumber: 1);

        // Tamper with ciphertext
        encrypted[20] ^= 0xFF;

        var decrypted = new byte[PageSize];
        Assert.ThrowsAny<CryptographicException>(() =>
            transform.TransformRead(encrypted, decrypted, pageNumber: 1));
    }

    [Fact]
    public void Decrypt_WrongPageNumber_ThrowsCryptographicException()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        var plaintext = new byte[PageSize];
        var encrypted = new byte[transform.TransformedPageSize(PageSize)];
        transform.TransformWrite(plaintext, encrypted, pageNumber: 1);

        // Decrypt with different page number (AAD mismatch)
        var decrypted = new byte[PageSize];
        Assert.ThrowsAny<CryptographicException>(() =>
            transform.TransformRead(encrypted, decrypted, pageNumber: 2));
    }

    [Fact]
    public void Encrypt_DifferentPages_ProduceDifferentCiphertext()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        var plaintext = new byte[PageSize]; // same content

        var encrypted1 = new byte[transform.TransformedPageSize(PageSize)];
        var encrypted2 = new byte[transform.TransformedPageSize(PageSize)];
        transform.TransformWrite(plaintext, encrypted1, pageNumber: 1);
        transform.TransformWrite(plaintext, encrypted2, pageNumber: 2);

        // Different page numbers produce different nonces and thus different ciphertext
        Assert.False(encrypted1.AsSpan().SequenceEqual(encrypted2));
    }

    [Fact]
    public void Decrypt_SourceTooSmall_ThrowsCryptographicException()
    {
        using var key = SharcKeyHandle.FromRawKey(GenerateKey());
        using var transform = new AesGcmPageTransform(key);

        var shortSource = new byte[10];
        var destination = new byte[PageSize];

        Assert.ThrowsAny<CryptographicException>(() =>
            transform.TransformRead(shortSource, destination, pageNumber: 1));
    }

    private static byte[] GenerateKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }
}
