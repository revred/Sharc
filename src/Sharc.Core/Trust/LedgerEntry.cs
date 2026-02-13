using System.Buffers.Binary;

namespace Sharc.Core.Trust;

/// <summary>
/// A single entry in the Sharc distributed ledger.
/// </summary>
public record LedgerEntry(
    long SequenceNumber,
    long Timestamp,
    string AgentId,
    byte[] PayloadHash,
    byte[] PreviousHash,
    byte[] Signature);
