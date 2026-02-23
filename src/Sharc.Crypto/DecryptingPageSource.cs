// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Core;

namespace Sharc.Crypto;

/// <summary>
/// Wraps an inner <see cref="IPageSource"/> that contains encrypted pages,
/// decrypting each page on read using an <see cref="IPageTransform"/>.
/// </summary>
/// <remarks>
/// The inner source reads raw encrypted bytes (nonce + ciphertext + tag).
/// This source returns decrypted plaintext pages of the logical page size.
/// The encrypted file starts after the 128-byte Sharc encryption header,
/// so page offsets are adjusted by <see cref="EncryptionHeader.HeaderSize"/>.
/// </remarks>
internal sealed class DecryptingPageSource : IPageSource
{
    private readonly IPageSource _inner;
    private readonly IPageTransform _transform;
    private readonly int _logicalPageSize;
    private readonly byte[] _decryptBuffer;
    private bool _disposed;

    /// <inheritdoc />
    public int PageSize => _logicalPageSize;

    /// <inheritdoc />
    public int PageCount => _inner.PageCount;

    /// <summary>
    /// Creates a decrypting page source.
    /// </summary>
    /// <param name="inner">The inner page source reading encrypted page data.</param>
    /// <param name="transform">The page transform for decryption.</param>
    /// <param name="logicalPageSize">The plaintext SQLite page size.</param>
    public DecryptingPageSource(IPageSource inner, IPageTransform transform, int logicalPageSize)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        _logicalPageSize = logicalPageSize;
        _decryptBuffer = new byte[logicalPageSize];
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var encrypted = _inner.GetPage(pageNumber);
        _transform.TransformRead(encrypted, _decryptBuffer, pageNumber);
        return _decryptBuffer;
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetPageMemory(uint pageNumber)
    {
        return GetPage(pageNumber).ToArray();
    }

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var encrypted = _inner.GetPage(pageNumber);
        _transform.TransformRead(encrypted, destination[.._logicalPageSize], pageNumber);
        return _logicalPageSize;
    }

    /// <inheritdoc />
    public void Invalidate(uint pageNumber) => _inner.Invalidate(pageNumber);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inner.Dispose();
    }
}
