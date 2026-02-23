// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Crypto;

public sealed class HkdfSha256Tests
{
    private static byte[] TestMasterKey => new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    [Fact]
    public void DeriveRowKey_ReturnsThirtyTwoBytes()
    {
        var key = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:acme");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveRowKey_SameInputs_ReturnsSameKey()
    {
        var key1 = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:acme");
        var key2 = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:acme");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveRowKey_DifferentTags_ReturnsDifferentKeys()
    {
        var key1 = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:acme");
        var key2 = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:beta");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveRowKey_DifferentMasterKeys_ReturnsDifferentKeys()
    {
        var masterKey2 = new byte[32];
        Array.Fill(masterKey2, (byte)0xFF);

        var key1 = HkdfSha256.DeriveRowKey(TestMasterKey, "tenant:acme");
        var key2 = HkdfSha256.DeriveRowKey(masterKey2, "tenant:acme");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveRowKey_EmptyTag_DoesNotThrow()
    {
        var key = HkdfSha256.DeriveRowKey(TestMasterKey, "");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveRowKey_UnicodeTag_ProducesValidKey()
    {
        var key = HkdfSha256.DeriveRowKey(TestMasterKey, "team:日本語");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveRowKey_ByteOverload_MatchesStringOverload()
    {
        var tag = "role:admin";
        var tagBytes = Encoding.UTF8.GetBytes(tag);

        var key1 = HkdfSha256.DeriveRowKey(TestMasterKey, tag);
        var key2 = HkdfSha256.DeriveRowKey(TestMasterKey, tagBytes);
        Assert.Equal(key1, key2);
    }
}
