using System.Security.Cryptography;
using Sharc.Crypto;

namespace Sharc.Trust;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ISharcSigner"/> for Wasm compatibility.
/// </summary>
public sealed class SharcSigner : ISharcSigner, IDisposable
{
    private readonly HMACSHA256 _hmac;
    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharcSigner"/> class.
    /// </summary>
    /// <param name="agentId">The unique ID of the agent.</param>
    public SharcSigner(string agentId)
    {
        AgentId = agentId;
        // Use deterministic key from AgentId for the demo
        _key = SharcHash.Compute(System.Text.Encoding.UTF8.GetBytes(agentId));
        _hmac = new HMACSHA256(_key);
    }

    /// <inheritdoc />
    public string AgentId { get; }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        byte[] result = new byte[SignatureSize];
        _hmac.TryComputeHash(data, result, out _);
        return result;
    }

    /// <inheritdoc />
    public int SignatureSize => 32; // HMACSHA256 size

    /// <inheritdoc />
    public bool TrySign(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten)
    {
        return _hmac.TryComputeHash(data, destination, out bytesWritten);
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        Span<byte> hash = stackalloc byte[SignatureSize];
        _hmac.TryComputeHash(data, hash, out int written);
        return hash.Slice(0, written).SequenceEqual(signature);
    }

    /// <inheritdoc />
    public byte[] GetPublicKey()
    {
        // For HMAC demo, "public key" is the shared secret derived from ID
        return _key;
    }

    /// <summary>
    /// Verifies a signature using the provided public key.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[32]; // HMACSHA256 output is always 32 bytes
        HMACSHA256.HashData(publicKey, data, hash);
        return hash.SequenceEqual(signature);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hmac.Dispose();
    }
}
