// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Fuzz;

/// <summary>
/// TD-9: Fuzz testing for EncryptionHeader parser.
/// Ensures header parsing handles adversarial inputs gracefully.
/// </summary>
public sealed class EncryptionHeaderFuzzTests
{
    private static readonly Random Rng = new(42);

    [Fact]
    public void Parse_RandomBytes_NeverCrashes()
    {
        var buffer = new byte[128];
        for (int trial = 0; trial < 200; trial++)
        {
            Rng.NextBytes(buffer);
            try
            {
                EncryptionHeader.Parse(buffer);
            }
            catch (InvalidOperationException)
            {
                // Expected — random bytes won't have valid magic
            }
        }
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var buffer = new byte[64]; // less than 128
        Assert.Throws<InvalidOperationException>(() => EncryptionHeader.Parse(buffer));
    }

    [Fact]
    public void Parse_ValidMagicRandomRest_DoesNotCrash()
    {
        var buffer = new byte[128];
        Rng.NextBytes(buffer);
        // Write valid magic "SHARC\0"
        buffer[0] = (byte)'S';
        buffer[1] = (byte)'H';
        buffer[2] = (byte)'A';
        buffer[3] = (byte)'R';
        buffer[4] = (byte)'C';
        buffer[5] = 0;
        var header = EncryptionHeader.Parse(buffer);
        Assert.Equal(32, header.Salt.Length);
        Assert.Equal(32, header.VerificationHash.Length);
    }

    [Fact]
    public void HasMagic_RandomBytes_NeverCrashes()
    {
        var buffer = new byte[6];
        for (int trial = 0; trial < 100; trial++)
        {
            Rng.NextBytes(buffer);
            bool result = EncryptionHeader.HasMagic(buffer);
            Assert.False(result); // random bytes won't match magic
        }
    }

    [Fact]
    public void HasMagic_TooShort_ReturnsFalse()
    {
        Assert.False(EncryptionHeader.HasMagic(ReadOnlySpan<byte>.Empty));
        Assert.False(EncryptionHeader.HasMagic(new byte[3]));
    }

    [Fact]
    public void HasMagic_ValidMagic_ReturnsTrue()
    {
        var buffer = new byte[] { (byte)'S', (byte)'H', (byte)'A', (byte)'R', (byte)'C', 0 };
        Assert.True(EncryptionHeader.HasMagic(buffer));
    }

    [Fact]
    public void Parse_AllZeros_ThrowsInvalidMagic()
    {
        var buffer = new byte[128];
        Assert.Throws<InvalidOperationException>(() => EncryptionHeader.Parse(buffer));
    }
}
