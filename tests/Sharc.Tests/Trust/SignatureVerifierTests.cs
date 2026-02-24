// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Sharc.Core.Trust;
using Sharc.Trust;
using Xunit;

namespace Sharc.Tests.Trust;

public class SignatureVerifierTests
{
    [Fact]
    public void Verify_HmacSha256_ValidSignature_ReturnsTrue()
    {
        using var signer = new SharcSigner("hmac-test-agent");
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = signer.Sign(data);
        byte[] publicKey = signer.GetPublicKey();

        bool result = SignatureVerifier.Verify(data, signature, publicKey, SignatureAlgorithm.HmacSha256);

        Assert.True(result);
    }

    [Fact]
    public void Verify_HmacSha256_InvalidSignature_ReturnsFalse()
    {
        using var signer = new SharcSigner("hmac-test-agent");
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = signer.Sign(data);
        byte[] publicKey = signer.GetPublicKey();

        // Corrupt the signature
        signature[0] ^= 0xFF;

        bool result = SignatureVerifier.Verify(data, signature, publicKey, SignatureAlgorithm.HmacSha256);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EcdsaP256_ValidSignature_ReturnsTrue()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        bool result = SignatureVerifier.Verify(data, signature, publicKey, SignatureAlgorithm.EcdsaP256);

        Assert.True(result);
    }

    [Fact]
    public void Verify_EcdsaP256_InvalidSignature_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        // Corrupt the signature
        signature[0] ^= 0xFF;

        bool result = SignatureVerifier.Verify(data, signature, publicKey, SignatureAlgorithm.EcdsaP256);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EcdsaP256_WrongKey_ReturnsFalse()
    {
        using var ecdsa1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = ecdsa1.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] wrongPublicKey = ecdsa2.ExportSubjectPublicKeyInfo();

        bool result = SignatureVerifier.Verify(data, signature, wrongPublicKey, SignatureAlgorithm.EcdsaP256);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EcdsaP256_TamperedData_ReturnsFalse()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = "hello world"u8.ToArray();
        byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        // Tamper with the data
        data[0] ^= 0xFF;

        bool result = SignatureVerifier.Verify(data, signature, publicKey, SignatureAlgorithm.EcdsaP256);

        Assert.False(result);
    }

    [Fact]
    public void Verify_UnknownAlgorithm_ReturnsFalse()
    {
        byte[] data = "hello"u8.ToArray();
        byte[] signature = new byte[32];
        byte[] publicKey = new byte[32];

        bool result = SignatureVerifier.Verify(data, signature, publicKey, (SignatureAlgorithm)255);

        Assert.False(result);
    }

    [Fact]
    public void Verify_EcdsaP256_SignatureIs64Bytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = "test data for signature length check"u8.ToArray();
        byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public void Verify_HmacSha256_SignatureIs32Bytes()
    {
        using var signer = new SharcSigner("hmac-size-test");
        byte[] data = "test data for signature length check"u8.ToArray();
        byte[] signature = signer.Sign(data);

        Assert.Equal(32, signature.Length);
    }
}
