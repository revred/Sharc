namespace Sharc.Core.Trust;

/// <summary>
/// Defines the trust level and capabilities of an agent.
/// </summary>
public enum AgentClass : byte
{
    /// <summary>
    /// Root authority. Can issue any certificate within its ceiling.
    /// </summary>
    Root = 1,

    /// <summary>
    /// Intermediate authority. Can issue user certificates but cannot create other CAs unless authorized.
    /// </summary>
    Intermediate = 2,

    /// <summary>
    /// End-user or leaf agent. Cannot issue certificates.
    /// </summary>
    User = 3
}
