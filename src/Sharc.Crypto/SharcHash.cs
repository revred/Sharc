using System.Security.Cryptography;

namespace Sharc.Crypto;

/// <summary>
/// SHA-256 hashing utility for consistent cryptographic summaries.
/// </summary>
public static class SharcHash
{
    /// <summary>
    /// Computes the SHA-256 hash of the given data.
    /// </summary>
    public static byte[] Compute(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Attempts to compute the SHA-256 hash into a destination span.
    /// </summary>
    public static bool TryCompute(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten)
    {
        return SHA256.TryHashData(data, destination, out bytesWritten);
    }
}
