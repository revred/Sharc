// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Core;

/// <summary>
/// Transforms page data during read/write operations.
/// Primary use: encryption/decryption at the page level.
/// </summary>
public interface IPageTransform
{
    /// <summary>
    /// Calculates the on-disk size of a transformed page.
    /// For encryption, this includes nonce + ciphertext + auth tag.
    /// </summary>
    /// <param name="rawPageSize">The logical page size.</param>
    /// <returns>The physical size of a transformed page on disk.</returns>
    int TransformedPageSize(int rawPageSize);

    /// <summary>
    /// Transforms (decrypts) a page read from disk.
    /// </summary>
    /// <param name="source">The raw page bytes from disk (may include nonce + tag).</param>
    /// <param name="destination">Buffer for the decrypted page data.</param>
    /// <param name="pageNumber">1-based page number, used as associated data for AEAD.</param>
    void TransformRead(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber);

    /// <summary>
    /// Transforms (encrypts) a page for writing to disk.
    /// </summary>
    /// <param name="source">The plaintext page data.</param>
    /// <param name="destination">Buffer for the encrypted output.</param>
    /// <param name="pageNumber">1-based page number, used as associated data for AEAD.</param>
    void TransformWrite(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber);
}

/// <summary>
/// Identity transform Ã¢â‚¬â€ no-op pass-through for unencrypted databases.
/// </summary>
public sealed class IdentityPageTransform : IPageTransform
{
    /// <summary>Singleton instance.</summary>
    public static readonly IdentityPageTransform Instance = new();

    /// <inheritdoc />
    public int TransformedPageSize(int rawPageSize) => rawPageSize;

    /// <inheritdoc />
    public void TransformRead(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber)
    {
        source.CopyTo(destination);
    }

    /// <inheritdoc />
    public void TransformWrite(ReadOnlySpan<byte> source, Span<byte> destination, uint pageNumber)
    {
        source.CopyTo(destination);
    }
}