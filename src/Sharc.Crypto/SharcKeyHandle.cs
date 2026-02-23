// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Sharc.Crypto;

/// <summary>
/// Holds a derived encryption key in pinned memory that is zeroed on disposal.
/// Prevents the GC from relocating key material and ensures cleanup.
/// </summary>
internal sealed class SharcKeyHandle : IDisposable
{
    private readonly byte[] _key;
    private readonly GCHandle _pin;
    private bool _disposed;

    /// <summary>Key length in bytes.</summary>
    public int Length => _key.Length;

    private SharcKeyHandle(byte[] key)
    {
        _key = key;
        _pin = GCHandle.Alloc(_key, GCHandleType.Pinned);
    }

    /// <summary>
    /// Creates a key handle from a password and KDF parameters.
    /// </summary>
    public static SharcKeyHandle DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt,
        int timeCost, int memoryCostKiB, int parallelism)
    {
        var key = new byte[32];
        // Argon2id is not available in base .NET; use PBKDF2 with SHA-512 as a bridge.
        // DECISION: Using PBKDF2-SHA512 (built-in .NET). Argon2id deferred until
        // a zero-dependency managed implementation is available.
        var iterations = Math.Max(timeCost * 100_000, 100_000);
        Rfc2898DeriveBytes.Pbkdf2(password, salt, key, iterations, HashAlgorithmName.SHA512);
        return new SharcKeyHandle(key);
    }

    /// <summary>
    /// Creates a key handle from an already-derived raw key (for testing).
    /// </summary>
    internal static SharcKeyHandle FromRawKey(ReadOnlySpan<byte> rawKey)
    {
        if (rawKey.Length != 32)
            throw new ArgumentException("Key must be exactly 32 bytes.", nameof(rawKey));
        var key = new byte[32];
        rawKey.CopyTo(key);
        return new SharcKeyHandle(key);
    }

    /// <summary>
    /// Provides read-only access to the key bytes.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key;
    }

    /// <summary>
    /// Computes HMAC-SHA256(key, data) for key verification.
    /// </summary>
    public byte[] ComputeHmac(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return HMACSHA256.HashData(_key, data);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
        _pin.Free();
    }
}
