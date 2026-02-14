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
        return _hmac.ComputeHash(data.ToArray());
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        var hash = _hmac.ComputeHash(data.ToArray());
        return hash.AsSpan().SequenceEqual(signature);
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
        using var hmac = new HMACSHA256(publicKey.ToArray());
        var hash = hmac.ComputeHash(data.ToArray());
        return hash.AsSpan().SequenceEqual(signature);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hmac.Dispose();
    }
}
