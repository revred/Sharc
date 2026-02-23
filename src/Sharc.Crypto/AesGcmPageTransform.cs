// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using Sharc.Core;

namespace Sharc.Crypto;

/// <summary>
/// AES-256-GCM page transform for encrypting/decrypting database pages.
/// Each encrypted page: [12-byte nonce][ciphertext][16-byte auth tag].
/// </summary>
internal sealed class AesGcmPageTransform : IPageTransform, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly SharcKeyHandle _keyHandle;
    private bool _disposed;

    /// <summary>
    /// Overhead bytes per page (nonce + auth tag).
    /// </summary>
    public const int Overhead = NonceSize + TagSize; // 28 bytes

    /// <summary>
    /// Creates an AES-256-GCM page transform with the given key.
    /// </summary>
    public AesGcmPageTransform(SharcKeyHandle keyHandle)
    {
        _keyHandle = keyHandle ?? throw new ArgumentNullException(nameof(keyHandle));
    }

    /// <inheritdoc />
    public int TransformedPageSize(int rawPageSize) => rawPageSize + Overhead;

    /// <inheritdoc />
    public void TransformRead(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int expectedSize = destination.Length + Overhead;
        if (source.Length < expectedSize)
            throw new CryptographicException(
                $"Encrypted page too small: expected {expectedSize} bytes, got {source.Length}.");

        var nonce = source[..NonceSize];
        var ciphertext = source.Slice(NonceSize, destination.Length);
        var tag = source.Slice(NonceSize + destination.Length, TagSize);

        // AAD = page number as 4-byte big-endian (prevents page swapping)
        Span<byte> aad = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(aad, pageNumber);

        using var aes = new AesGcm(_keyHandle.AsSpan(), TagSize);
        aes.Decrypt(nonce, ciphertext, tag, destination, aad);
    }

    /// <inheritdoc />
    public void TransformWrite(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.Length < source.Length + Overhead)
            throw new ArgumentException("Destination buffer too small for encrypted output.");

        // Generate deterministic nonce from key + page number
        var nonceOut = destination[..NonceSize];
        var ciphertextOut = destination.Slice(NonceSize, source.Length);
        var tagOut = destination.Slice(NonceSize + source.Length, TagSize);

        // Nonce = HMAC-SHA256(key, page_number)[..12]
        Span<byte> pageBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pageBytes, pageNumber);
        var hmac = _keyHandle.ComputeHmac(pageBytes);
        hmac.AsSpan(0, NonceSize).CopyTo(nonceOut);

        // AAD = page number as 4-byte big-endian
        Span<byte> aad = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(aad, pageNumber);

        using var aes = new AesGcm(_keyHandle.AsSpan(), TagSize);
        aes.Encrypt(nonceOut, source, ciphertextOut, tagOut, aad);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }
}
