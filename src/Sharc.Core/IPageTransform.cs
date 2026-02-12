/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
/// Identity transform — no-op pass-through for unencrypted databases.
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
