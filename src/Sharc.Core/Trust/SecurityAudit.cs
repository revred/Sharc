namespace Sharc.Core.Trust;

/// <summary>
/// Categorizes security-relevant events in the Sharc trust layer.
/// </summary>
public enum SecurityEventType
{
    /// <summary>
    /// An agent attempted to register but failed validation (invalid signature or claim).
    /// </summary>
    RegistrationFailed,
    
    /// <summary>
    /// An agent registration was successful.
    /// </summary>
    RegistrationSuccess,

    /// <summary>
    /// A ledger append was rejected due to invalid authority or expiry.
    /// </summary>
    AppendRejected,

    /// <summary>
    /// A ledger append was successfully committed.
    /// </summary>
    AppendSuccess,

    /// <summary>
    /// A tamper attempt or data corruption was detected during integrity verification.
    /// </summary>
    IntegrityViolation,
    
    /// <summary>
    /// An import of external deltas failed verification.
    /// </summary>
    ImportRejected
}

/// <summary>
/// Arguments for the SecurityAudit event.
/// </summary>
public class SecurityEventArgs : EventArgs
{
    /// <summary>
    /// Gets the type of security event.
    /// </summary>
    public SecurityEventType EventType { get; }

    /// <summary>
    /// Gets the Agent ID involved in the event.
    /// </summary>
    public string AgentId { get; }

    /// <summary>
    /// Gets descriptive details about the event.
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Gets the timestamp of the event in Unix milliseconds.
    /// </summary>
    public long Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityEventArgs"/> class.
    /// </summary>
    /// <param name="eventType">The type of security event.</param>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="details">Event details.</param>
    public SecurityEventArgs(SecurityEventType eventType, string agentId, string details)
    {
        EventType = eventType;
        AgentId = agentId;
        Details = details;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
