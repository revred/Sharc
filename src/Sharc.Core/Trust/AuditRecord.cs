namespace Sharc.Core.Trust;

/// <summary>
/// Represents a tamper-evident audit log entry.
/// </summary>
/// <param name="EventId">The unique, auto-incrementing ID of the event.</param>
/// <param name="Timestamp">The timestamp of the event in Unix microseconds.</param>
/// <param name="EventType">The classification of the security event.</param>
/// <param name="AgentId">The agent identifier associated with the event.</param>
/// <param name="Details">Descriptive details about the event.</param>
/// <param name="PreviousHash">The hash of the previous audit record (for chaining).</param>
/// <param name="Hash">The hash of this record (including PreviousHash).</param>
public record AuditRecord(
    long EventId,
    long Timestamp,
    SecurityEventType EventType,
    string AgentId,
    string Details,
    byte[] PreviousHash,
    byte[] Hash
);
