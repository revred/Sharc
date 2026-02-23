// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Crypto;

public class EncryptionHeaderTests
{
    [Fact]
    public void HasMagic_ValidMagic_ReturnsTrue()
    {
        var data = new byte[128];
        "SHARC\0"u8.CopyTo(data);
        Assert.True(EncryptionHeader.HasMagic(data));
    }

    [Fact]
    public void HasMagic_InvalidMagic_ReturnsFalse()
    {
        var data = new byte[128];
        "SQLite"u8.CopyTo(data);
        Assert.False(EncryptionHeader.HasMagic(data));
    }

    [Fact]
    public void HasMagic_TooShort_ReturnsFalse()
    {
        Assert.False(EncryptionHeader.HasMagic(new byte[3]));
    }

    [Fact]
    public void Parse_ValidHeader_ExtractsAllFields()
    {
        var salt = new byte[32];
        salt[0] = 0xAA;
        var verification = new byte[32];
        verification[0] = 0xBB;

        var data = new byte[128];
        EncryptionHeader.Write(data, kdfAlgorithm: 1, cipherAlgorithm: 1,
            timeCost: 3, memoryCostKiB: 65536, parallelism: 4,
            salt, verification, pageSize: 4096, pageCount: 100);

        var header = EncryptionHeader.Parse(data);

        Assert.Equal(1, header.KdfAlgorithm);
        Assert.Equal(1, header.CipherAlgorithm);
        Assert.Equal(3, header.TimeCost);
        Assert.Equal(65536, header.MemoryCostKiB);
        Assert.Equal(4, header.Parallelism);
        Assert.Equal(0xAA, header.Salt.Span[0]);
        Assert.Equal(0xBB, header.VerificationHash.Span[0]);
        Assert.Equal(4096, header.PageSize);
        Assert.Equal(100, header.PageCount);
        Assert.Equal(1, header.VersionMajor);
        Assert.Equal(0, header.VersionMinor);
    }

    [Fact]
    public void Parse_InvalidMagic_Throws()
    {
        var data = new byte[128];
        Assert.Throws<InvalidOperationException>(() => EncryptionHeader.Parse(data));
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var data = new byte[64];
        "SHARC\0"u8.CopyTo(data);
        Assert.Throws<InvalidOperationException>(() => EncryptionHeader.Parse(data));
    }

    [Fact]
    public void Write_RoundTrip_PreservesAllFields()
    {
        var salt = new byte[32];
        var verification = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            salt[i] = (byte)i;
            verification[i] = (byte)(255 - i);
        }

        var buffer = new byte[128];
        EncryptionHeader.Write(buffer, kdfAlgorithm: 2, cipherAlgorithm: 2,
            timeCost: 7, memoryCostKiB: 131072, parallelism: 8,
            salt, verification, pageSize: 8192, pageCount: 500);

        var header = EncryptionHeader.Parse(buffer);

        Assert.Equal(2, header.KdfAlgorithm);
        Assert.Equal(2, header.CipherAlgorithm);
        Assert.Equal(7, header.TimeCost);
        Assert.Equal(131072, header.MemoryCostKiB);
        Assert.Equal(8, header.Parallelism);
        Assert.Equal(8192, header.PageSize);
        Assert.Equal(500, header.PageCount);
        Assert.True(header.Salt.Span.SequenceEqual(salt));
        Assert.True(header.VerificationHash.Span.SequenceEqual(verification));
    }
}
