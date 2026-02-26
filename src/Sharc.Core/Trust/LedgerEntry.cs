// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Trust;

/// <summary>
/// A single entry in the Sharc distributed ledger.
/// </summary>
/// <param name="SequenceNumber">Monotonically increasing sequence within the ledger.</param>
/// <param name="Timestamp">Unix timestamp in milliseconds when the entry was created.</param>
/// <param name="AgentId">Identifier of the agent that authored this entry.</param>
/// <param name="Payload">The raw payload bytes committed to the ledger.</param>
/// <param name="PayloadHash">SHA-256 hash of the payload for integrity verification.</param>
/// <param name="PreviousHash">Hash of the preceding entry, forming the hash chain.</param>
/// <param name="Signature">ECDSA signature over the entry by the authoring agent.</param>
public record LedgerEntry(
    long SequenceNumber,
    long Timestamp,
    string AgentId,
    byte[] Payload,
    byte[] PayloadHash,
    byte[] PreviousHash,
    byte[] Signature);
