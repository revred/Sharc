namespace Sharc.Core.Trust;

/// <summary>
/// Represents registry information for an agent.
/// </summary>
/// <param name="AgentId">Unique identifier of the agent.</param>
/// <param name="PublicKey">The agent's public key (SubjectPublicKeyInfo).</param>
/// <param name="ValidityStart">Unix epoch (ms) when the key becomes valid.</param>
/// <param name="ValidityEnd">Unix epoch (ms) when the key expires (0 for never).</param>
/// <param name="Signature">Self-signature of (AgentId + PublicKey + ValidityStart + ValidityEnd).</param>
public record AgentInfo(
    string AgentId,
    byte[] PublicKey,
    long ValidityStart,
    long ValidityEnd,
    byte[] Signature);
