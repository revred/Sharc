namespace Sharc.Trust;

/// <summary>
/// Provides cryptographic signatures for ledger entries.
/// </summary>
public interface ISharcSigner : IDisposable
{
    /// <summary>
    /// Gets the unique Agent ID associated with this signer.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Signs the given data.
    /// </summary>
    byte[] Sign(ReadOnlySpan<byte> data);

    /// <summary>
    /// Verifies the signature of the given data.
    /// </summary>
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);

    /// <summary>
    /// Gets the public key bytes for this signer.
    /// </summary>
    byte[] GetPublicKey();

    /// <summary>
    /// Gets the size of the signature produced by this signer in bytes.
    /// </summary>
    int SignatureSize { get; }

    /// <summary>
    /// Attempts to sign the given data into a destination span.
    /// </summary>
    bool TrySign(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten);
}
