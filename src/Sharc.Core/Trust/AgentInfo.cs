namespace Sharc.Core.Trust;

/// <summary>
/// Represents registry information for an agent.
/// </summary>
/// <param name="AgentId">Unique identifier of the agent.</param>
/// <param name="Class">Trust classification of the agent.</param>
/// <param name="PublicKey">The agent's public key (SubjectPublicKeyInfo).</param>
/// <param name="AuthorityCeiling">Maximum economic value the agent can authorize.</param>
/// <param name="WriteScope">Scope string defining write permissions.</param>
/// <param name="ReadScope">Scope string defining read permissions.</param>
/// <param name="ValidityStart">Unix epoch (ms) when the key becomes valid.</param>
/// <param name="ValidityEnd">Unix epoch (ms) when the key expires (0 for never).</param>
/// <param name="ParentAgent">Agent ID of the parent CA (if any).</param>
/// <param name="CoSignRequired">True if actions require co-signatures.</param>
/// <param name="Signature">Self-signature of (AgentId + PublicKey + ValidityStart + ValidityEnd).</param>
public record AgentInfo(
    string AgentId,
    AgentClass Class,
    byte[] PublicKey,
    ulong AuthorityCeiling,
    string WriteScope,
    string ReadScope,
    long ValidityStart,
    long ValidityEnd,
    string ParentAgent,
    bool CoSignRequired,
    byte[] Signature);
