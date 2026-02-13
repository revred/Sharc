using System.Security.Cryptography;

namespace Sharc.Trust;

/// <summary>
/// ECDsa P-256 implementation of <see cref="ISharcSigner"/>.
/// </summary>
public sealed class SharcSigner : ISharcSigner, IDisposable
{
    private readonly ECDsa _dsa;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharcSigner"/> class.
    /// </summary>
    /// <param name="agentId">The unique ID of the agent.</param>
    public SharcSigner(string agentId)
    {
        AgentId = agentId;
        _dsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <inheritdoc />
    public string AgentId { get; }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        return _dsa.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        return _dsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc />
    public byte[] GetPublicKey()
    {
        return _dsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// Verifies a signature using the provided public key.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        using var dsa = ECDsa.Create();
        dsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        return dsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dsa.Dispose();
    }
}
