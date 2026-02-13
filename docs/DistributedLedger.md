# Distributed Ledger — Sharc

The Sharc Distributed Ledger provides a high-performance, tamper-evident audit trail for AI agent interactions and context sharing. It is built directly on Sharc's B-tree layer for maximum efficiency.

---

## 1. Overview

The ledger is implemented as a specialized table (`_sharc_ledger`) containing a cryptographically linked chain of records. Each entry attests to a specific piece of context, signed by an agent, and linked to the previous state of the ledger.

### Key Capabilities
- **Hash-Chain Integrity**: Each entry links to the `PayloadHash` of the previous entry.
- **Cryptographic Attribution**: Entries are signed using ECDsa (P-256) by the source agent.
- **Verifiable Provenance**: One-pass scan verifies the entire history of modifications.
- **Acid Compliance**: Ledger writes enjoy full atomicity and durability via Sharc's ACID engine.

---

## 2. Table Schema

| Column | Type | Description |
|--------|------|-------------|
| `SequenceNumber` | INTEGER (PK) | Monotonically increasing index starting at 1. |
| `Timestamp` | INTEGER | Unix epoch (milliseconds) of the entry creation. |
| `AgentId` | TEXT | Unique identifier of the agent providing the context. |
| `PayloadHash` | BLOB (32 bytes)| SHA-256 hash of the context payload. |
| `PreviousHash` | BLOB (32 bytes)| `PayloadHash` of the entry with `SequenceNumber - 1`. |
| `Signature` | BLOB | ECDsa signature of `(PrevHash + PayloadHash + Seq)`. |

---

## 3. Cryptographic Specification

### Hashing
- **Algorithm**: SHA-256
- **Input**: The raw bytes of the context payload being shared.

### Signing (Atestation)
- **Algorithm**: ECDsa using the nistP256 (P-256) curve.
- **Encoding**: SubjectPublicKeyInfo for public keys; standard DER/RAW for signatures.
- **Data Signed**: 
  `Signature = Sign(PrevHash || PayloadHash || Uint64BE(SequenceNumber))`

---

## 4. Verification Logic

The `LedgerManager.VerifyIntegrity()` method performs a strict validation of the chain:

1. **Sequence Check**: Ensures `SequenceNumber` is strictly sequential with no gaps or overlaps.
2. **Hash Linkage**: Validates that `PreviousHash[N]` matches `PayloadHash[N-1]`.
3. **Attribution Check**: (When public keys are provided) Verifies that the `Signature` is valid for the given `AgentId` and data payload.

---

## 5. Performance Considerations

- **Direct B-Tree Writes**: The ledger bypasses high-level SQL parsing, writing directly to leaf pages via `LedgerManager.Append`.
- **Zero-Alloc Scan**: Integrity verification uses `SharcDataReader` with pooled buffers to ensure high-performance audit scans in resource-constrained environments (e.g., WASM).
- **Page-Level Security**: Since the ledger uses standard Sharc pages, it can be transparently encrypted via `AesGcmPageTransform`.
