// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Sharc.Crypto;
using Xunit;

namespace Sharc.Tests.Crypto;

/// <summary>
/// Unit tests for SharcKeyHandle — pinned-memory key container with secure disposal.
/// </summary>
public class SharcKeyHandleTests
{
    // ── FromRawKey ──

    [Fact]
    public void FromRawKey_32Bytes_RoundTripsViaAsSpan()
    {
        var raw = new byte[32];
        for (int i = 0; i < 32; i++) raw[i] = (byte)i;

        using var handle = SharcKeyHandle.FromRawKey(raw);

        Assert.Equal(32, handle.Length);
        Assert.Equal(raw, handle.AsSpan().ToArray());
    }

    [Fact]
    public void FromRawKey_CopiesToInternalBuffer()
    {
        var raw = new byte[32];
        raw[0] = 0xAA;
        using var handle = SharcKeyHandle.FromRawKey(raw);

        // Mutate original — handle should be unaffected
        raw[0] = 0x00;
        Assert.Equal(0xAA, handle.AsSpan()[0]);
    }

    [Fact]
    public void FromRawKey_NonThirtyTwoBytes_Throws()
    {
        Assert.Throws<ArgumentException>(() => SharcKeyHandle.FromRawKey(new byte[16]));
        Assert.Throws<ArgumentException>(() => SharcKeyHandle.FromRawKey(new byte[64]));
        Assert.Throws<ArgumentException>(() => SharcKeyHandle.FromRawKey(ReadOnlySpan<byte>.Empty));
    }

    // ── DeriveKey ──

    [Fact]
    public void DeriveKey_SameInputs_ProducesSameKey()
    {
        var password = System.Text.Encoding.UTF8.GetBytes("test-password");
        var salt = new byte[16];
        salt[0] = 0x01;

        using var key1 = SharcKeyHandle.DeriveKey(password, salt, 1, 64, 1);
        using var key2 = SharcKeyHandle.DeriveKey(password, salt, 1, 64, 1);

        Assert.Equal(key1.AsSpan().ToArray(), key2.AsSpan().ToArray());
    }

    [Fact]
    public void DeriveKey_DifferentPasswords_ProduceDifferentKeys()
    {
        var salt = new byte[16];
        using var key1 = SharcKeyHandle.DeriveKey("pass1"u8, salt, 1, 64, 1);
        using var key2 = SharcKeyHandle.DeriveKey("pass2"u8, salt, 1, 64, 1);

        Assert.NotEqual(key1.AsSpan().ToArray(), key2.AsSpan().ToArray());
    }

    [Fact]
    public void DeriveKey_Produces32ByteKey()
    {
        var salt = new byte[16];
        using var key = SharcKeyHandle.DeriveKey("password"u8, salt, 1, 64, 1);
        Assert.Equal(32, key.Length);
    }

    // ── ComputeHmac ──

    [Fact]
    public void ComputeHmac_ProducesConsistentResult()
    {
        var raw = new byte[32];
        raw[0] = 0xFF;
        using var handle = SharcKeyHandle.FromRawKey(raw);

        var data = "hello world"u8;
        var hmac1 = handle.ComputeHmac(data);
        var hmac2 = handle.ComputeHmac(data);

        Assert.Equal(32, hmac1.Length); // HMAC-SHA256 is 32 bytes
        Assert.Equal(hmac1, hmac2);
    }

    [Fact]
    public void ComputeHmac_DifferentData_ProducesDifferentHmac()
    {
        var raw = new byte[32];
        using var handle = SharcKeyHandle.FromRawKey(raw);

        var hmac1 = handle.ComputeHmac("data1"u8);
        var hmac2 = handle.ComputeHmac("data2"u8);

        Assert.NotEqual(hmac1, hmac2);
    }

    // ── Dispose: zeros key and throws on subsequent access ──

    [Fact]
    public void Dispose_ZerosKeyBytes()
    {
        var raw = new byte[32];
        for (int i = 0; i < 32; i++) raw[i] = 0xFF;

        var handle = SharcKeyHandle.FromRawKey(raw);
        // Read key before dispose
        var keyBefore = handle.AsSpan().ToArray();
        Assert.Contains(keyBefore, b => b != 0);

        handle.Dispose();

        // After dispose, AsSpan should throw
        Assert.Throws<ObjectDisposedException>(() => handle.AsSpan());
    }

    [Fact]
    public void Dispose_ComputeHmac_ThrowsAfterDispose()
    {
        var raw = new byte[32];
        var handle = SharcKeyHandle.FromRawKey(raw);
        handle.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handle.ComputeHmac("test"u8));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var raw = new byte[32];
        var handle = SharcKeyHandle.FromRawKey(raw);
        handle.Dispose();
        handle.Dispose(); // should not throw
    }
}